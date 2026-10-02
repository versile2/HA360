using Microsoft.AspNetCore.Builder;
using Realm.Demo;
using Realm.TestKit;
using Realm.Web.Components;
using Realm.Web.Hosting;
using Xunit;

namespace Realm.Web.Tests;

/// <summary>
/// Who "me" is, resolved in static SSR by <c>App.razor</c> from <c>X-Remote-User-Id</c> (03 section 5.5, R-082), and the rule that goes with it (D38): the Home Assistant user id is read
/// into a local, goes into <see cref="ViewerResolver"/> and nowhere else, so it is in no part of the HTML (the component marker, the parameters of <c>Routes</c>, the markup), in no response
/// header and in no log line; only the member's slug is handed on. Each row asks the real pipeline for the root page with a header, as Home Assistant's Ingress does. The slug
/// itself cannot be read from the HTML (the marker's descriptor is data-protected), so the resolution is read from the one Debug line the resolver writes, <c>viewer=king via=Person</c>, which
/// names the member and the source and never the id.
/// </summary>
public sealed class ViewerIdentityPrivacyTests
{
    private const string UserIdHeader = "X-Remote-User-Id";

    // An id no person of the demo cast has.
    private const string UnlinkedUserId = "unlinked-user-0001";

    [Fact(DisplayName = "[D38] The user id of a linked viewer is in no part of the page, the headers or the logs, and the page resolves its member")]
    public async Task ALinkedViewer_IsResolvedToTheirMember_AndTheIdLeaksNowhere()
    {
        var userId = PersonUserIdOf(DemoCast.Queen);

        var seen = await GetPageAsync(userId);

        AssertNothingLeaks(seen, userId);
        Assert.Equal([$"viewer={DemoCast.Queen.Id} via=Person"], seen.ResolverLines);
    }

    [Fact]
    public async Task AnotherLinkedViewer_IsAnotherMember()
    {
        var userId = PersonUserIdOf(DemoCast.King);

        var seen = await GetPageAsync(userId);

        AssertNothingLeaks(seen, userId);
        Assert.Equal([$"viewer={DemoCast.King.Id} via=Person"], seen.ResolverLines);
    }

    [Fact]
    public async Task AUserWhoIsLinkedToNoPerson_FallsToTheFirstLiveMember_AndTheirIdLeaksNowhere()
    {
        var seen = await GetPageAsync(UnlinkedUserId);

        AssertNothingLeaks(seen, UnlinkedUserId);
        Assert.Equal([$"viewer={DemoCast.King.Id} via=FirstLive"], seen.ResolverLines);
    }

    [Fact]
    public async Task WithoutTheHeader_TheFirstLiveMemberIsMe()
    {
        var seen = await GetPageAsync(userId: null);

        Assert.Equal([$"viewer={DemoCast.King.Id} via=FirstLive"], seen.ResolverLines);
        Assert.DoesNotContain(UserIdHeader, seen.Html, StringComparison.OrdinalIgnoreCase);
    }

    private static string PersonUserIdOf(DemoMember member) =>
        member.PersonUserId ?? throw new InvalidOperationException($"The demo member {member.Id} has no person link.");

    private static void AssertNothingLeaks(Seen seen, string userId)
    {
        Assert.DoesNotContain(userId, seen.Html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(userId, seen.Headers, StringComparison.OrdinalIgnoreCase);
        Assert.All(seen.Entries, entry => Assert.DoesNotContain(userId, entry.Message, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(UserIdHeader, seen.Html, StringComparison.OrdinalIgnoreCase);
    }

    // Asks for the root page the way Ingress does, with the Debug level switched on for the resolver's category only, and keeps what came back and what the host logged.
    private static async Task<Seen> GetPageAsync(string? userId)
    {
        var sink = new InMemoryLogSink();
        await using var host = await RealmTestHost.StartAsync(
            logs: sink,
            settings: new Dictionary<string, string?> { [$"Logging:LogLevel:{typeof(ViewerResolver).FullName}"] = "Debug" },
            mapEndpoints: app => app.MapRazorComponents<App>().AddInteractiveServerRenderMode());
        using var client = host.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, string.Empty);
        if (userId is not null)
        {
            request.Headers.TryAddWithoutValidation(UserIdHeader, userId);
        }

        using var response = await client.SendAsync(request);
        await RealmTestHost.EnsureSuccessAsync(host, response);   // a 500 reports the server-side exception, not just the status code
        var html = await response.Content.ReadAsStringAsync();
        var headers = string.Join('\n', response.Headers.Concat(response.Content.Headers).Select(header => $"{header.Key}: {string.Join(',', header.Value)}"));
        return new Seen(html, headers, sink.Entries);
    }

    private sealed record Seen(string Html, string Headers, IReadOnlyList<LogEntry> Entries)
    {
        public IReadOnlyList<string> ResolverLines { get; } =
        [
            .. Entries.Where(entry => entry.Category == typeof(ViewerResolver).FullName).Select(entry => entry.Message),
        ];
    }
}
