using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Realm.Demo;
using Realm.Domain;
using Realm.Web.Components;
using Xunit;

namespace Realm.Web.Tests;

/// <summary>
/// The Kestrel test host is composed by <c>AddRealmApp</c>, the call <c>Program.cs</c> makes, so it holds every service the real pages inject.
/// S4c put <c>RealmShell</c> into <c>MainLayout</c> and the test host, which then lacked its services, answered every page with a 500.
/// </summary>
public sealed class RealmAppCompositionTests
{
    [Fact]
    public async Task TestHost_HasTheServicesRealmShellInjects()
    {
        await using var host = await RealmTestHost.StartAsync();

        Assert.IsType<DemoRealmSessionFactory>(host.Services.GetRequiredService<IRealmSessionFactory>());
        Assert.NotNull(host.Services.GetRequiredService<RuntimeOptions>());
    }

    [Fact]
    public async Task RootPage_RendersInsideTheShell()
    {
        await using var host = await RealmTestHost.StartAsync(
            mapEndpoints: app => app.MapRazorComponents<App>().AddInteractiveServerRenderMode());
        using var client = host.CreateClient();

        using var response = await client.GetAsync(string.Empty);

        await RealmTestHost.EnsureSuccessAsync(host, response);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("id=\"realm-main\"", html);
    }
}
