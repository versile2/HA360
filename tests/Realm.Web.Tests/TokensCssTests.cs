using System.Net;
using Realm.Web.Theme;
using Xunit;

namespace Realm.Web.Tests;

public sealed class TokensCssTests
{
    [Fact]
    public void Css_DeclaresThePaletteAsRealmTokensOnRoot()
    {
        var css = RealmTokens.Css();

        Assert.StartsWith(":root {", css, StringComparison.Ordinal);
        Assert.Contains("color-scheme: dark;", css, StringComparison.Ordinal);
        Assert.Contains($"--realm-bg: {RealmPalette.Bg};", css, StringComparison.Ordinal);
        Assert.Contains($"--realm-primary: {RealmPalette.Primary};", css, StringComparison.Ordinal);
        Assert.Contains($"--realm-sheet-bg-peek: {RealmPalette.SheetBgPeek};", css, StringComparison.Ordinal);
        Assert.Contains($"--realm-pin-outline: {RealmPalette.PinOutline};", css, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Get_ReturnsTheStylesheetWithNoCacheAndAStrongETag()
    {
        await using var host = await RealmTestHost.StartAsync();
        using var client = host.CreateClient();

        using var response = await client.GetAsync("css/tokens.css");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/css", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.NoCache);
        var etag = response.Headers.ETag;
        Assert.NotNull(etag);
        Assert.False(etag.IsWeak);
        Assert.Equal(RealmTokens.Css(), await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Get_WithTheCurrentETag_Returns304WithoutABody()
    {
        await using var host = await RealmTestHost.StartAsync();
        using var client = host.CreateClient();
        using var first = await client.GetAsync("css/tokens.css");
        var etag = first.Headers.ETag ?? throw new InvalidOperationException("The first response carried no ETag.");
        using var request = new HttpRequestMessage(HttpMethod.Get, "css/tokens.css");
        request.Headers.IfNoneMatch.Add(etag);

        using var second = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotModified, second.StatusCode);
        Assert.Empty(await second.Content.ReadAsStringAsync());
    }
}
