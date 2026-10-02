using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Realm.Infrastructure.Hosting;

namespace Realm.Web.Hosting;

/// <summary>
/// The process settings the architecture asks of the host: the shutdown budget (03 section 2.13), Kestrel's cleartext HTTP/1.1 (5.1), the persisted
/// Data Protection key ring (5.7) and the single-line UTC console (9.1). <see cref="AddRealmHosting"/> is part of <c>AddRealmApp</c>, so the Kestrel test
/// host gets exactly what <c>Program.cs</c> gets; the console is a logging-builder call because a test host chooses its own log providers.
/// </summary>
public static class RealmHostingExtensions
{
    /// <summary>
    /// <c>HostOptions.ShutdownTimeout</c>: shorter than the Supervisor's <c>timeout: 30</c> (config.yaml), so a slow drain of the database writer ends in a
    /// logged failure of ours and not in the Supervisor's SIGKILL, which would leave <c>meta.clean_shutdown</c> at 0 (03 section 2.13).
    /// </summary>
    public static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(25);

    /// <summary>The application name that isolates this app's key ring (03 section 5.7).</summary>
    public const string DataProtectionApplicationName = "realm";

    /// <summary>The key ring's directory, beside the database file.</summary>
    public const string DataProtectionDirectoryName = "dp-keys";

    /// <summary>The timestamp of a console line: UTC, millisecond, with the space that separates it from the level (03 section 9.1).</summary>
    public const string ConsoleTimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ ";

    /// <summary>
    /// Sets the shutdown timeout, switches HTTP/2 off on every endpoint and persists the Data Protection keys (<see cref="DataProtectionDirectory"/>); a
    /// directory that cannot be written is a warning in the log and an in-memory key ring, never a failed start (03 section 5.7).
    /// </summary>
    /// <param name="configuration">Where <c>REALM_DB</c> and <c>Realm:Db</c> live; null in a Demo host that is composed without it, which keeps the framework's own key location.</param>
    public static IServiceCollection AddRealmHosting(this IServiceCollection services, RuntimeOptions runtime, IConfiguration? configuration = null)
    {
        services.Configure<HostOptions>(options => options.ShutdownTimeout = ShutdownTimeout);
        services.Configure<KestrelServerOptions>(ConfigureKestrel);

        var keys = services.AddDataProtection().SetApplicationName(DataProtectionApplicationName);
        var (directory, problem) = PrepareDataProtectionDirectory(runtime, configuration);
        if (problem is not null)
        {
            services.AddHostedService(provider => new DataProtectionReport(directory ?? string.Empty, problem, provider.GetRequiredService<ILogger<DataProtectionReport>>()));
        }
        else if (directory is not null)
        {
            keys.PersistKeysToFileSystem(new DirectoryInfo(directory));
        }

        return services;
    }

    /// <summary>Cleartext HTTP/1.1 only, on every endpoint: the ingress proxy speaks HTTP/1.1 and TLS ends at Home Assistant (03 section 5.1).</summary>
    public static void ConfigureKestrel(KestrelServerOptions options) =>
        options.ConfigureEndpointDefaults(listen => listen.Protocols = HttpProtocols.Http1);

    /// <summary>
    /// The built-in simple console with one line per entry and UTC timestamps, no custom formatter and no redaction layer (03 section 9.1). Stdout is what
    /// the Supervisor shows in the add-on's Log tab.
    /// </summary>
    public static ILoggingBuilder AddRealmConsole(this ILoggingBuilder logging) =>
        logging.AddSimpleConsole(options =>
        {
            options.SingleLine = true;
            options.UseUtcTimestamp = true;
            options.TimestampFormat = ConsoleTimestampFormat;
        });

    /// <summary>
    /// Where the key ring lives (03 section 5.7): beside the database file, which in Live mode in the add-on is <c>/data/dp-keys</c>. A Demo host, which has no
    /// database, uses that place only when <c>Realm:Db</c> (or <c>REALM_DB</c>) names one; otherwise null, so the framework's user-profile location applies and
    /// <c>/data</c> is never touched.
    /// </summary>
    public static string? DataProtectionDirectory(RuntimeOptions runtime, IConfiguration? configuration)
    {
        if (configuration is null)
        {
            return null;
        }

        var configured = !string.IsNullOrEmpty(configuration["REALM_DB"]) || !string.IsNullOrEmpty(configuration["Realm:Db"]);
        if (runtime.Mode != RealmMode.Live && !configured)
        {
            return null;
        }

        var database = RealmLiveServiceCollectionExtensions.ResolveDatabasePath(configuration);
        return Path.Combine(Path.GetDirectoryName(Path.GetFullPath(database)) ?? ".", DataProtectionDirectoryName);
    }

    // The directory is created and written once, now: the key ring would otherwise first fail at the first circuit that needs a key, with a 500.
    private static (string? Directory, string? Problem) PrepareDataProtectionDirectory(RuntimeOptions runtime, IConfiguration? configuration)
    {
        string? directory = null;
        try
        {
            directory = DataProtectionDirectory(runtime, configuration);
            if (directory is null)
            {
                return (null, null);
            }

            Directory.CreateDirectory(directory);
            var probe = Path.Combine(directory, ".write-test-" + Guid.NewGuid().ToString("N"));
            File.WriteAllBytes(probe, []);
            File.Delete(probe);
            return (directory, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return (directory, ex.GetType().Name);
        }
    }

    /// <summary>Logs the warning of 03 section 5.7 once, at start. It never throws and has nothing to stop.</summary>
    private sealed class DataProtectionReport(string directory, string problem, ILogger<DataProtectionReport> logger) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
            logger.LogWarning(
                "The Data Protection key directory {Directory} cannot be used ({ErrorType}); the keys stay in memory, so a restart or an update ends the open circuits",
                directory,
                problem);
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
