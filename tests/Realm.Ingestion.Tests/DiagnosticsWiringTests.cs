using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Realm.Domain;
using Realm.Infrastructure.Avatars;
using Realm.Infrastructure.Data;
using Realm.Infrastructure.Diagnostics;
using Realm.Infrastructure.Ha;
using Realm.Infrastructure.Ingestion;
using Realm.Infrastructure.Options;
using Realm.TestKit;
using Xunit;

namespace Realm.Ingestion.Tests;

/// <summary>
/// The call sites of <see cref="ServiceCounters"/> (03 section 9.2): each service counts what it does, at the place it already passes, and
/// <see cref="DiagnosticsSnapshotBuilder"/> shows it. The websocket counts connects, reconnects and messages; the REST client calls and failures (a retry is not
/// another call, a cancellation is not a failure); the pipeline items, skipped items and the queue depth; the writer commits, rows, depth and drops; the schema
/// step the schema version and whether the last run stopped cleanly; the backfill and the retention job their runs; the avatar proxy its fetches and failures; the
/// refresher the size of the watch list. Real loopback sockets, a real temporary database and a manual clock; nothing here touches the network or sleeps.
/// </summary>
public sealed class DiagnosticsWiringTests : IDisposable
{
    private const string HaPicture = "/api/image/serve/0a1b2c3d4e5f/512x512";
    private const string Home = "zone.home";

