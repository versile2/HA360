using System.Net;
using Microsoft.AspNetCore.Builder;
using Realm.Web.Components;
using Xunit;

namespace Realm.Web.Tests;

/// <summary>
/// The static <c>#blazor-error-ui</c> of <c>App.razor</c> (D67, CR2-002), read from the real HTML a Kestrel host returns. Blazor looks the element up
/// by id when a circuit ends on an unhandled exception, shows it by setting <c>display: block</c>, and makes every <c>.reload</c> inside it call
/// <c>location.reload()</c>. Without the element the page freezes in silence, because the reconnect banner never appears for such a failure.
/// </summary>
public sealed class AppErrorUiTests
{
    private const string Marker = "id=\"blazor-error-ui\"";
    private const string Copy = "Something went sideways in the castle. Try again.";

    [Fact]
    public async Task Page_HasOneErrorUiThatIsHiddenAndAnAlert()
    {
        var html = await GetPageAsync();

        Assert.Contains(Marker, html);
        Assert.Equal(html.IndexOf(Marker, StringComparison.Ordinal), html.LastIndexOf(Marker, StringComparison.Ordinal));

        var tag = OpeningTagAt(html, html.IndexOf(Marker, StringComparison.Ordinal));
        Assert.StartsWith("<div ", tag, StringComparison.Ordinal);
        Assert.Contains("role=\"alert\"", Attributes(tag));
        Assert.True(HasBooleanAttribute(tag, "hidden"), "The error UI must start hidden: Blazor shows it, the page never does. Tag: " + tag);
    }

    [Fact]
    public async Task ErrorUi_SaysTheGenericCopy_AndOffersAReloadThatBlazorWiresUp()
    {
        var html = await GetPageAsync();
        var marker = html.IndexOf(Marker, StringComparison.Ordinal);
        Assert.True(marker >= 0, "The page has no element with id=\"blazor-error-ui\".");

        var contentStart = html.IndexOf('>', marker) + 1;
        var contentEnd = html.IndexOf("</div>", contentStart, StringComparison.Ordinal);
        Assert.True(contentEnd > contentStart, "The error UI is not closed by a </div>.");
        var content = html[contentStart..contentEnd];

        Assert.Contains(Copy, WebUtility.HtmlDecode(content));

        var buttonStart = content.IndexOf("<button", StringComparison.Ordinal);
        Assert.True(buttonStart >= 0, "The error UI has no <button>.");
        var button = OpeningTagAt(content, buttonStart);
        Assert.Contains("type=\"button\"", Attributes(button));
        Assert.Contains("reload", ClassesOf(button));   // BootErrors.ts binds '#blazor-error-ui .reload' to location.reload()
    }

    [Fact]
    public async Task ErrorUi_IsStaticMarkupThatComesBeforeThePage()
    {
        var html = await GetPageAsync();

        var errorUi = html.IndexOf(Marker, StringComparison.Ordinal);
        var page = html.IndexOf("id=\"realm-main\"", StringComparison.Ordinal);
        Assert.True(errorUi >= 0 && page >= 0, "The page lacks the error UI or the shell's main element.");
        Assert.True(errorUi < page, "The error UI must be outside Routes, so it exists even when the circuit never starts.");
    }

    private static async Task<string> GetPageAsync()
    {
        await using var host = await RealmTestHost.StartAsync(
            mapEndpoints: app => app.MapRazorComponents<App>().AddInteractiveServerRenderMode());
        using var client = host.CreateClient();

        using var response = await client.GetAsync(string.Empty);

        await RealmTestHost.EnsureSuccessAsync(host, response);   // a 500 reports the server-side exception, not just the status code
        return await response.Content.ReadAsStringAsync();
    }

    // The start tag that contains the position: from the last '<' before it to the next '>'.
    private static string OpeningTagAt(string html, int position)
    {
        var start = html.LastIndexOf('<', position);
        var end = html.IndexOf('>', position);
        Assert.True(start >= 0 && end > start, "Could not find the start tag.");
        return html[start..(end + 1)];
    }

    // The attributes of a start tag, as they were written ("name" or "name=\"value\""), without the tag name and the angle brackets.
    private static string[] Attributes(string tag) =>
        tag.TrimStart('<').TrimEnd('>').TrimEnd('/').Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[1..];

    // A boolean attribute is written as "hidden" or as "hidden=\"\"".
    private static bool HasBooleanAttribute(string tag, string name) =>
        Attributes(tag).Any(attribute => attribute == name || attribute == name + "=\"\"");

    private static string[] ClassesOf(string tag)
    {
        const string marker = "class=\"";
        var start = tag.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, "The tag has no class attribute: " + tag);
        start += marker.Length;
        var end = tag.IndexOf('"', start);
        Assert.True(end > start, "The class attribute is not closed: " + tag);
        return tag[start..end].Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }
}
