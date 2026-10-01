using System.Net;
using Realm.Web.Hosting;
using Xunit;

namespace Realm.Web.Tests;

public class HealthzTests
{
    [Fact]
    public async Task Healthz_Returns200()
    {
        await using var host = await RealmTestHost.StartAsync();
        using var client = host.CreateClient();

        using var response = await client.GetAsync("healthz");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("ok", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Healthz_Returns200InLiveModeWithNoHaReachable()
    {
        // A token makes this Live mode, and nothing answers as Home Assistant: liveness must not depend on HA (03 section 2.14).
        var settings = new Dictionary<string, string?> { ["SUPERVISOR_TOKEN"] = "test-token" };
        await using var host = await RealmTestHost.StartAsync(settings: settings);
        using var client = host.CreateClient();

        using var response = await client.GetAsync("healthz");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task HealthProbe_ReturnsZeroWhenHealthzAnswers()
    {
        await using var host = await RealmTestHost.StartAsync();

        Assert.Equal(0, await HealthProbe.RunAsync(host.BaseAddress));
    }

    [Fact]
    public async Task HealthProbe_ReturnsOneWhenNothingListens()
    {
        // Port 1 is privileged and unused, so the connection is refused at once.
        Assert.Equal(1, await HealthProbe.RunAsync(new Uri("http://127.0.0.1:1/")));
    }
}
