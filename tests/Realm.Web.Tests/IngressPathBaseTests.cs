using Microsoft.Extensions.Logging;
using Realm.TestKit;
using Realm.Web.Hosting;
using Xunit;

namespace Realm.Web.Tests;

public class IngressPathBaseTests
{
    [Theory]
    [InlineData("/api/hassio_ingress/AbC-_123", "/api/hassio_ingress/AbC-_123")]
    [InlineData("/api/hassio_ingress/AbC-_123/", "/api/hassio_ingress/AbC-_123")]
    [InlineData("/x", "/x")]
    public async Task ValidHeader_SetsPathBaseWithoutTrailingSlashAndLeavesPathAlone(string header, string expectedPathBase)
    {
        var logs = new InMemoryLogSink();
        await using var host = await RealmTestHost.StartAsync(logs);

        var echo = await RealmTestHost.GetEchoAsync(host, ("X-Ingress-Path", header));

        Assert.Equal(expectedPathBase, echo.PathBase);
        Assert.Equal("/echo", echo.Path);
        Assert.Empty(Warnings(logs));
    }

    [Theory]
    [InlineData("api/hassio_ingress/x")]
    [InlineData("/api/hassio ingress")]
    [InlineData("/api/\"x")]
    [InlineData("/api/'x")]
    [InlineData("/api/<x")]
    [InlineData("/api/x>")]
    public async Task InvalidHeader_IsIgnoredWithOneWarning(string header)
    {
        var logs = new InMemoryLogSink();
        await using var host = await RealmTestHost.StartAsync(logs);

        var echo = await RealmTestHost.GetEchoAsync(host, ("X-Ingress-Path", header));

        Assert.Empty(echo.PathBase);
        Assert.Equal("/echo", echo.Path);
        var warning = Assert.Single(Warnings(logs));
        Assert.Contains(header, warning.Message);
    }

    [Fact]
    public async Task InvalidHeader_SameValueTwice_WarnsOnce()
    {
        var logs = new InMemoryLogSink();
        await using var host = await RealmTestHost.StartAsync(logs);

        await RealmTestHost.GetEchoAsync(host, ("X-Ingress-Path", "no-leading-slash"));
        await RealmTestHost.GetEchoAsync(host, ("X-Ingress-Path", "no-leading-slash"));

        Assert.Single(Warnings(logs));
    }

    [Fact]
    public async Task AbsentHeader_LeavesPathBaseEmptyWithoutWarning()
    {
        var logs = new InMemoryLogSink();
        await using var host = await RealmTestHost.StartAsync(logs);

        var echo = await RealmTestHost.GetEchoAsync(host);

        Assert.Empty(echo.PathBase);
        Assert.Equal("/echo", echo.Path);
        Assert.Empty(Warnings(logs));
    }

    [Fact]
    public async Task EmptyHeader_IsTreatedAsAbsent()
    {
        var logs = new InMemoryLogSink();
        await using var host = await RealmTestHost.StartAsync(logs);

        var echo = await RealmTestHost.GetEchoAsync(host, ("X-Ingress-Path", string.Empty));

        Assert.Empty(echo.PathBase);
        Assert.Empty(Warnings(logs));
    }

    private static IReadOnlyList<LogEntry> Warnings(InMemoryLogSink logs) =>
        logs.Entries.Where(entry => entry.Level == LogLevel.Warning && entry.Category == typeof(IngressPathBase).FullName).ToList();
}
