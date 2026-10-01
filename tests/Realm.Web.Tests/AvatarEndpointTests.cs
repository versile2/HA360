using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Realm.Domain;
using Realm.Infrastructure.Avatars;
using Realm.TestKit;
using Xunit;

namespace Realm.Web.Tests;

/// <summary>
/// <c>GET avatars/{memberId}</c> (03 section 10.4): the path carries a member id and nothing else, the answer is the picture the source returns or a bare 404,
/// and the browser may keep a picture for a day and revalidate it with the entity tag. What makes a picture acceptable (raster only, 2 MB, the member's
/// own upstream) is the source's job and is tested with <c>AvatarService</c> in <c>Realm.Ingestion.Tests</c>; these tests use a fake source and so pin the
/// endpoint's own behaviour.
/// </summary>
public sealed class AvatarEndpointTests
{
    private const string FictionalToken = "fictional-test-token-0001";
    private const string Tag = "\"0123456789abcdef0123456789abcdef\"";

    // The eight bytes of a PNG signature and four more; the endpoint never looks inside.
    private static readonly byte[] Picture = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4];

    [Fact]
    public async Task InDemoMode_NoSourceIsRegistered_AndTheAnswerIs404()
    {
        await using var host = await RealmTestHost.StartAsync();
        using var client = host.CreateClient();

        using var response = await client.GetAsync("avatars/king");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Null(host.Services.GetService<IAvatarSource>());
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task APictureThatExists_IsServedWithItsTypeItsTagAndADayOfPrivateCaching()
    {
        var source = new FakeAvatarSource().With("king", new AvatarImage(Picture, "image/png", Tag));
        await using var host = await StartWithAsync(source);
        using var client = host.CreateClient();

        using var response = await client.GetAsync("avatars/king");

        await RealmTestHost.EnsureSuccessAsync(host, response);
        Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(Tag, response.Headers.ETag?.Tag);
        var cache = response.Headers.CacheControl;
        Assert.NotNull(cache);
        Assert.True(cache.Private);
        Assert.Equal(TimeSpan.FromHours(24), cache.MaxAge);
        Assert.Equal(Picture, await response.Content.ReadAsByteArrayAsync());
        Assert.Equal(["king"], source.Requests);
    }

    [Fact]
    public async Task APictureTheBrowserAlreadyHas_IsAnswered304WithNoBody()
    {
        var source = new FakeAvatarSource().With("king", new AvatarImage(Picture, "image/png", Tag));
        await using var host = await StartWithAsync(source);
        using var client = host.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "avatars/king");
        request.Headers.IfNoneMatch.Add(new EntityTagHeaderValue(Tag));

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotModified, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task APictureWithNoTag_IsStillServed()
    {
        var source = new FakeAvatarSource().With("king", new AvatarImage(Picture, "image/webp"));
        await using var host = await StartWithAsync(source);
        using var client = host.CreateClient();

        using var response = await client.GetAsync("avatars/king");

        await RealmTestHost.EnsureSuccessAsync(host, response);
        Assert.Equal("image/webp", response.Content.Headers.ContentType?.MediaType);
        Assert.Null(response.Headers.ETag);
        Assert.Equal(Picture, await response.Content.ReadAsByteArrayAsync());
    }

    [Theory]
    [InlineData("nobody")]
    [InlineData("KING")]
    [InlineData("king.png")]
    public async Task AnIdTheSourceHasNoPictureFor_Is404WithNoBody_AndTheSourceSawTheIdAsSent(string id)
    {
        var source = new FakeAvatarSource().With("king", new AvatarImage(Picture, "image/png", Tag));
        await using var host = await StartWithAsync(source);
        using var client = host.CreateClient();

        using var response = await client.GetAsync("avatars/" + id);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
        Assert.Equal([id], source.Requests);
    }

    [Theory]
    [InlineData("avatars/king/extra")]
    [InlineData("avatars/")]
    [InlineData("avatars")]
    public async Task APathThatIsNotOneMemberId_NeverReachesTheSource(string path)
    {
        var source = new FakeAvatarSource().With("king", new AvatarImage(Picture, "image/png", Tag));
        await using var host = await StartWithAsync(source);
        using var client = host.CreateClient();

        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(source.Requests);
    }

    [Fact]
    public async Task InLiveMode_TheAvatarServiceIsRegistered_AndAMemberNobodyHasDiscoveredHasNoPicture()
    {
        var logs = new InMemoryLogSink();
        var settings = new Dictionary<string, string?> { ["SUPERVISOR_TOKEN"] = FictionalToken };
        await using var host = await RealmTestHost.StartAsync(logs, settings);
        using var client = host.CreateClient();

        using var response = await client.GetAsync("avatars/king");

        Assert.IsType<AvatarService>(host.Services.GetRequiredService<IAvatarSource>());
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains(logs.Entries, entry => entry.Level == LogLevel.Warning && entry.Category == typeof(AvatarService).FullName);
    }

    [Fact]
    public async Task InLiveMode_NothingThatIsLogged_CarriesTheToken()
    {
        var logs = new InMemoryLogSink();
        var settings = new Dictionary<string, string?> { ["SUPERVISOR_TOKEN"] = FictionalToken };
        await using var host = await RealmTestHost.StartAsync(logs, settings);
        using var client = host.CreateClient();

        using var response = await client.GetAsync("avatars/king");

        // The connection attempt to the refusing address is logged at once; without it this test would say nothing, so it waits for it.
        Assert.True(
            SpinWait.SpinUntil(() => logs.Entries.Any(entry => entry.Category.EndsWith("HaWebSocketConnection", StringComparison.Ordinal)), TimeSpan.FromSeconds(10)),
            "The websocket connection logged nothing");
        Assert.NotEmpty(logs.Entries);
        Assert.DoesNotContain(logs.Entries, entry => entry.Message.Contains(FictionalToken, StringComparison.Ordinal));
    }

    private static Task<KestrelHost> StartWithAsync(FakeAvatarSource source) =>
        RealmTestHost.StartAsync(configureServices: services => services.AddSingleton<IAvatarSource>(source));

    private sealed class FakeAvatarSource : IAvatarSource
    {
        private readonly Dictionary<string, AvatarImage> _images = new(StringComparer.Ordinal);
        private readonly ConcurrentQueue<string> _requests = new();

        /// <summary>The ids the endpoint asked for, in order.</summary>
        public IReadOnlyList<string> Requests => _requests.ToArray();

        public FakeAvatarSource With(string memberId, AvatarImage image)
        {
            _images[memberId] = image;
            return this;
        }

        public Task<AvatarImage?> GetAsync(string memberId, CancellationToken cancellationToken)
        {
            _requests.Enqueue(memberId);
            return Task.FromResult(_images.TryGetValue(memberId, out var image) ? image : null);
        }
    }
}
