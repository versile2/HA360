using Microsoft.Extensions.Logging;
using Realm.Infrastructure.Diagnostics;
using Realm.TestKit;
using Xunit;

namespace Realm.Ingestion.Tests;

/// <summary>
/// 03 sections 2.11 and 9.2: <see cref="ServiceCounters"/> are plain Interlocked counters, one <c>Record</c> or <c>Set</c> call per event, with nothing in them
/// but counts, depths, instants and flags. The per-minute rates read the instants they are given (no clock of their own), the counters that only grow never go
/// down, and the time zone self-check of 03 section 5.1 sets the flag of <c>zone_data_missing</c>. Time is manual; nothing sleeps.
/// </summary>
public sealed class ServiceCountersTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 2, 15, 0, 0, TimeSpan.Zero);

    // ---- the start -------------------------------------------------------------------------------------------------

    [Fact]
    public void ANewInstance_CountsNothing_AndRaisesNoFlag()
    {
        var counters = new ServiceCounters(new ManualTimeProvider(Start));

        Assert.Equal(Start, counters.StartedUtc);
        Assert.Equal(0, counters.WsConnects);
        Assert.Equal(0, counters.WsReconnects);
        Assert.Equal(0, counters.WsMessages);
        Assert.Null(counters.LastWsMessageUtc);
        Assert.Equal(0, counters.WatchedEntities);
        Assert.Equal(0, counters.RestCalls);
        Assert.Equal(0, counters.RestFailures);
        Assert.Equal(0, counters.IngestItems);
        Assert.Equal(0, counters.IngestSkipped);
        Assert.Equal(0, counters.IngestQueueDepth);
        Assert.Equal(0, counters.DbCommits);
        Assert.Equal(0, counters.DbRows);
        Assert.Null(counters.LastDbCommitUtc);
        Assert.Equal(0, counters.WriterQueueDepth);
        Assert.Equal(0, counters.WriterDropped);
        Assert.Equal(0, counters.BackfillRuns);
        Assert.Equal(0, counters.BackfillRows);
        Assert.Equal(0, counters.RetentionRuns);
        Assert.Equal(0, counters.RetentionRowsDeleted);
        Assert.Equal(0, counters.AvatarFetches);
        Assert.Equal(0, counters.AvatarFailures);
        Assert.Equal(0, counters.SchemaVersion);
        Assert.False(counters.UncleanShutdownAtStart);
        Assert.True(counters.ZoneDataOk);   // the self-check says otherwise; until it does nothing is missing
        Assert.False(counters.PayloadSchemaMismatch);
        Assert.Equal(0, counters.WsMessagesPerMinute(Start));
        Assert.Equal(0, counters.IngestEventsPerMinute(Start));
    }

    [Fact]
    public void TheClock_IsRequired()
    {
        Assert.Throws<ArgumentNullException>(() => new ServiceCounters(null!));
    }

    // ---- what each call records -------------------------------------------------------------------------------------

    [Fact]
    public void TheWebsocketCalls_CountConnectsReconnectsAndMessages_AndRememberTheLastMessage()
    {
        var counters = new ServiceCounters(new ManualTimeProvider(Start));

        counters.RecordWsConnected();
        counters.RecordWsConnected();
        counters.RecordWsReconnect();
        counters.RecordWsMessage(Start.AddSeconds(1));
        counters.RecordWsMessage(Start.AddSeconds(3));
        counters.SetWatchedEntities(42);

        Assert.Equal(2, counters.WsConnects);
        Assert.Equal(1, counters.WsReconnects);
        Assert.Equal(2, counters.WsMessages);
        Assert.Equal(Start.AddSeconds(3), counters.LastWsMessageUtc);
        Assert.Equal(42, counters.WatchedEntities);
    }

    [Fact]
    public void TheRestCalls_CountCallsAndFailuresApart()
    {
        var counters = new ServiceCounters(new ManualTimeProvider(Start));

        counters.RecordRestCall();
        counters.RecordRestCall();
        counters.RecordRestCall();
        counters.RecordRestFailure();

        Assert.Equal(3, counters.RestCalls);
        Assert.Equal(1, counters.RestFailures);
    }

    [Fact]
    public void TheIngestionCalls_CountItemsSkipsAndTheQueueDepth()
    {
        var counters = new ServiceCounters(new ManualTimeProvider(Start));

        counters.RecordIngestItem(Start);
        counters.RecordIngestItem(Start);
        counters.RecordIngestSkipped();
        counters.SetIngestQueueDepth(17);
        counters.SetIngestQueueDepth(4);

        Assert.Equal(2, counters.IngestItems);
        Assert.Equal(1, counters.IngestSkipped);
        Assert.Equal(4, counters.IngestQueueDepth);   // a depth is what the pipeline last saw, not a total
    }

    [Fact]
    public void TheWriterCalls_AddCommitsAndRows_AndKeepTheLatestCommit()
    {
        var counters = new ServiceCounters(new ManualTimeProvider(Start));

        counters.RecordDbCommit(100, Start.AddSeconds(2));
        counters.RecordDbCommit(7, Start.AddSeconds(4));

        Assert.Equal(2, counters.DbCommits);
        Assert.Equal(107, counters.DbRows);
        Assert.Equal(Start.AddSeconds(4), counters.LastDbCommitUtc);
    }

    [Fact]
    public void TheWriterQueue_FollowsTheDepth_ButTheDropCountNeverGoesDown()
    {
        var counters = new ServiceCounters(new ManualTimeProvider(Start));

        counters.SetWriterQueue(9_000, 12);
        counters.SetWriterQueue(0, 12);
        counters.SetWriterQueue(3, 5);   // a producer that read the queue earlier and got here late

        Assert.Equal(3, counters.WriterQueueDepth);
        Assert.Equal(12, counters.WriterDropped);
    }

    [Fact]
    public void TheJobs_CountFinishedRunsAndTheRowsTheyMoved()
    {
        var counters = new ServiceCounters(new ManualTimeProvider(Start));

        counters.RecordBackfillRun(60);
        counters.RecordBackfillRun(0);
        counters.RecordRetentionRun(5_200);

        Assert.Equal(2, counters.BackfillRuns);
        Assert.Equal(60, counters.BackfillRows);
        Assert.Equal(1, counters.RetentionRuns);
        Assert.Equal(5_200, counters.RetentionRowsDeleted);
    }

    [Fact]
    public void TheAvatarCalls_CountFetchesAndFailuresApart()
    {
        var counters = new ServiceCounters(new ManualTimeProvider(Start));

        counters.RecordAvatarFetch();
        counters.RecordAvatarFetch();
        counters.RecordAvatarFailure();

        Assert.Equal(2, counters.AvatarFetches);
        Assert.Equal(1, counters.AvatarFailures);
    }

    [Fact]
    public void TheStartFacts_AreTheSchemaVersionAndWhetherTheLastRunStoppedCleanly()
    {
        var counters = new ServiceCounters(new ManualTimeProvider(Start));

        counters.RecordStartup(1, uncleanShutdown: true);

        Assert.Equal(1, counters.SchemaVersion);
        Assert.True(counters.UncleanShutdownAtStart);

        counters.RecordStartup(2, uncleanShutdown: false);   // the schema step ran again (a quarantined file is replaced)

        Assert.Equal(2, counters.SchemaVersion);
        Assert.False(counters.UncleanShutdownAtStart);
    }

    [Fact]
    public void TheZoneDataFlag_FollowsTheSelfCheck_AndAPayloadSchemaMismatchOnceSeenStays()
    {
        var counters = new ServiceCounters(new ManualTimeProvider(Start));

        counters.RecordZoneData(ok: false);
        Assert.False(counters.ZoneDataOk);
        counters.RecordZoneData(ok: true);
        Assert.True(counters.ZoneDataOk);

        counters.MarkPayloadSchemaMismatch();
        counters.MarkPayloadSchemaMismatch();
        Assert.True(counters.PayloadSchemaMismatch);
    }

    // ---- thread safety ---------------------------------------------------------------------------------------------

    [Fact]
    public void ConcurrentCalls_LoseNothing()
    {
        var counters = new ServiceCounters(new ManualTimeProvider(Start));

        Parallel.For(
            0,
            8,
            _ =>
            {
                for (var i = 0; i < 10_000; i++)
                {
                    counters.RecordRestCall();
                    counters.RecordRestFailure();
                    counters.RecordWsMessage(Start);
                    counters.RecordIngestItem(Start);
                    counters.RecordDbCommit(2, Start);
                }
            });

        Assert.Equal(80_000, counters.RestCalls);
        Assert.Equal(80_000, counters.RestFailures);
        Assert.Equal(80_000, counters.WsMessages);
        Assert.Equal(80_000, counters.IngestItems);
        Assert.Equal(80_000, counters.DbCommits);
        Assert.Equal(160_000, counters.DbRows);
        Assert.Equal(80_000, counters.WsMessagesPerMinute(Start));   // the ring takes the same hits as the totals
        Assert.Equal(80_000, counters.IngestEventsPerMinute(Start));
    }

    [Fact]
    public void ConcurrentDropCounts_EndAtTheHighestOne()
    {
        var counters = new ServiceCounters(new ManualTimeProvider(Start));

        Parallel.For(1, 5_001, dropped => counters.SetWriterQueue(0, dropped));

        Assert.Equal(5_000, counters.WriterDropped);
    }

    // ---- the per-minute rates --------------------------------------------------------------------------------------

    [Fact]
    public void TheMessageRate_IsTheMessagesOfTheLastSixtySeconds()
    {
        var counters = new ServiceCounters(new ManualTimeProvider(Start));
        counters.RecordWsMessage(Start);
        counters.RecordWsMessage(Start.AddSeconds(30));
        counters.RecordWsMessage(Start.AddSeconds(30));
        counters.RecordWsMessage(Start.AddSeconds(59));

        Assert.Equal(4, counters.WsMessagesPerMinute(Start.AddSeconds(59)));   // the second of the first message is the oldest one still in
        Assert.Equal(3, counters.WsMessagesPerMinute(Start.AddSeconds(60)));   // and now it is out
        Assert.Equal(3, counters.WsMessagesPerMinute(Start.AddSeconds(89)));   // the two of the thirtieth second are still in
        Assert.Equal(1, counters.WsMessagesPerMinute(Start.AddSeconds(90)));   // and now they are out
        Assert.Equal(0, counters.WsMessagesPerMinute(Start.AddSeconds(119)));
        Assert.Equal(4, counters.WsMessages);   // the total does not age
    }

    [Fact]
    public void TheEventRate_IsTheItemsOfTheLastSixtySeconds_AndIsNotTheMessageRate()
    {
        var counters = new ServiceCounters(new ManualTimeProvider(Start));
        counters.RecordIngestItem(Start);
        counters.RecordIngestItem(Start.AddSeconds(10));
        counters.RecordWsMessage(Start.AddSeconds(10));

        Assert.Equal(2, counters.IngestEventsPerMinute(Start.AddSeconds(10)));
        Assert.Equal(1, counters.WsMessagesPerMinute(Start.AddSeconds(10)));
    }

    [Fact]
    public void ASecondThatComesRoundAgain_ReplacesTheOldMinuteInItsSlot()
    {
        var counters = new ServiceCounters(new ManualTimeProvider(Start));
        counters.RecordWsMessage(Start);
        counters.RecordWsMessage(Start);

        counters.RecordWsMessage(Start.AddSeconds(60));   // the same slot of the ring, a minute later

        Assert.Equal(1, counters.WsMessagesPerMinute(Start.AddSeconds(60)));
        Assert.Equal(3, counters.WsMessages);
    }

    [Fact]
    public void ARateRead_LongAfterTheLastMessage_IsZero_WhateverTheSlotsStillHold()
    {
        var counters = new ServiceCounters(new ManualTimeProvider(Start));
        for (var second = 0; second < 60; second++)
        {
            counters.RecordWsMessage(Start.AddSeconds(second));
        }

        Assert.Equal(60, counters.WsMessagesPerMinute(Start.AddSeconds(59)));
        Assert.Equal(0, counters.WsMessagesPerMinute(Start.AddHours(1)));
    }

    [Fact]
    public void AnInstantBeforeTheEpoch_IsCountedLikeAnyOther()
    {
        var counters = new ServiceCounters(new ManualTimeProvider(Start));
        var early = new DateTimeOffset(1969, 12, 31, 23, 59, 30, TimeSpan.Zero);

        counters.RecordWsMessage(early);

        Assert.Equal(1, counters.WsMessagesPerMinute(early.AddSeconds(5)));
    }

    // ---- the time zone self-check ----------------------------------------------------------------------------------

    [Fact]
    public void TheSelfCheck_WithZoneData_SetsNothingAndLogsNothing()
    {
        var counters = new ServiceCounters(new ManualTimeProvider(Start));
        var log = new RecordingLogger<ZoneDataSelfCheck>();
        var asked = new List<string>();

        var ok = new ZoneDataSelfCheck(counters, log, id => { asked.Add(id); return true; }).Check();

        Assert.True(ok);
        Assert.True(counters.ZoneDataOk);
        Assert.Equal([ZoneDataSelfCheck.ProbeZoneId], asked);
        Assert.Empty(log.Entries);
    }

    [Fact]
    public void TheSelfCheck_WithoutZoneData_RaisesTheFlag_AndLogsOneError()
    {
        var counters = new ServiceCounters(new ManualTimeProvider(Start));
        var log = new RecordingLogger<ZoneDataSelfCheck>();

        var ok = new ZoneDataSelfCheck(counters, log, _ => false).Check();

        Assert.False(ok);
        Assert.False(counters.ZoneDataOk);
        Assert.Single(log.Messages(LogLevel.Error));
        Assert.Single(log.Entries);
    }

    [Fact]
    public async Task TheSelfCheck_RunsOnceAtStart_AsAHostedService()
    {
        var counters = new ServiceCounters(new ManualTimeProvider(Start));
        var check = new ZoneDataSelfCheck(counters, new RecordingLogger<ZoneDataSelfCheck>(), _ => false);

        await check.StartAsync(CancellationToken.None);
        await check.StopAsync(CancellationToken.None);

        Assert.False(counters.ZoneDataOk);
    }

    [Theory]
    [InlineData("UTC", true)]
    [InlineData("Nowhere/Fictional", false)]
    [InlineData("", false)]
    public void AZoneId_ResolvesOrNot_WithoutEverThrowing(string zoneId, bool expected)
    {
        Assert.Equal(expected, ZoneDataSelfCheck.Resolves(zoneId));
    }
}
