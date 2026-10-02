using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using Realm.Domain;
using Realm.Infrastructure.Avatars;
using Realm.Infrastructure.Ha;
using Realm.TestKit;
using Xunit;

namespace Realm.Ingestion.Tests;

/// <summary>
/// 03 section 10.4: the proxy fetches only the member's own configured picture (an HA <c>image/serve</c> path or a Life360 HTTPS address), serves raster
/// images only, refuses more than 2 MB and any redirect, answers "none" with one Warning for every refusal, and keeps one cache file per member that is
/// used for 24 hours. Nothing touches the network: a fake gateway and a scripted handler answer, and a manual clock keeps the 24 hours.
/// </summary>
public sealed class AvatarServiceTests : IDisposable
{
    private const string HaPicture = "/api/image/serve/0a1b2c3d4e5f/512x512";
    private const string Life360Picture = "https://cdn.life360.com/user_images/00000000-0000-4000-8000-000000000001/king.png";
    private const int ThreeMegabytes = 3 * 1024 * 1024;

    private static readonly DateTimeOffset Start = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13];

    private readonly string _cache = Path.Combine(Path.GetTempPath(), "realm-avatars-" + Guid.NewGuid().ToString("N"));
    private readonly List<HttpClient> _clients = [];

    private sealed record Rig(
        AvatarService Service,
        FakeHaGateway Gateway,
        ScriptedHttpHandler Life360,
        ManualTimeProvider Time,
        RecordingLogger<AvatarService> Log,
        DiscoveryState Discovery);

    public void Dispose()
    {
        foreach (var client in _clients)
        {
            client.Dispose();
        }

        if (Directory.Exists(_cache))
        {
            Directory.Delete(_cache, recursive: true);
        }
    }

    // ---- served ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task AConfiguredHaPicture_IsFetchedOnce_ThenServedFromTheCache()
    {
        var rig = NewRig(Plans.Member("king", avatar: HaPicture));
        rig.Gateway.Image = (_, _, _) => Task.FromResult<AvatarImage?>(new AvatarImage(Png, "image/png"));

        var first = await rig.Service.GetAsync("king", CancellationToken.None);
        var second = await rig.Service.GetAsync("king", CancellationToken.None);

        Assert.NotNull(first);
        Assert.Equal("image/png", first.ContentType);
        Assert.Equal(Png, first.Bytes);
        Assert.Equal(first.ETag, second?.ETag);
        Assert.Equal(Png, second?.Bytes);
        Assert.Equal(1, rig.Gateway.ImageCalls);
        Assert.Matches("^\"[0-9a-f]{32}\"$", first.ETag);
        Assert.Empty(rig.Log.Entries);
    }

    [Fact]
    public async Task TheHaPicture_IsAskedForByThePathTheMemberConfigured_AndWithinTheLimit()
    {
        var rig = NewRig(Plans.Member("king", avatar: HaPicture));
        string? asked = null;
        int? limit = null;
        rig.Gateway.Image = (path, max, _) =>
        {
            asked = path;
            limit = max;
            return Task.FromResult<AvatarImage?>(new AvatarImage(Png, "image/png"));
        };

        await rig.Service.GetAsync("king", CancellationToken.None);

        Assert.Equal(HaPicture, asked);
        Assert.Equal(2 * 1024 * 1024, limit);
    }

    [Fact]
    public async Task TheCache_IsOneFilePerMember_NamedByTheMemberId()
    {
        var rig = NewRig(Plans.Member("king", avatar: HaPicture), Plans.Member("queen", avatar: HaPicture));
        rig.Gateway.Image = (_, _, _) => Task.FromResult<AvatarImage?>(new AvatarImage(Png, "image/png"));

        await rig.Service.GetAsync("king", CancellationToken.None);
        await rig.Service.GetAsync("queen", CancellationToken.None);
        rig.Time.Advance(AvatarService.RevalidateAfter);
        await rig.Service.GetAsync("king", CancellationToken.None);   // overwritten, not added to

        Assert.Equal(new[] { "king", "queen" }, Directory.GetFiles(_cache).Select(path => Path.GetFileName(path)!).Order().ToArray());
    }

    [Fact]
    public async Task ACachedPicture_IsFetchedAgainWhen24HoursOld()
    {
        var rig = NewRig(Plans.Member("king", avatar: HaPicture));
        rig.Gateway.Image = (_, _, _) => Task.FromResult<AvatarImage?>(new AvatarImage(Png, "image/png"));
        await rig.Service.GetAsync("king", CancellationToken.None);

        rig.Time.Advance(AvatarService.RevalidateAfter - TimeSpan.FromSeconds(1));
        await rig.Service.GetAsync("king", CancellationToken.None);
        Assert.Equal(1, rig.Gateway.ImageCalls);

        rig.Time.Advance(TimeSpan.FromSeconds(1));
        await rig.Service.GetAsync("king", CancellationToken.None);
        Assert.Equal(2, rig.Gateway.ImageCalls);
    }

    [Fact]
    public async Task ARefreshThatFails_AnswersNone_AndLogsOneWarning()
    {
        var rig = NewRig(Plans.Member("king", avatar: HaPicture));
        rig.Gateway.Image = (_, _, _) => Task.FromResult<AvatarImage?>(new AvatarImage(Png, "image/png"));
        await rig.Service.GetAsync("king", CancellationToken.None);
        rig.Time.Advance(AvatarService.RevalidateAfter);
        rig.Gateway.Image = (_, _, _) => Task.FromResult<AvatarImage?>(null);

        Assert.Null(await rig.Service.GetAsync("king", CancellationToken.None));

        Assert.Single(rig.Log.Messages(LogLevel.Warning));
    }

    [Theory]
    [InlineData("image/jpeg", new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 16, 0x4A, 0x46 })]
    [InlineData("image/png", new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })]
    [InlineData("image/gif", new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61, 1, 0 })]
    [InlineData("image/webp", new byte[] { 0x52, 0x49, 0x46, 0x46, 4, 0, 0, 0, 0x57, 0x45, 0x42, 0x50 })]
    [InlineData("image/avif", new byte[] { 0, 0, 0, 0x1C, 0x66, 0x74, 0x79, 0x70, 0x61, 0x76, 0x69, 0x66 })]
    public async Task EachOfTheFiveRasterTypes_IsServed_WithTheTypeItsBytesShow(string contentType, byte[] bytes)
    {
        var rig = NewRig(Plans.Member("king", avatar: HaPicture));
        rig.Gateway.Image = (_, _, _) => Task.FromResult<AvatarImage?>(new AvatarImage(bytes, contentType + "; charset=binary"));

        var image = await rig.Service.GetAsync("king", CancellationToken.None);

        Assert.Equal(contentType, image?.ContentType);
        Assert.Equal(bytes, image?.Bytes);
    }

    [Fact]
    public async Task ALife360Picture_IsFetchedFromItsOwnAddress_WithNoCredential()
    {
        var rig = NewRig(Plans.Member("king", avatar: Life360Picture));
        rig.Life360.RespondWith(_ => PngResponse());

        var image = await rig.Service.GetAsync("king", CancellationToken.None);

        Assert.Equal("image/png", image?.ContentType);
        var request = Assert.Single(rig.Life360.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(Life360Picture, request.Uri?.AbsoluteUri);
        Assert.False(request.Headers.ContainsKey("Authorization"));
        Assert.Equal(0, rig.Gateway.ImageCalls);   // the Supervisor is not asked for a Life360 picture
    }

    [Fact]
    public async Task ALife360Picture_IsFetchedOnce_ThenServedFromTheCache()
    {
        var rig = NewRig(Plans.Member("king", avatar: Life360Picture));
        rig.Life360.RespondWith(_ => PngResponse());

        await rig.Service.GetAsync("king", CancellationToken.None);
        var second = await rig.Service.GetAsync("king", CancellationToken.None);

        Assert.NotNull(second);
        Assert.Single(rig.Life360.Requests);
    }

    // ---- refused: each answers none and logs one Warning ----------------------------------------------------------

    [Theory]
    [InlineData("image/svg+xml", "<svg xmlns=\"http://www.w3.org/2000/svg\"/>")]
    [InlineData("text/html", "<html><script>alert(1)</script></html>")]
    [InlineData("application/octet-stream", "PNG")]
    [InlineData("", "PNG")]
    public async Task ANonRasterContentType_IsRefused(string contentType, string body)
    {
        var rig = NewRig(Plans.Member("king", avatar: HaPicture));
        rig.Gateway.Image = (_, _, _) => Task.FromResult<AvatarImage?>(new AvatarImage(System.Text.Encoding.UTF8.GetBytes(body), contentType));

        Assert.Null(await rig.Service.GetAsync("king", CancellationToken.None));

        AssertRefusedOnce(rig);
    }

    [Fact]
    public async Task ARasterContentType_OverBytesThatAreNotAnImage_IsRefused()
    {
        var rig = NewRig(Plans.Member("king", avatar: HaPicture));
        rig.Gateway.Image = (_, _, _) => Task.FromResult<AvatarImage?>(new AvatarImage("<html>not a picture</html>"u8.ToArray(), "image/png"));

        Assert.Null(await rig.Service.GetAsync("king", CancellationToken.None));

        AssertRefusedOnce(rig);
    }

    [Fact]
    public async Task ABodyOfThreeMegabytes_IsRefused_WhetherTheGatewayRefusesItOrNot()
    {
        var rig = NewRig(Plans.Member("king", avatar: HaPicture));
        rig.Gateway.Image = (_, _, _) => throw new InvalidDataException("The image is larger than the limit");
        Assert.Null(await rig.Service.GetAsync("king", CancellationToken.None));
        AssertRefusedOnce(rig);

        var oversized = new byte[ThreeMegabytes];
        Png.CopyTo(oversized, 0);
        var second = NewRig(Plans.Member("king", avatar: HaPicture));
        second.Gateway.Image = (_, _, _) => Task.FromResult<AvatarImage?>(new AvatarImage(oversized, "image/png"));
        Assert.Null(await second.Service.GetAsync("king", CancellationToken.None));
        AssertRefusedOnce(second);
    }

    [Fact]
    public async Task ALife360BodyOfThreeMegabytes_IsRefused_WhetherItIsAnnouncedOrNot()
    {
        var oversized = new byte[ThreeMegabytes];
        Png.CopyTo(oversized, 0);

        var announced = NewRig(Plans.Member("king", avatar: Life360Picture));
        announced.Life360.RespondWith(_ => ImageResponse(oversized, "image/png"));
        Assert.Null(await announced.Service.GetAsync("king", CancellationToken.None));
        AssertRefusedOnce(announced);

        var streamed = NewRig(Plans.Member("king", avatar: Life360Picture));
        streamed.Life360.RespondWith(_ =>
        {
            var content = new ChunkedContent(oversized);
            content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        });
        Assert.Null(await streamed.Service.GetAsync("king", CancellationToken.None));
        AssertRefusedOnce(streamed);
    }

    [Theory]
    [InlineData(HttpStatusCode.MovedPermanently)]
    [InlineData(HttpStatusCode.Found)]
    [InlineData(HttpStatusCode.TemporaryRedirect)]
    public async Task ARedirect_IsRefused_NotFollowed(HttpStatusCode redirect)
    {
        var rig = NewRig(Plans.Member("king", avatar: Life360Picture));
        rig.Life360.RespondWith(_ =>
        {
            var response = new HttpResponseMessage(redirect);
            response.Headers.Location = new Uri("https://example.test/elsewhere.png");
            return response;
        });

        Assert.Null(await rig.Service.GetAsync("king", CancellationToken.None));

        AssertRefusedOnce(rig);
        Assert.Single(rig.Life360.Requests);   // the Location was not requested
    }

    [Fact]
    public async Task AnUpstreamThatFailsOrAnswers404_IsRefused()
    {
        var failing = NewRig(Plans.Member("king", avatar: Life360Picture));
        failing.Life360.Fail(new HttpRequestException("connection refused: https://secret.invalid/path"));
        Assert.Null(await failing.Service.GetAsync("king", CancellationToken.None));
        AssertRefusedOnce(failing);
        Assert.DoesNotContain("secret.invalid", failing.Log.Text, StringComparison.Ordinal);   // the type of the failure is logged, never its message

        var notFound = NewRig(Plans.Member("king", avatar: Life360Picture));
        notFound.Life360.Respond(HttpStatusCode.NotFound);
        Assert.Null(await notFound.Service.GetAsync("king", CancellationToken.None));
        AssertRefusedOnce(notFound);
    }

    [Theory]
    [InlineData("http://cdn.life360.com/user_images/a/king.png")]
    [InlineData("https://evil.example/king.png")]
    [InlineData("https://life360.com.evil.example/king.png")]
    [InlineData("https://user:pass@cdn.life360.com/king.png")]
    [InlineData("/api/states")]
    [InlineData("/api/image/serve/../states")]
    [InlineData("file:///etc/passwd")]
    public async Task AnUpstreamThatIsNeitherAnHaImageNorALife360Address_IsNeverFetched(string upstream)
    {
        var rig = NewRig(Plans.Member("king", avatar: upstream));

        Assert.Null(await rig.Service.GetAsync("king", CancellationToken.None));

        AssertRefusedOnce(rig);
        Assert.Empty(rig.Life360.Requests);
        Assert.Equal(0, rig.Gateway.ImageCalls);
    }

    [Fact]
    public async Task AMemberWithNoPicture_AnswersNone()
    {
        var rig = NewRig(Plans.Member("king", avatar: null));

        Assert.Null(await rig.Service.GetAsync("king", CancellationToken.None));

        AssertRefusedOnce(rig);
        Assert.Equal(0, rig.Gateway.ImageCalls);
        Assert.Empty(rig.Life360.Requests);
    }

    [Fact]
    public async Task AnUnknownId_AnswersNone_AndNothingIsFetched()
    {
        var rig = NewRig(Plans.Member("king", avatar: HaPicture));

        Assert.Null(await rig.Service.GetAsync("queen", CancellationToken.None));

        AssertRefusedOnce(rig);
        Assert.Equal(0, rig.Gateway.ImageCalls);
        Assert.False(Directory.Exists(_cache));
    }

    [Theory]
    [InlineData("../king")]
    [InlineData("..%2Fking")]
    [InlineData("king/../king")]
    [InlineData("king/..")]
    [InlineData("..")]
    [InlineData("C:\\king")]
    [InlineData("/etc/passwd")]
    [InlineData("KING")]
    [InlineData("king\n")]
    [InlineData("1king")]
    [InlineData("a23456789012345678901234x")]
    [InlineData("")]
    public async Task APathTraversalOrMalformedId_IsRefused_BeforeAnythingElse(string id)
    {
        var rig = NewRig(Plans.Member("king", avatar: HaPicture));
        rig.Gateway.Image = (_, _, _) => Task.FromResult<AvatarImage?>(new AvatarImage(Png, "image/png"));

        Assert.Null(await rig.Service.GetAsync(id, CancellationToken.None));

        AssertRefusedOnce(rig);
        Assert.Equal(0, rig.Gateway.ImageCalls);
        Assert.False(Directory.Exists(_cache));
    }

    [Fact]
    public async Task ACacheFileThatIsNotAnImage_IsFetchedAgain()
    {
        var rig = NewRig(Plans.Member("king", avatar: HaPicture));
        rig.Gateway.Image = (_, _, _) => Task.FromResult<AvatarImage?>(new AvatarImage(Png, "image/png"));
        await rig.Service.GetAsync("king", CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(_cache, "king"), "<html>nobody wrote this</html>");

        var image = await rig.Service.GetAsync("king", CancellationToken.None);

        Assert.Equal(Png, image?.Bytes);
        Assert.Equal(2, rig.Gateway.ImageCalls);
    }

    [Fact]
    public async Task ACacheThatCannotBeWritten_StillServesThePicture_AndLogsOneWarning()
    {
        var rig = Build([Plans.Member("king", avatar: HaPicture)], Path.Combine(_cache, "blocked", "avatars"));
        Directory.CreateDirectory(_cache);
        await File.WriteAllTextAsync(Path.Combine(_cache, "blocked"), "a file where the directory should be");
        rig.Gateway.Image = (_, _, _) => Task.FromResult<AvatarImage?>(new AvatarImage(Png, "image/png"));

        var image = await rig.Service.GetAsync("king", CancellationToken.None);

        Assert.Equal(Png, image?.Bytes);
        Assert.Single(rig.Log.Messages(LogLevel.Warning));
    }

    [Fact]
    public async Task TwoRequestsAtOnce_FetchOnce()
    {
        var rig = NewRig(Plans.Member("king", avatar: HaPicture));
        var release = new TaskCompletionSource();
        rig.Gateway.Image = async (_, _, _) =>
        {
            await release.Task;
            return new AvatarImage(Png, "image/png");
        };

        var first = rig.Service.GetAsync("king", CancellationToken.None);
        var second = rig.Service.GetAsync("king", CancellationToken.None);
        release.SetResult();

        Assert.NotNull(await first);
        Assert.NotNull(await second);
        Assert.Equal(1, rig.Gateway.ImageCalls);
    }

    [Fact]
    public async Task ACancelledRequest_IsNotLoggedAsARefusal()
    {
        var rig = NewRig(Plans.Member("king", avatar: HaPicture));
        using var cancelled = new CancellationTokenSource();
        rig.Gateway.Image = async (_, _, token) =>
        {
            await cancelled.CancelAsync();
            token.ThrowIfCancellationRequested();
            return null;
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => rig.Service.GetAsync("king", cancelled.Token));

        Assert.Empty(rig.Log.Entries);
    }

    // ---- helpers --------------------------------------------------------------------------------------------------

    private static void AssertRefusedOnce(Rig rig)
    {
        var warning = Assert.Single(rig.Log.Messages(LogLevel.Warning));
        Assert.DoesNotContain("life360.com", warning, StringComparison.OrdinalIgnoreCase);   // no address in the log
        Assert.DoesNotContain("0a1b2c3d4e5f", warning, StringComparison.Ordinal);
        Assert.Single(rig.Log.Entries);
    }

    private Rig NewRig(params ResolvedMember[] members) => Build(members, _cache);

    private Rig Build(ResolvedMember[] members, string cacheDirectory)
    {
        var discovery = new DiscoveryState();
        discovery.Publish(Plans.Discovery(members: members));
        var gateway = new FakeHaGateway();
        var time = new ManualTimeProvider(Start);
        var handler = new ScriptedHttpHandler(time);
        var client = new HttpClient(handler);
        _clients.Add(client);
        var log = new RecordingLogger<AvatarService>();
        return new Rig(new AvatarService(discovery, gateway, client, cacheDirectory, time, log), gateway, handler, time, log, discovery);
    }

    private static HttpResponseMessage PngResponse() => ImageResponse(Png, "image/png");

    private static HttpResponseMessage ImageResponse(byte[] bytes, string mediaType)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    // A body whose length is not announced, so only the counter can stop it.
    private sealed class ChunkedContent(byte[] bytes) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(bytes).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