    private static readonly DateTimeOffset Start = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan OneSecond = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan[] NoWaits = [TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero];
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13];

    private readonly List<HttpClient> _clients = [];
    private readonly List<string> _folders = [];

    public void Dispose()
    {
        foreach (var client in _clients)
        {
            client.Dispose();
        }

        foreach (var folder in _folders.Where(Directory.Exists))
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    // ---- the websocket ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task TheWebsocket_CountsItsConnects_ItsMessages_AndTheReconnectsAfterALoss()
    {
        var counters = new ServiceCounters(new ManualTimeProvider(Start));
        await using var rig = await ConnectionRig.StartAsync(SeedHome, counters: counters);
        var first = await rig.NextSessionAsync();
        await rig.WaitForItemsAsync(1);
        await rig.WaitForStateAsync(HaConnectionState.Connected);

        Assert.Equal(1, counters.WsConnects);
        Assert.Equal(0, counters.WsReconnects);
        Assert.True(counters.WsMessages > 0);   // the result of the subscription, and the snapshot
        Assert.Equal(rig.Connection.MessageCount, counters.WsMessages);
        Assert.Equal(rig.Time.GetUtcNow(), counters.LastWsMessageUtc);

        var before = counters.WsMessages;
        await first.SendEventAsync("""{"c":{"zone.home":{"+":{"s":"1"}}}}""");
        await first.SendEventAsync("""{"c":{"zone.home":{"+":{"s":"2"}}}}""");
        await rig.WaitForItemsAsync(3);

        Assert.Equal(before + 2, counters.WsMessages);
        Assert.Equal(rig.Connection.MessageCount, counters.WsMessages);
        Assert.Equal(counters.WsMessages, counters.WsMessagesPerMinute(rig.Time.GetUtcNow()));   // all of them arrived within the minute
        Assert.Equal(1, counters.WsConnects);

        first.Abort();
        await rig.WaitForStateAsync(HaConnectionState.Reconnecting);
        await rig.TimerAsync(OneSecond);
        rig.Time.Advance(OneSecond);
        await rig.NextSessionAsync();
        await rig.WaitForStateAsync(HaConnectionState.Connected);

        Assert.Equal(2, counters.WsConnects);
        Assert.Equal(1, counters.WsReconnects);
        Assert.Equal(rig.Connection.ReconnectCount, counters.WsReconnects);
    }

    [Fact]
    public async Task AConnectionThatNeverGotAsFarAsBeingEstablished_IsNeitherAConnectNorAReconnect()
    {
        var counters = new ServiceCounters(new ManualTimeProvider(Start));
        await using var rig = await ConnectionRig.StartAsync(server => server.ScriptNext(FakeHaAttempt.Reject(502)), counters: counters);
        await rig.WaitForStateAsync(HaConnectionState.Reconnecting);

        Assert.Equal(0, counters.WsConnects);
        Assert.Equal(0, counters.WsReconnects);
        Assert.Equal(0, counters.WsMessages);
        Assert.Null(counters.LastWsMessageUtc);
    }

    // ---- REST ------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task ARestCall_IsCounted_AndAnAnsweredOneIsNoFailure()
    {
        var counters = new ServiceCounters(new ManualTimeProvider(Start));
        var client = NewRest(new ScriptedHttpHandler().Respond(HttpStatusCode.OK, """{"time_zone":"UTC","version":"2026.9.1"}"""), counters);

        await client.GetConfigAsync(CancellationToken.None);

        Assert.Equal(1, counters.RestCalls);
        Assert.Equal(0, counters.RestFailures);
    }

    [Fact]
    public async Task ARestCall_ThatEndsInAnError_IsAFailure()
    {
        var counters = new ServiceCounters(new ManualTimeProvider(Start));
        var client = NewRest(new ScriptedHttpHandler().Respond(HttpStatusCode.Unauthorized), counters);

        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetConfigAsync(CancellationToken.None));

        Assert.Equal(1, counters.RestCalls);
        Assert.Equal(1, counters.RestFailures);
    }

    [Fact]
    public async Task ARetriedCall_CountsOnce_AndIsNoFailureWhenTheRetrySucceeds()
    {
        var counters = new ServiceCounters(new ManualTimeProvider(Start));
        var handler = new ScriptedHttpHandler()
            .Respond(HttpStatusCode.BadGateway)
            .Respond(HttpStatusCode.BadGateway)
            .Respond(HttpStatusCode.OK, """{"time_zone":"UTC"}""");
        var client = NewRest(handler, counters);

        await client.GetConfigAsync(CancellationToken.None);

        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal(1, counters.RestCalls);
        Assert.Equal(0, counters.RestFailures);
    }

    [Fact]
    public async Task ACallWhoseRetriesRunOut_IsOneFailure()
    {
        var counters = new ServiceCounters(new ManualTimeProvider(Start));
        var handler = new ScriptedHttpHandler();
        for (var attempt = 0; attempt <= NoWaits.Length; attempt++)
        {
            handler.Respond(HttpStatusCode.ServiceUnavailable);
        }

        var client = NewRest(handler, counters);

        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetConfigAsync(CancellationToken.None));

        Assert.Equal(NoWaits.Length + 1, handler.Requests.Count);
        Assert.Equal(1, counters.RestCalls);
        Assert.Equal(1, counters.RestFailures);
    }

    [Fact]
    public async Task ACallTheCallerCancelled_IsNotAFailure()
    {
        var counters = new ServiceCounters(new ManualTimeProvider(Start));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var client = NewRest(new FailingHandler(new OperationCanceledException(cancelled.Token)), counters);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetConfigAsync(cancelled.Token));

        Assert.Equal(1, counters.RestCalls);
        Assert.Equal(0, counters.RestFailures);
    }

    [Fact]
    public async Task AnImageHomeAssistantDoesNotAnswer200_IsAFailure_EvenThoughItReturnsNull()
    {
        var counters = new ServiceCounters(new ManualTimeProvider(Start));
        var client = NewRest(new ScriptedHttpHandler().Respond(HttpStatusCode.NotFound), counters);

        var image = await client.GetImageAsync(HaPicture, 1024, CancellationToken.None);

        Assert.Null(image);
        Assert.Equal(1, counters.RestCalls);
        Assert.Equal(1, counters.RestFailures);
    }

    // ---- the pipeline ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task ThePipeline_CountsItsItems_AndTheEventsOfTheLastMinute()
    {
        await using var rig = await StoreRig.StartAsync();

        await rig.DiscoverAsync(Plans.Member("king", life360: Plans.KingTracker));
        await rig.FeedAsync(Drives.Life360Row(Plans.KingTracker, rig.Time.GetUtcNow(), 0, 0));

        Assert.Equal(2, rig.Counters.IngestItems);
        Assert.Equal(rig.Pipeline.ProcessedCount, rig.Counters.IngestItems);
        Assert.Equal(2, rig.Counters.IngestEventsPerMinute(rig.Time.GetUtcNow()));
        Assert.Equal(0, rig.Counters.IngestSkipped);

        rig.Time.Advance(TimeSpan.FromSeconds(61));

        Assert.Equal(0, rig.Counters.IngestEventsPerMinute(rig.Time.GetUtcNow()));
        Assert.Equal(2, rig.Counters.IngestItems);
    }

    [Fact]
    public async Task TheQueueDepth_IsWhatWaitsInThePipelinesQueue()
    {
        await using var rig = await StoreRig.StartAsync();

        for (var i = 0; i < 3; i++)
        {
            await rig.Pipeline.EnqueueAsync(new ZonesUpdated([]), CancellationToken.None);
        }

        Assert.Equal(3, rig.Counters.IngestQueueDepth);

        await rig.Pipeline.StartAsync(CancellationToken.None);
        Assert.True(SpinWait.SpinUntil(() => rig.Pipeline.ProcessedCount == 3, TimeSpan.FromSeconds(10)), "The consumer did not drain the queue");
        await rig.Pipeline.StopAsync(CancellationToken.None);

        Assert.Equal(0, rig.Counters.IngestQueueDepth);
        Assert.Equal(3, rig.Counters.IngestItems);
    }

    [Fact]
    public async Task AnItemThatFails_IsCountedAsSkipped_AndTheOnesAroundItAsProcessed()
    {
        await using var rig = await StoreRig.StartAsync();
        await rig.DiscoverAsync(Plans.Member("king", life360: Plans.KingTracker));
        await rig.Pipeline.StartAsync(CancellationToken.None);
        var processed = rig.Pipeline.ProcessedCount;
        await rig.Pipeline.EnqueueAsync(new ZonesUpdated([]), CancellationToken.None);
        Assert.True(SpinWait.SpinUntil(() => rig.Pipeline.ProcessedCount > processed, TimeSpan.FromSeconds(10)), "The consumer did not start");

        await rig.Pipeline.EnqueueAsync(new DiscoveryUpdated(null!), CancellationToken.None);   // a bug: processing it throws
        await rig.Pipeline.EnqueueAsync(new ZonesUpdated([]), CancellationToken.None);
        await rig.Pipeline.StopAsync(CancellationToken.None);

        Assert.Equal(1, rig.Counters.IngestSkipped);
        Assert.Equal(rig.Pipeline.ProcessedCount, rig.Counters.IngestItems);
        Assert.Equal(0, rig.Counters.IngestQueueDepth);
    }

    [Fact]
    public async Task TheStaleThresholds_AreThoseOfTheLiveMembers_WithTheHeartbeatInThem()
    {
        await using var rig = await StoreRig.StartAsync(OptionsBinding.Defaults with { UiStaleAfterMinutes = 20, FusionStaleGraceMinutes = 10 });
        await rig.DiscoverAsync(Plans.Member("king", life360: Plans.KingTracker));

        var thresholds = rig.Pipeline.StaleAfterMinutes();

        // No heartbeat observed yet: 30 minutes plus the 10 of grace is above the option's 20.
        Assert.Equal(["king"], thresholds.Keys);
        Assert.Equal(40, thresholds["king"]);
    }

    // ---- the writer ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task TheWriter_CountsItsCommitsAndRows_AndTheDepthBeforeAndAfterTheFlush()
    {
        await using var rig = await StoreRig.StartAsync();
        for (var i = 0; i < 3; i++)
        {
            Assert.True(rig.Writer.EnqueueFix("king", Drives.Fix(Drives.Life360Row(Plans.KingTracker, rig.Time.GetUtcNow().AddSeconds(i), 0, 0))));
        }

        Assert.Equal(3, rig.Counters.WriterQueueDepth);
        Assert.Equal(0, rig.Counters.DbCommits);
        Assert.Null(rig.Counters.LastDbCommitUtc);

        await rig.Writer.FlushAsync();

        Assert.Equal(0, rig.Counters.WriterQueueDepth);
        Assert.Equal(1, rig.Counters.DbCommits);
        Assert.Equal(3, rig.Counters.DbRows);
        Assert.Equal(rig.Time.GetUtcNow(), rig.Counters.LastDbCommitUtc);
        Assert.Equal(rig.Writer.CommittedRows, rig.Counters.DbRows);
        Assert.Equal(0, rig.Counters.WriterDropped);
    }

    [Fact]
    public void AFullQueue_CountsEveryRowItRefused_AndRaisesTheTwoWarningsOfIt()
    {
        var time = new ManualTimeProvider(Start);
        var counters = new ServiceCounters(time);
        using var provider = FactoryOver(NewFolder() + "/never-opened.db");
        using var writer = new DbWriter(provider.GetRequiredService<IDbContextFactory<RealmDb>>(), time, new RecordingLogger<DbWriter>(), counters);
        for (var i = 0; i < DbWriter.QueueCapacity; i++)
        {
            Assert.True(writer.EnqueueFix("king", Fix(i), inTrack: true));
        }

        Assert.Equal(DbWriter.QueueCapacity, counters.WriterQueueDepth);
        Assert.Equal(0, counters.WriterDropped);

        // Nothing queued is a diagnostic row to give way, so the next row is refused: the queue never grows past its bound.
        Assert.False(writer.EnqueueFix("king", Fix(DbWriter.QueueCapacity), inTrack: true));
        Assert.False(writer.EnqueueFix("king", Fix(DbWriter.QueueCapacity + 1), inTrack: false, reason: TrackReason.Accuracy));

        Assert.Equal(DbWriter.QueueCapacity, counters.WriterQueueDepth);
        Assert.Equal(2, counters.WriterDropped);

        var warnings = NewBuilder(counters, time).GetSnapshot().Warnings;
        Assert.Equal(["ingest_drops", "writer_queue_over_80pct"], warnings);
    }

    [Fact]
    public void ADiagnosticRowThatGivesWay_IsCountedAsDropped()
    {
        var time = new ManualTimeProvider(Start);
        var counters = new ServiceCounters(time);
        using var provider = FactoryOver(NewFolder() + "/never-opened.db");
        using var writer = new DbWriter(provider.GetRequiredService<IDbContextFactory<RealmDb>>(), time, new RecordingLogger<DbWriter>(), counters);
        for (var i = 0; i < DbWriter.QueueCapacity; i++)
        {
            Assert.True(writer.EnqueueFix("king", Fix(i), inTrack: false, reason: TrackReason.Accuracy));
        }

        Assert.True(writer.EnqueueFix("king", Fix(DbWriter.QueueCapacity), inTrack: true));   // a row that matters evicts the oldest diagnostic row

        Assert.Equal(DbWriter.QueueCapacity, counters.WriterQueueDepth);
        Assert.Equal(1, counters.WriterDropped);
    }

    // ---- the schema step -------------------------------------------------------------------------------------------

    [Fact]
    public async Task TheSchemaStep_ReportsTheSchemaVersion_AndANewDatabaseStoppedCleanlyByDefinition()
    {
        await using var rig = await StoreRig.StartAsync();

        Assert.True(rig.Counters.SchemaVersion > 0);
        Assert.False(rig.Counters.UncleanShutdownAtStart);
    }

    [Fact]
    public async Task TheSchemaStep_FindsWhetherThePreviousRunStoppedCleanly()
    {
        await using var rig = await StoreRig.StartAsync();
        var log = new RecordingLogger<SchemaBootstrap>();

        rig.Exec("INSERT OR REPLACE INTO meta(key, value) VALUES ('clean_shutdown', '1')");   // what a graceful stop leaves
        var clean = new ServiceCounters(rig.Time);
        new SchemaBootstrap(rig.FilePath, log, rig.Time, clean).Run();

        // The run above marked the file as not yet cleanly stopped, and nobody stopped it: the next start finds the marker at 0.
        var unclean = new ServiceCounters(rig.Time);
        new SchemaBootstrap(rig.FilePath, log, rig.Time, unclean).Run();

        Assert.False(clean.UncleanShutdownAtStart);
        Assert.True(unclean.UncleanShutdownAtStart);
        Assert.Equal(rig.Counters.SchemaVersion, clean.SchemaVersion);
        Assert.Equal(rig.Counters.SchemaVersion, unclean.SchemaVersion);
        Assert.Contains("clean_shutdown = 0 found at start", string.Join('\n', log.Messages(LogLevel.Warning)), StringComparison.Ordinal);

        var warnings = NewBuilder(unclean, rig.Time).GetSnapshot().Warnings;
        Assert.Equal(["unclean_shutdown"], warnings);
        Assert.Empty(NewBuilder(clean, rig.Time).GetSnapshot().Warnings);
    }

    // ---- the jobs --------------------------------------------------------------------------------------------------

    [Fact]
    public async Task TheBackfill_CountsAFinishedRun_AndTheRowsItQueued()
    {
        await using var rig = await StoreRig.StartAsync();
        rig.History.Add(Drives.DriveRows(StoreRig.Start.AddDays(-3)));
        await rig.DiscoverAsync(Plans.Member("king", life360: Plans.KingTracker));
        var backfill = rig.NewBackfill();

        await backfill.RunOnceAsync(CancellationToken.None);

        Assert.Equal(1, rig.Counters.BackfillRuns);
        Assert.Equal(backfill.RowsBackfilled, rig.Counters.BackfillRows);
        Assert.True(rig.Counters.BackfillRows > 0);

        await backfill.RunOnceAsync(CancellationToken.None);

        Assert.Equal(2, rig.Counters.BackfillRuns);   // a second run is a run of its own
        Assert.Equal(backfill.RowsBackfilled, rig.Counters.BackfillRows);
    }

    [Fact]
    public async Task ABackfillThatIsOff_IsNotARun()
    {
        await using var rig = await StoreRig.StartAsync(OptionsBinding.Defaults with { BackfillDays = 0 });

        await rig.NewBackfill().RunOnceAsync(CancellationToken.None);

        Assert.Equal(0, rig.Counters.BackfillRuns);
    }

    [Fact]
    public async Task TheRetentionJob_CountsAFinishedRun_AndTheRowsItDeleted()
    {
        var thirtyDays = OptionsBinding.Defaults with { RetentionFixDays = 30 };
        await using var rig = await StoreRig.StartAsync(thirtyDays);
        await rig.StoreFixesAsync(
            "king",
            [
                Drives.Fix(Drives.Life360Row(Plans.KingTracker, StoreRig.Start.AddDays(-90), 0, 0)),
                Drives.Fix(Drives.Life360Row(Plans.KingTracker, StoreRig.Start.AddDays(-80), 0, 0)),
                Drives.Fix(Drives.Life360Row(Plans.KingTracker, StoreRig.Start.AddDays(-1), 0, 0)),
            ]);
        var retention = rig.NewRetention();

        var deleted = await retention.PruneAsync(CancellationToken.None);
        await retention.PruneAsync(CancellationToken.None);

        Assert.Equal(2, deleted);
        Assert.Equal(2, rig.Counters.RetentionRuns);
        Assert.Equal(2, rig.Counters.RetentionRowsDeleted);   // the second run deleted nothing
    }

    // ---- the avatar proxy ------------------------------------------------------------------------------------------

    [Fact]
    public async Task TheAvatarProxy_CountsAFetchOnce_AndNotThePicturesItServesFromItsCache()
    {
        var counters = new ServiceCounters(new ManualTimeProvider(Start));
        var (service, gateway, _) = NewAvatars(counters);
        gateway.Image = (_, _, _) => Task.FromResult<AvatarImage?>(new AvatarImage(Png, "image/png"));

        Assert.NotNull(await service.GetAsync("king", CancellationToken.None));
        Assert.NotNull(await service.GetAsync("king", CancellationToken.None));

        Assert.Equal(1, counters.AvatarFetches);
        Assert.Equal(0, counters.AvatarFailures);
    }

    [Fact]
    public async Task TheAvatarProxy_CountsEveryRefusalAsAFailure()
    {
        var counters = new ServiceCounters(new ManualTimeProvider(Start));
        var (service, gateway, time) = NewAvatars(counters);
        gateway.Image = (_, _, _) => Task.FromResult<AvatarImage?>(null);

        Assert.Null(await service.GetAsync("king", CancellationToken.None));
        time.Advance(AvatarService.RevalidateAfter);   // a refusal is remembered for the revalidation period (R3-08)
        gateway.Image = (_, _, _) => Task.FromResult<AvatarImage?>(new AvatarImage([1, 2, 3, 4], "image/png"));   // the bytes are not a picture
        Assert.Null(await service.GetAsync("king", CancellationToken.None));

        Assert.Equal(2, counters.AvatarFetches);
        Assert.Equal(2, counters.AvatarFailures);
    }

    [Fact]
    public async Task AnAvatarRequestForAMemberThatHasNone_IsNotAFetch()
    {
        var counters = new ServiceCounters(new ManualTimeProvider(Start));
        var (service, _, _) = NewAvatars(counters);

        Assert.Null(await service.GetAsync("nobody", CancellationToken.None));

        Assert.Equal(0, counters.AvatarFetches);
        Assert.Equal(0, counters.AvatarFailures);
    }

    // ---- the refresher ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task TheDiscoveryRefresher_ReportsTheSizeOfTheWatchList()
    {
        var counters = new ServiceCounters(new ManualTimeProvider(Start));
        var time = new ManualTimeProvider(Start);
        var gateway = new FakeHaGateway { States = [new HaEntitySnapshot(Home, "0", new Dictionary<string, JsonElement>(), Start, Start)] };
        using var refresher = new HaDiscoveryRefresher(
            gateway,
            OptionsBinding.Defaults,
            new DiscoveryState(),
            (_, _) => ValueTask.CompletedTask,
            time,
            new RecordingLogger<HaDiscoveryRefresher>(),
            counters);

        await refresher.StartAsync(CancellationToken.None);
        Assert.True(SpinWait.SpinUntil(() => refresher.LastRefreshUtc is not null, TimeSpan.FromSeconds(10)), "The first discovery did not finish");
        await refresher.StopAsync(CancellationToken.None);

        Assert.Equal(1, refresher.WatchedEntityCount);   // the zone, which has to be watched to update
        Assert.Equal(refresher.WatchedEntityCount, counters.WatchedEntities);
    }

    // ---- the whole file, from the services ---------------------------------------------------------------------------

    [Fact]
    public async Task TheFile_ShowsWhatTheServicesCounted()
    {
        await using var rig = await StoreRig.StartAsync();
        await rig.DiscoverAsync(Plans.Member("king", life360: Plans.KingTracker));
        await rig.FeedAsync(Drives.Life360Row(Plans.KingTracker, rig.Time.GetUtcNow(), 0, 0));
        await rig.Writer.FlushAsync();
        var builder = NewBuilder(rig.Counters, rig.Time, rig.State, rig.FilePath);

        var snapshot = builder.GetSnapshot();

        Assert.Equal("live", snapshot.Mode);
        Assert.Equal(2, snapshot.Ingestion.EventsPerMinute);
        Assert.Equal(0, snapshot.Ingestion.QueueDepth);
        Assert.Equal(0, snapshot.Ingestion.Dropped);
        Assert.Equal(rig.Counters.SchemaVersion, snapshot.Db.SchemaVersion);
        Assert.Equal(rig.Time.GetUtcNow(), snapshot.Db.LastCommitUtc);
        Assert.Equal(0, snapshot.Db.WriterQueueDepth);
        Assert.True(snapshot.Db.SizeBytes > 0);   // the real file, measured and not named
        Assert.Equal(["king"], snapshot.Members.Select(member => member.Id));
        Assert.DoesNotContain(rig.FilePath, JsonSerializer.Serialize(snapshot), StringComparison.Ordinal);
    }

    // ---- builders --------------------------------------------------------------------------------------------------

    private static void SeedHome(FakeHaServer server) =>
        server.SetEntity(Home, "0", new Dictionary<string, object?> { ["radius"] = 100 });

    private static RawFix Fix(int second) => Drives.Fix(Drives.Life360Row(Plans.KingTracker, Start.AddSeconds(second), 0, 0));

    private static DiagnosticsSnapshotBuilder NewBuilder(ServiceCounters counters, TimeProvider time, RealmState? state = null, string? databasePath = null) =>
        new(
            state ?? RealmState.CreateInitial(OptionsBinding.Defaults, time),
            OptionsBinding.Defaults,
            counters,
            time,
            databasePath ?? "/never/there/realm.db",
            () => null,
            () => new Dictionary<string, int>(),
            "1.2.3");

    private string NewFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "realm-wiring-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        _folders.Add(folder);
        return folder;
    }

    private static ServiceProvider FactoryOver(string path) =>
        new ServiceCollection().AddPooledDbContextFactory<RealmDb>(builder => RealmDb.Configure(builder, path)).BuildServiceProvider();

    private HaRestClient NewRest(HttpMessageHandler handler, ServiceCounters counters)
    {
        var time = new ManualTimeProvider(Start);
        var options = new HaRestOptions { Token = "fake-supervisor-token-5678", RetryDelays = NoWaits };
        var http = new HttpClient(handler, disposeHandler: false) { BaseAddress = options.BaseAddress, Timeout = Timeout.InfiniteTimeSpan };
        _clients.Add(http);
        return new HaRestClient(http, options, time, new RecordingLogger<HaRestClient>(), counters);
    }

    private (AvatarService Service, FakeHaGateway Gateway, ManualTimeProvider Time) NewAvatars(ServiceCounters counters)
    {
        var discovery = new DiscoveryState();
        discovery.Publish(Plans.Discovery(members: Plans.Member("king", avatar: HaPicture)));
        var gateway = new FakeHaGateway();
        var client = new HttpClient(new ScriptedHttpHandler());
        _clients.Add(client);
        var time = new ManualTimeProvider(Start);
        var service = new AvatarService(discovery, gateway, client, NewFolder() + "/avatars", time, new RecordingLogger<AvatarService>(), counters);
        return (service, gateway, time);
    }

    // Throws what it is given instead of answering, as a connection that fails (or a request that was cancelled) does.
    private sealed class FailingHandler(Exception error) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(error);
    }
}
