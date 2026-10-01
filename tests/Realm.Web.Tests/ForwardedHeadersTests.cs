using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Realm.Web.Tests;

public class ForwardedHeadersTests
{
    [Fact]
    public async Task ProtoAndHostHeaders_AreApplied()
    {
        await using var host = await RealmTestHost.StartAsync();

        var echo = await RealmTestHost.GetEchoAsync(host, ("X-Forwarded-Proto", "https"), ("X-Forwarded-Host", "ha.example.test"));

        Assert.Equal("https", echo.Scheme);
        Assert.Equal("ha.example.test", echo.Host);
    }

    [Fact]
    public async Task WithoutForwardedHeaders_KeepsTheLoopbackOrigin()
    {
        await using var host = await RealmTestHost.StartAsync();

        var echo = await RealmTestHost.GetEchoAsync(host);

        Assert.Equal("http", echo.Scheme);
        Assert.Equal(host.BaseAddress.Authority, echo.Host);
    }

    [Fact]
    public async Task Registration_ClearsKnownProxiesAndNetworks()
    {
        await using var host = await RealmTestHost.StartAsync();

        var options = host.Services.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;

        Assert.Equal(ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost, options.ForwardedHeaders);
        Assert.Empty(options.KnownProxies);
        Assert.Empty(options.KnownIPNetworks);
    }
}
