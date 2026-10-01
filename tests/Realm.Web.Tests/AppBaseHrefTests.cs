using System.Net;
using Microsoft.AspNetCore.Builder;
using Realm.Web.Components;
using Xunit;

namespace Realm.Web.Tests;

/// <summary>
/// The rendered <c>&lt;base href&gt;</c> of <c>App.razor</c> for an absent, a valid and an invalid <c>X-Ingress-Path</c> (03 section 5.4),
/// read from the real HTML a Kestrel host returns.
/// </summary>
public sealed class AppBaseHrefTests
{
    [Fact]
    public async Task AbsentHeader_RendersTheRootAsBase()
    {
        var html = await GetPageAsync(ingressPath: null);

        Assert.Equal("/", BaseHrefOf(html));
    }

    [Theory]
    [InlineData("/api/hassio_ingress/AbC-_123", "/api/hassio_ingress/AbC-_123/")]
    [InlineData("/api/hassio_ingress/AbC-_123/", "/api/hassio_ingress/AbC-_123/")]
    [InlineData("/x", "/x/")]
    public async Task ValidHeader_RendersThePrefixWithOneTrailingSlashAsBase(string header, string expectedBase)
    {
        var html = await GetPageAsync(header);

        Assert.Equal(expectedBase, BaseHrefOf(html));
    }

    [Theory]
    [InlineData("api/hassio_ingress/x")]
    [InlineData("/api/hassio ingress")]
    [InlineData("/api/\"x")]
    [InlineData("/api/<x")]
    public async Task InvalidHeader_IsIgnoredAndRendersTheRootAsBase(string header)
    {
        var html = await GetPageAsync(header);

        Assert.Equal("/", BaseHrefOf(html));
    }

    [Fact]
    public async Task BaseTag_IsTheOnlyOneAndComesBeforeEveryElementThatLoadsAUrl()
    {
        var html = await GetPageAsync("/api/hassio_ingress/AbC-_123");

        var baseTag = html.IndexOf("<base ", StringComparison.Ordinal);
        Assert.True(baseTag >= 0, "The page has no <base> tag.");
        Assert.Equal(baseTag, html.LastIndexOf("<base ", StringComparison.Ordinal));
        Assert.True(baseTag < html.IndexOf("<link ", StringComparison.Ordinal), "A <link> comes before <base>.");
        Assert.True(baseTag < html.IndexOf("<script ", StringComparison.Ordinal), "A <script> comes before <base>.");
    }

    private static async Task<string> GetPageAsync(string? ingressPath)
    {
        await using var host = await RealmTestHost.StartAsync(
            mapEndpoints: app => app.MapRazorComponents<App>().AddInteractiveServerRenderMode());
        using var client = host.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, string.Empty);
        if (ingressPath is not null)
        {
            request.Headers.TryAddWithoutValidation("X-Ingress-Path", ingressPath);
        }

        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    private static string BaseHrefOf(string html)
    {
        const string marker = "<base href=\"";
        var start = html.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, "The page has no <base href=\"...\"> tag.");
        start += marker.Length;
        var end = html.IndexOf('"', start);
        Assert.True(end > start, "The href of <base> is not closed.");
        return WebUtility.HtmlDecode(html[start..end]);
    }
}
