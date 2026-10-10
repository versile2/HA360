using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using Realm.Domain;
using Realm.Infrastructure.Ha;
using Realm.TestKit;
using Xunit;

namespace Realm.Ingestion.Tests;

/// <summary>
/// 03 section 2.6 and 02 section 10.3: relative request URIs, the bearer header, retries of 502, 503 and 504 (and of timeouts and refused connections,
/// and nothing else) on 1, 2, 5, 10 and 30 s, 250 ms between history requests, URL-encoded UTC instants, the image path restriction and the closed list of
/// HA calls. Nothing touches the network: a scripted handler answers and a manual clock keeps every wait.
/// </summary>
public sealed class HaRestClientTests : IDisposable
{
    private const string Token = "fake-supervisor-token-5678";

    private static readonly DateTimeOffset Start = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan[] NoWaits = [TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero];

    private readonly List<HttpClient> _clients = [];

    private sealed record Rig(HaRestClient Client, ScriptedHttpHandler Handler, ManualTimeProvider Time, RecordingLogger<HaRestClient> Log);

    public void Dispose()
    {
        foreach (var client in _clients)
        {
            client.Dispose();
        }
    }

    [Fact]
    public async Task Config_ReadsTheTimeZoneAndTheVersion_WithTheBearerToken()
    {
        var rig = NewRig(handler => handler.Respond(HttpStatusCode.OK, """{"time_zone":"America/Chicago","version":"2026.9.1","unit_system":{"length":"mi"}}"""));

        var config = await rig.Client.GetConfigAsync(CancellationToken.None);

        Assert.Equal(new HaConfig("America/Chicago", "2026.9.1", "mi"), config);
        var request = Assert.Single(rig.Handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("http://supervisor/core/api/config", request.Uri?.AbsoluteUri);
        Assert.Equal("Bearer " + Token, request.Headers["Authorization"]);
    }

    [Fact]
    public async Task Config_WithoutATimeZone_IsRefused()
    {
        var rig = NewRig(handler => handler.Respond(HttpStatusCode.OK, """{"version":"2026.9.1"}"""));

        await Assert.ThrowsAsync<InvalidDataException>(() => rig.Client.GetConfigAsync(CancellationToken.None));
    }

    [Fact]
    public async Task EveryCall_UsesARelativeUri_WithNoLeadingSlash_UnderTheBaseAddress()
    {
        // A request URI that began with a slash would replace the base path (/core/api/) and miss the Supervisor's proxy.
        var rig = NewRig(handler => handler
            .Respond(HttpStatusCode.OK, """{"time_zone":"UTC"}""")
            .Respond(HttpStatusCode.OK, "[]")
            .Respond(HttpStatusCode.OK, "{}")
            .Respond(HttpStatusCode.OK, "[]")
            .Respond(HttpStatusCode.OK, "[[]]")
            .RespondWith(_ => ImageResponse([1, 2, 3], "image/png")));

        await rig.Client.GetConfigAsync(CancellationToken.None);
        await rig.Client.GetStatesAsync(null, CancellationToken.None);
        await rig.Client.GetIntegrationEntitiesAsync(CancellationToken.None);
        await rig.Client.GetZonesAsync(CancellationToken.None);
        await rig.Client.GetHistoryAsync("device_tracker.king_pixel", Start, Start.AddHours(1), withAttributes: false, CancellationToken.None);
        await rig.Client.GetImageAsync("/api/image/serve/abc123/512x512", 1024, CancellationToken.None);

        Assert.Equal(6, rig.Handler.Requests.Count);
        foreach (var request in rig.Handler.Requests)
        {
            Assert.StartsWith("/core/api/", request.Uri?.AbsolutePath);
            Assert.Equal("supervisor", request.Uri?.Host);
        }
    }

    [Fact]
    public void TheClosedList_IsConfigStatesTemplateHistoryImageAndTheNotification()
    {
        // 02 section 10.3: the data layer calls no service but persistent_notification.create, writes no state and issues no admin command. A new public call is a decision, so it fails here.
        var calls = typeof(HaRestClient).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(method => method.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[] { "GetConfigAsync", "GetHistoryAsync", "GetImageAsync", "GetIntegrationEntitiesAsync", "GetStatesAsync", "GetZonesAsync", "NotifyAsync", "RenderTemplateAsync" },
            calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task Transient502_503_504_AreRetried(HttpStatusCode status)
    {
        var rig = NewRig(handler => handler
            .Respond(status)
            .Respond(status)
            .Respond(HttpStatusCode.OK, """{"time_zone":"UTC"}"""));

        var config = await rig.Client.GetConfigAsync(CancellationToken.None);

        Assert.Equal("UTC", config.TimeZone);
        Assert.Equal(3, rig.Handler.Requests.Count);
        Assert.Equal(2, rig.Log.Messages(Microsoft.Extensions.Logging.LogLevel.Warning).Count);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task OtherFailures_AreNotRetried(HttpStatusCode status)
    {
        var rig = NewRig(handler => handler.Respond(status));

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => rig.Client.GetConfigAsync(CancellationToken.None));

        Assert.Equal(status, error.StatusCode);
        Assert.Single(rig.Handler.Requests);
    }

    [Fact]
    public async Task ARefusedConnection_IsRetried()
    {
        var rig = NewRig(handler => handler
            .Fail(new HttpRequestException("Connection refused"))
            .Respond(HttpStatusCode.OK, """{"time_zone":"UTC"}"""));

        await rig.Client.GetConfigAsync(CancellationToken.None);

        Assert.Equal(2, rig.Handler.Requests.Count);
    }

    [Fact]
    public async Task TheRetriesRunOut_AndTheLastFailureIsThrown()
    {
        var rig = NewRig(handler =>
        {
            for (var i = 0; i < 6; i++)
            {
                handler.Respond(HttpStatusCode.BadGateway);
            }
        });

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => rig.Client.GetConfigAsync(CancellationToken.None));

        Assert.Equal(HttpStatusCode.BadGateway, error.StatusCode);
        Assert.Equal(6, rig.Handler.Requests.Count);   // the first attempt and five retries
    }

    [Fact]
    public async Task TheRetryDelays_Are1_2_5_10_And30Seconds()
    {
        var time = new ManualTimeProvider(Start);
        var handler = new ScriptedHttpHandler(time);
        for (var i = 0; i < 5; i++)
        {
            handler.Respond(HttpStatusCode.ServiceUnavailable);
        }

        handler.Respond(HttpStatusCode.OK, """{"time_zone":"UTC"}""");
        var client = NewClient(handler, time, new HaRestOptions { Token = Token }, out _);

        var call = client.GetConfigAsync(CancellationToken.None);
        foreach (var seconds in new[] { 1, 2, 5, 10, 30 })
        {
            await time.WaitForTimerAsync(TimeSpan.FromSeconds(seconds));
            time.Advance(TimeSpan.FromSeconds(seconds));
        }

        await call;
        Assert.Equal(
            new double[] { 0, 1, 3, 8, 18, 48 },
            handler.Requests.Select(request => (request.At - Start).TotalSeconds).ToArray());
    }

    [Fact]
    public async Task AnAttemptThatTimesOut_IsRetried()
    {
        var time = new ManualTimeProvider(Start);
        using var handler = new HangThenAnswer();
        var client = NewClient(handler, time, new HaRestOptions { Token = Token, RetryDelays = [TimeSpan.FromSeconds(1)] }, out _);

        var call = client.GetConfigAsync(CancellationToken.None);
        await time.WaitForTimerAsync(TimeSpan.FromSeconds(60));   // the per-attempt timeout
        time.Advance(TimeSpan.FromSeconds(60));
        await time.WaitForTimerAsync(TimeSpan.FromSeconds(1));    // the retry wait
        time.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal("UTC", (await call).TimeZone);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task ACancellationByTheCaller_IsNotRetried()
    {
        var rig = NewRig(handler => handler.Fail(new TaskCanceledException()));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => rig.Client.GetConfigAsync(cts.Token));
    }

    [Fact]
    public async Task HistoryRequests_AreSpacedBy250Milliseconds()
    {
        var time = new ManualTimeProvider(Start);
        var handler = new ScriptedHttpHandler(time).Respond(HttpStatusCode.OK, "[[]]").Respond(HttpStatusCode.OK, "[[]]").Respond(HttpStatusCode.OK, "[[]]");
        var client = NewClient(handler, time, new HaRestOptions { Token = Token }, out _);

        await client.GetHistoryAsync("sensor.fordpass_demo_fuel", Start, Start.AddHours(12), withAttributes: false, CancellationToken.None);
        var second = client.GetHistoryAsync("sensor.fordpass_demo_fuel", Start, Start.AddHours(12), withAttributes: false, CancellationToken.None);
        await time.WaitForTimerAsync(TimeSpan.FromMilliseconds(250));

        Assert.False(second.IsCompleted);
        Assert.Single(handler.Requests);
        time.Advance(TimeSpan.FromMilliseconds(250));
        await second;
        Assert.Equal(TimeSpan.FromMilliseconds(250), handler.Requests[1].At - handler.Requests[0].At);

        // Time that passed on its own counts: a third request after a long pause waits for nothing.
        time.Advance(TimeSpan.FromSeconds(5));
        await client.GetHistoryAsync("sensor.fordpass_demo_fuel", Start, Start.AddHours(12), withAttributes: false, CancellationToken.None);
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task HistoryInstants_AreUtcWithTheirOffset_UrlEncoded()
    {
        var rig = NewRig(handler => handler.Respond(HttpStatusCode.OK, "[[]]"));
        var localStart = new DateTimeOffset(2026, 10, 1, 8, 0, 0, TimeSpan.FromHours(-5));

        await rig.Client.GetHistoryAsync("device_tracker.life360_king", localStart, localStart.AddHours(12), withAttributes: true, CancellationToken.None);

        var uri = Assert.Single(rig.Handler.Requests).Uri!;
        Assert.Contains("history/period/2026-10-01T13%3A00%3A00%2B00%3A00?", uri.AbsoluteUri);
        Assert.Contains("end_time=2026-10-02T01%3A00%3A00%2B00%3A00", uri.AbsoluteUri);
        Assert.Contains("filter_entity_id=device_tracker.life360_king", uri.AbsoluteUri);
        Assert.Contains("significant_changes_only=0", uri.AbsoluteUri);
        Assert.DoesNotContain("minimal_response", uri.AbsoluteUri);
        Assert.DoesNotContain("no_attributes", uri.AbsoluteUri);
    }

    [Fact]
    public async Task HistoryOfASensor_AsksForMinimalResponseWithoutAttributes()
    {
        var rig = NewRig(handler => handler.Respond(HttpStatusCode.OK, "[[]]"));

        await rig.Client.GetHistoryAsync("sensor.fordpass_demo_odometer", Start, Start.AddHours(48), withAttributes: false, CancellationToken.None);

        var uri = Assert.Single(rig.Handler.Requests).Uri!;
        Assert.Contains("&minimal_response&no_attributes", uri.AbsoluteUri);
        Assert.Contains("significant_changes_only=0", uri.AbsoluteUri);
    }

    [Fact]
    public async Task History_MapsMinimalRowsToTheEntityOfTheFirstRow()
    {
        const string body = """
            [[
              {"entity_id":"sensor.fordpass_demo_fuel","state":"62","attributes":{"unit_of_measurement":"%"},"last_changed":"2026-09-30T10:00:00+00:00","last_updated":"2026-09-30T10:00:01+00:00"},
              {"state":"61","last_changed":"2026-09-30T11:00:00+00:00"},
              {"state":"unavailable","last_changed":"2026-09-30T11:30:00+00:00"}
            ]]
            """;
        var rig = NewRig(handler => handler.Respond(HttpStatusCode.OK, body));

        var rows = await rig.Client.GetHistoryAsync("sensor.fordpass_demo_fuel", Start, Start.AddHours(48), withAttributes: false, CancellationToken.None);

        Assert.Equal(3, rows.Count);
        Assert.All(rows, row => Assert.Equal("sensor.fordpass_demo_fuel", row.EntityId));
        Assert.Equal(new[] { "62", "61", "unavailable" }, rows.Select(row => row.State).ToArray());
        Assert.Equal(new DateTimeOffset(2026, 9, 30, 10, 0, 1, TimeSpan.Zero), rows[0].LastUpdatedUtc);
        Assert.Equal(new DateTimeOffset(2026, 9, 30, 11, 0, 0, TimeSpan.Zero), rows[1].LastChangedUtc);
        Assert.Equal(rows[1].LastChangedUtc, rows[1].LastUpdatedUtc);   // a minimal row has no last_updated: the change time stands in
        Assert.Empty(rows[1].Attributes);
        Assert.Equal("%", rows[0].Attributes["unit_of_measurement"].GetString());
    }

    [Fact]
    public async Task States_CopyOnlyTheEntitiesThatWereAskedFor()
    {
        const string body = """
            [
              {"entity_id":"person.king","state":"home","attributes":{"user_id":"user-aaa","device_trackers":["device_tracker.life360_king"]},"last_changed":"2026-09-30T10:00:00+00:00","last_updated":"2026-09-30T10:00:00+00:00"},
              {"entity_id":"light.hall","state":"on","attributes":{},"last_changed":"2026-09-30T10:00:00+00:00","last_updated":"2026-09-30T10:00:00+00:00"},
              {"entity_id":"zone.home","state":"1","attributes":{"latitude":31.1,"longitude":-85.3,"radius":100.0},"last_changed":"2026-09-30T10:00:00+00:00","last_updated":"2026-09-30T10:00:00+00:00"}
            ]
            """;
        var rig = NewRig(handler => handler.Respond(HttpStatusCode.OK, body));

        var states = await rig.Client.GetStatesAsync(id => id.StartsWith("person.", StringComparison.Ordinal) || id.StartsWith("zone.", StringComparison.Ordinal), CancellationToken.None);

        Assert.Equal(new[] { "person.king", "zone.home" }, states.Select(state => state.EntityId).ToArray());
        Assert.Equal("user-aaa", states[0].Attributes["user_id"].GetString());
        Assert.Equal(100.0, states[1].Attributes["radius"].GetDouble());
        Assert.Equal(new DateTimeOffset(2026, 9, 30, 10, 0, 0, TimeSpan.Zero), states[0].LastUpdatedUtc);
    }

    [Fact]
    public async Task Template_IsAReadOnlyPost_WithAJsonBody()
    {
        var rig = NewRig(handler => handler.Respond(HttpStatusCode.OK, "hello", "text/plain"));

        var text = await rig.Client.RenderTemplateAsync("{{ 1 + 1 }}", CancellationToken.None);

        Assert.Equal("hello", text);
        var request = Assert.Single(rig.Handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/core/api/template", request.Uri?.AbsolutePath);
        Assert.Equal("{{ 1 + 1 }}", TemplateOf(request));
    }

    [Fact]
    public async Task Notify_PostsOnePersistentNotification_WithItsStableId()
    {
        var rig = NewRig(handler => handler.Respond(HttpStatusCode.OK, "[]"));

        await rig.Client.NotifyAsync("ha_cartographer_device_tracker.pixel_8", "HA Cartographer: New tracker found", "Pixel 8 was added to People.", CancellationToken.None);

        var request = Assert.Single(rig.Handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/core/api/services/persistent_notification/create", request.Uri?.AbsolutePath);
        Assert.Equal("Bearer " + Token, request.Headers["Authorization"]);
        using var body = System.Text.Json.JsonDocument.Parse(request.Body!);
        Assert.Equal("ha_cartographer_device_tracker.pixel_8", body.RootElement.GetProperty("notification_id").GetString());
        Assert.Equal("HA Cartographer: New tracker found", body.RootElement.GetProperty("title").GetString());
        Assert.Equal("Pixel 8 was added to People.", body.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Notify_AnErrorStatus_Throws()
    {
        var rig = NewRig(handler => handler.Respond(HttpStatusCode.Unauthorized));

        await Assert.ThrowsAsync<HttpRequestException>(() => rig.Client.NotifyAsync("ha_cartographer_x", "HA Cartographer: x", "x", CancellationToken.None));
    }

    [Fact]
    public async Task IntegrationEntities_AreReadFromTheDiscoveryTemplate()
    {
        const string answer = """{"life360":["device_tracker.life360_king"],"mobile_app":["device_tracker.king_pixel","sensor.king_pixel_battery_level"]}""";
        var rig = NewRig(handler => handler.Respond(HttpStatusCode.OK, answer, "text/plain"));

        var entities = await rig.Client.GetIntegrationEntitiesAsync(CancellationToken.None);

        Assert.Equal(new[] { "device_tracker.life360_king" }, entities.Life360.ToArray());
        Assert.Equal(new[] { "device_tracker.king_pixel", "sensor.king_pixel_battery_level" }, entities.MobileApp.ToArray());
        var template = TemplateOf(rig.Handler.Requests[0]);
        Assert.Contains("integration_entities('life360')", template);
        Assert.Contains("integration_entities('mobile_app')", template);
        Assert.DoesNotContain("fordpass", template);
    }

    [Fact]
    public async Task Zones_AreReadFromTheZoneTemplate_AndAnInvalidZoneIsDropped()
    {
        const string answer = """
            [
              {"id":"zone.home","name":"Hearth","lat":31.1,"lon":-85.3,"r":100,"passive":false},
              {"id":"zone.work_2","name":" Work ","lat":31.2,"lon":-85.4,"r":250.5,"passive":true},
              {"id":"zone.broken","name":"Broken","lat":null,"lon":-85.4,"r":50,"passive":false},
              {"id":"zone.far","name":"Far","lat":131.2,"lon":-85.4,"r":50,"passive":false}
            ]
            """;
        var rig = NewRig(handler => handler.Respond(HttpStatusCode.OK, answer, "text/plain"));

        var zones = await rig.Client.GetZonesAsync(CancellationToken.None);

        Assert.Equal(
            new[] { new RawPlace("home", "Hearth", 31.1, -85.3, 100, false), new RawPlace("work_2", "Work", 31.2, -85.4, 250.5, true) },
            zones.ToArray());
    }

    [Theory]
    [InlineData("/api/image/serve/0a1b2c3d/512x512", "image/serve/0a1b2c3d/512x512")]
    [InlineData("api/image/serve/0a1b2c3d/512x512", "image/serve/0a1b2c3d/512x512")]
    [InlineData("image/serve/abc_DEF-1/original", "image/serve/abc_DEF-1/original")]
    public async Task Image_AcceptsOnlyImageServePaths(string given, string expectedPath)
    {
        var rig = NewRig(handler => handler.RespondWith(_ => ImageResponse([1, 2, 3, 4], "image/jpeg")));

        var image = await rig.Client.GetImageAsync(given, 1024, CancellationToken.None);

        Assert.NotNull(image);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, image.Bytes);
        Assert.Equal("image/jpeg", image.ContentType);
        Assert.Equal("/core/api/" + expectedPath, Assert.Single(rig.Handler.Requests).Uri?.AbsolutePath);
    }

    [Theory]
    [InlineData("/api/states")]
    [InlineData("/api/config")]
    [InlineData("image/serve/../config")]
    [InlineData("image/serve/abc/../../config")]
    [InlineData("image/serve/abc/512x512?token=1")]
    [InlineData("image/serve/abc/512x512/extra")]
    [InlineData("image/serve/%2e%2e/config")]
    [InlineData("//evil.example/image/serve/a/b")]
    [InlineData("https://evil.example/api/image/serve/a/b")]
    [InlineData("api/image_proxy/camera.front")]
    [InlineData("/local/avatar.png")]
    [InlineData("")]
    [InlineData("image/serve")]
    public async Task Image_RefusesEveryOtherPath_BeforeAnyRequest(string given)
    {
        var rig = NewRig(_ => { });

        await Assert.ThrowsAsync<ArgumentException>(() => rig.Client.GetImageAsync(given, 1024, CancellationToken.None));

        Assert.Empty(rig.Handler.Requests);
    }

    [Fact]
    public async Task Image_IsNullUnlessHaAnswers200_AndARedirectIsAFailure()
    {
        var rig = NewRig(handler => handler
            .Respond(HttpStatusCode.NotFound)
            .RespondWith(_ =>
            {
                var redirect = new HttpResponseMessage(HttpStatusCode.Found);
                redirect.Headers.Location = new Uri("https://evil.example/a.png");
                return redirect;
            }));

        Assert.Null(await rig.Client.GetImageAsync("image/serve/abc/512x512", 1024, CancellationToken.None));
        Assert.Null(await rig.Client.GetImageAsync("image/serve/abc/512x512", 1024, CancellationToken.None));

        Assert.Equal(2, rig.Handler.Requests.Count);   // the redirect target was never fetched
    }

    [Fact]
    public async Task Image_IsNotRetried_WhenHaIsRestarting()
    {
        var rig = NewRig(handler => handler.Respond(HttpStatusCode.BadGateway));

        Assert.Null(await rig.Client.GetImageAsync("image/serve/abc/512x512", 1024, CancellationToken.None));

        Assert.Single(rig.Handler.Requests);
    }

    [Fact]
    public async Task Image_OverTheLimit_IsRefused_WhetherOrNotTheLengthIsAnnounced()
    {
        var bytes = new byte[2048];
        var rig = NewRig(handler => handler
            .RespondWith(_ => ImageResponse(bytes, "image/png"))
            .RespondWith(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new UnannouncedLengthContent(bytes, "image/png"),
            }));

        await Assert.ThrowsAsync<InvalidDataException>(() => rig.Client.GetImageAsync("image/serve/abc/512x512", 1024, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidDataException>(() => rig.Client.GetImageAsync("image/serve/abc/512x512", 1024, CancellationToken.None));
    }

    [Fact]
    public async Task TheToken_IsNeverLogged_AndNotInTheOptionsText()
    {
        var rig = NewRig(handler => handler
            .Respond(HttpStatusCode.BadGateway)
            .Fail(new HttpRequestException("Connection refused"))
            .Respond(HttpStatusCode.OK, """{"time_zone":"UTC"}"""));

        await rig.Client.GetConfigAsync(CancellationToken.None);

        Assert.DoesNotContain(Token, rig.Log.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(Token, new HaRestOptions { Token = Token }.ToString(), StringComparison.Ordinal);
        Assert.All(rig.Handler.Requests, request => Assert.Equal("Bearer " + Token, request.Headers["Authorization"]));
    }

    // ---- helpers ------------------------------------------------------------------------------------------------

    // The template text of a POST body, read back from its JSON (the encoder escapes quotes and plus signs).
    private static string TemplateOf(ScriptedRequest request)
    {
        using var document = System.Text.Json.JsonDocument.Parse(request.Body!);
        return document.RootElement.GetProperty("template").GetString()!;
    }

    private Rig NewRig(Action<ScriptedHttpHandler> script)
    {
        var time = new ManualTimeProvider(Start);
        var handler = new ScriptedHttpHandler(time);
        script(handler);
        var client = NewClient(handler, time, new HaRestOptions { Token = Token, RetryDelays = NoWaits }, out var log);
        return new Rig(client, handler, time, log);
    }

    private HaRestClient NewClient(HttpMessageHandler handler, ManualTimeProvider time, HaRestOptions options, out RecordingLogger<HaRestClient> log)
    {
        var http = new HttpClient(handler, disposeHandler: false) { BaseAddress = options.BaseAddress, Timeout = Timeout.InfiniteTimeSpan };
        _clients.Add(http);
        log = new RecordingLogger<HaRestClient>();
        return new HaRestClient(http, options, time, log);
    }

    private static HttpResponseMessage ImageResponse(byte[] bytes, string contentType)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    // The first request never answers until it is cancelled (the per-attempt timeout); the next one gets a configuration.
    private sealed class HangThenAnswer : HttpMessageHandler
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _calls) == 1)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"time_zone":"UTC"}""", System.Text.Encoding.UTF8, "application/json"),
            };
        }
    }

    // A body whose length is not known up front (chunked), so the limit has to be enforced while reading.
    private sealed class UnannouncedLengthContent : HttpContent
    {
        private readonly byte[] _bytes;

        public UnannouncedLengthContent(byte[] bytes, string contentType)
        {
            _bytes = bytes;
            Headers.ContentType = new MediaTypeHeaderValue(contentType);
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(_bytes, 0, _bytes.Length);

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
