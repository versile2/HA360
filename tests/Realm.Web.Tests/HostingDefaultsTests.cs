using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;
using Realm.TestKit;
using Realm.Web.Hosting;
using Xunit;

namespace Realm.Web.Tests;

/// <summary>
/// The hosting defaults the architecture asks of the process (CR2-010): the shutdown budget of 03 section 2.13, cleartext HTTP/1.1 (5.1), the Data Protection key
/// ring beside the database (5.7), <c>AllowedHosts</c> (5.4) and the quiet framework categories and single-line UTC console of 9.1. Each runs on the same real Kestrel
/// host the other tests use, composed by <c>AddRealmApp</c> like <c>Program.cs</c>.
/// </summary>
public sealed partial class HostingDefaultsTests : IDisposable
{
    private const string FictionalToken = "fictional-test-token-0003";

    private readonly List<string> _directories = [];

    public void Dispose()
    {
        foreach (var directory in _directories)
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
                // A host that is still stopping may hold a file; the temp directory is not worth a failed test.
            }
        }
    }

    // ---- 03 section 2.13: the shutdown budget -------------------------------------------------------------------------

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TheShutdownTimeout_IsTwentyFiveSeconds_InDemoAndInLive(bool live)
    {
        await using var host = await RealmTestHost.StartAsync(settings: live ? LiveSettings() : null);

        var options = host.Services.GetRequiredService<IOptions<HostOptions>>().Value;

        Assert.Equal(TimeSpan.FromSeconds(25), options.ShutdownTimeout);   // the Supervisor's timeout is 30 s, so a slow drain ends before its SIGKILL
        Assert.True(options.ShutdownTimeout < TimeSpan.FromSeconds(30));
    }

    // ---- 03 section 5.1: HTTP/2 off ------------------------------------------------------------------------------------

    [Fact]
    public void EveryEndpoint_IsCleartextHttp1()
    {
        var provider = new ServiceCollection()
            .AddRealmHosting(new RuntimeOptions(RealmMode.Demo, DetailedErrors: true))
            .BuildServiceProvider();
        var kestrel = provider.GetRequiredService<IOptions<KestrelServerOptions>>().Value;
        var seen = new List<HttpProtocols>();

        kestrel.ListenAnyIP(8099, listen => seen.Add(listen.Protocols));
        kestrel.ListenLocalhost(8100, listen => seen.Add(listen.Protocols));

        Assert.Equal(new[] { HttpProtocols.Http1, HttpProtocols.Http1 }, seen);
    }

    // ---- 03 section 9.1 and 5.4: appsettings.json ----------------------------------------------------------------------

    [Theory]
    [InlineData("Microsoft.AspNetCore")]
    [InlineData("Microsoft.AspNetCore.SignalR")]
    [InlineData("Microsoft.AspNetCore.Components")]
    [InlineData("Microsoft.EntityFrameworkCore")]
    [InlineData("System.Net.Http.HttpClient")]
    public async Task TheNoisyFrameworkCategories_ArePinnedAtWarning(string category)
    {
        await using var host = await RealmTestHost.StartAsync();

        var configuration = host.Services.GetRequiredService<IConfiguration>();

        Assert.Equal("Warning", configuration[$"Logging:LogLevel:{category}"]);
    }

    [Fact]
    public async Task AllowedHosts_IsAny_BecauseTheHostAfterTheForwardedHeadersIsTheExternalOne()
    {
        await using var host = await RealmTestHost.StartAsync();

        Assert.Equal("*", host.Services.GetRequiredService<IConfiguration>()["AllowedHosts"]);
    }

    // A request on the host that reads the app's appsettings.json writes no Information line of the hosting diagnostics; the same request on a host that
    // does not (the control) writes them, so the file, and not the absence of a provider or an unserved request, is what removes them.
    [Fact]
    public async Task ARequest_WritesNoHostingDiagnosticsInformationLines()
    {
        var pinned = new InMemoryLogSink();
        await using (var host = await RealmTestHost.StartAsync(pinned))
        {
            await GetHealthzAsync(host);
        }

        var unpinned = new InMemoryLogSink();
        await using (var host = await RealmTestHost.StartAsync(unpinned, appSettings: false))
        {
            await GetHealthzAsync(host);
        }

        Assert.DoesNotContain(pinned.Entries, IsHostingDiagnosticsInformation);
        Assert.Contains(unpinned.Entries, IsHostingDiagnosticsInformation);
        Assert.Contains(pinned.Entries, entry => entry.Category == "Microsoft.Hosting.Lifetime");   // the start-up lines the Log tab does want are still there
    }

    // ---- 03 section 9.1: the console -----------------------------------------------------------------------------------

    [Fact]
    public void TheConsole_IsTheSimpleFormatter_WithOneLineAndUtcTimestamps()
    {
        using var provider = new ServiceCollection().AddLogging(logging => logging.AddRealmConsole()).BuildServiceProvider();

        var console = provider.GetRequiredService<IOptions<ConsoleLoggerOptions>>().Value;
        var simple = provider.GetRequiredService<IOptions<SimpleConsoleFormatterOptions>>().Value;

        Assert.Equal(ConsoleFormatterNames.Simple, console.FormatterName);
        Assert.True(simple.SingleLine);
        Assert.True(simple.UseUtcTimestamp);
        Assert.Equal("yyyy-MM-ddTHH:mm:ss.fffZ ", simple.TimestampFormat);
    }

    // The lines themselves: the timestamp first and in UTC, an exception on the same line as its message.
    [Fact]
    public void ALogLine_IsOneLineWithAUtcTimestampFirst()
    {
        using var provider = new ServiceCollection().AddLogging(logging => logging.AddRealmConsole()).BuildServiceProvider();
        var formatter = provider.GetServices<ConsoleFormatter>().Single(candidate => candidate.Name == ConsoleFormatterNames.Simple);
        var writer = new StringWriter();
        var failure = new InvalidOperationException("first line" + Environment.NewLine + "second line");

        formatter.Write(new LogEntry<string>(LogLevel.Warning, "Realm.Test", new EventId(7), "message", failure, (state, _) => state), null, writer);

        var line = AnsiEscape().Replace(writer.ToString(), string.Empty);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z \s*warn: Realm\.Test\[7\] message ", line);
        Assert.Single(line.Split('\n', StringSplitOptions.RemoveEmptyEntries));
        Assert.Contains("first line", line, StringComparison.Ordinal);
        Assert.Contains("second line", line, StringComparison.Ordinal);
    }

    // ---- 03 section 5.7: Data Protection --------------------------------------------------------------------------------

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TheKeyRing_IsPersistedBesideTheDatabase_InLiveAndInDemo(bool live)
    {
        var data = NewDirectory();
        var settings = live ? LiveSettings() : new Dictionary<string, string?>();
        settings["Realm:Db"] = Path.Combine(data, "realm.db");
        await using var host = await RealmTestHost.StartAsync(settings: settings);

        host.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("test").Protect("a value");   // the first use creates the first key

        Assert.NotEmpty(Directory.GetFiles(Path.Combine(data, "dp-keys"), "key-*.xml"));
    }

    [Fact]
    public void TheKeyDirectory_IsBesideTheDatabase_InLiveAndNeverInDataForDemo()
    {
        var live = new RuntimeOptions(RealmMode.Live, DetailedErrors: false);
        var demo = new RuntimeOptions(RealmMode.Demo, DetailedErrors: true);
        var data = Path.GetFullPath("/data");
        var home = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "realm-dev"));

        Assert.Equal(Path.Combine(data, "dp-keys"), RealmHostingExtensions.DataProtectionDirectory(live, Configure(("Realm:Db", Path.Combine(data, "realm.db")))));
        Assert.Equal(Path.Combine(data, "dp-keys"), RealmHostingExtensions.DataProtectionDirectory(live, Configure(("REALM_DB", Path.Combine(data, "realm.db")), ("Realm:Db", "/elsewhere/x.db"))));
        Assert.Equal(Path.Combine(home, "dp-keys"), RealmHostingExtensions.DataProtectionDirectory(demo, Configure(("Realm:Db", Path.Combine(home, "realm.db")))));
        Assert.Null(RealmHostingExtensions.DataProtectionDirectory(demo, Configure()));   // no database in Demo: the framework's user-profile location, never /data
        Assert.Null(RealmHostingExtensions.DataProtectionDirectory(demo, null));
    }

    [Fact]
    public async Task AKeyDirectoryThatCannotBeWritten_IsAWarning_AndTheHostStillStarts()
    {
        var data = NewDirectory();
        var blocker = Path.Combine(data, "a-file");
        await File.WriteAllTextAsync(blocker, "not a directory");   // a directory cannot be created below a regular file
        var logs = new InMemoryLogSink();
        var settings = new Dictionary<string, string?> { ["Realm:Db"] = Path.Combine(blocker, "below", "realm.db") };

        await using var host = await RealmTestHost.StartAsync(logs, settings);
        using var client = host.CreateClient();
        using var response = await client.GetAsync("healthz");

        Assert.True(response.IsSuccessStatusCode);
        var warning = Assert.Single(logs.Entries, entry => entry.Level == LogLevel.Warning && entry.Message.Contains("Data Protection key directory", StringComparison.Ordinal));
        Assert.Contains("keys stay in memory", warning.Message, StringComparison.Ordinal);
    }

    private static async Task GetHealthzAsync(KestrelHost host)
    {
        using var client = host.CreateClient();
        using var response = await client.GetAsync("healthz");
        response.EnsureSuccessStatusCode();
    }

    private static bool IsHostingDiagnosticsInformation(LogEntry entry) =>
        entry.Category == "Microsoft.AspNetCore.Hosting.Diagnostics" && entry.Level < LogLevel.Warning;

    private static Dictionary<string, string?> LiveSettings() => new() { ["SUPERVISOR_TOKEN"] = FictionalToken };

    private static IConfiguration Configure(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(pair => pair.Key, pair => (string?)pair.Value)).Build();

    private string NewDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "realm-hosting-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        _directories.Add(directory);
        return directory;
    }

    [GeneratedRegex(@"\x1b\[[0-9;]*m")]
    private static partial Regex AnsiEscape();
}
