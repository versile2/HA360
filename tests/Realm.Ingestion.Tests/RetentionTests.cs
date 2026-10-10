using System.Globalization;
using Microsoft.Extensions.Logging;
using Realm.Domain;
using Realm.Infrastructure.Hosting;
using Realm.Infrastructure.Options;
using Realm.Infrastructure.Retention;
using Realm.TestKit;
using Xunit;

namespace Realm.Ingestion.Tests;

/// <summary>
/// 02 section 7.6: the prune job deletes the fixes, vehicle samples and signals older than <c>retention_fix_days</c> in batches of 5 000 rows with 200 ms between
/// them, gives the freed pages back, checkpoints the log once a week, and runs five minutes after start and then every day at 03:30 in HA's time zone. Trips,
/// members and meta are kept for ever, and <c>members.recording_start</c> is not moved. The database is real; time is manual. Every id is fictional.
/// </summary>
public sealed class RetentionTests
{
    // The rig's clock is Friday 2026-10-02 15:00 UTC: with 30 days of retention the cutoff is 2026-09-02 15:00 UTC.
    private static readonly DateTimeOffset Cutoff = new(2026, 9, 2, 15, 0, 0, TimeSpan.Zero);
    private static readonly RealmOptions ThirtyDays = OptionsBinding.Defaults with { RetentionFixDays = 30 };

    [Fact]
    public async Task OldRawRows_AreDeleted_AndEverythingElseIsKeptForEver()
    {
        await using var rig = await StoreRig.StartAsync(ThirtyDays);
        var old = new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);
        await rig.DiscoverAsync(Plans.Member("king", life360: Plans.KingTracker));
        await rig.StoreFixesAsync("king", [Fix(old), Fix(Cutoff.AddMilliseconds(-1)), Fix(Cutoff), Fix(Cutoff.AddDays(1)), Fix(Cutoff.AddDays(27))]);
        await rig.StoreSignalsAsync("king", [new PhoneSignal(old, PhoneSignalKind.Screen, true), new PhoneSignal(Cutoff.AddDays(1), PhoneSignalKind.Screen, false)]);
        await rig.Writer.FlushAsync();
        Assert.True(await rig.Stats.RecordTripAsync("king", Drives.ClosedTrip(old), CancellationToken.None));
        var members = rig.Rows("SELECT id, recording_start FROM members");
        var meta = rig.Rows("SELECT key, value FROM meta WHERE key <> 'clean_shutdown' ORDER BY key");
        var retention = rig.NewRetention();

        var deleted = await retention.PruneAsync(CancellationToken.None);

        Assert.Equal(2 + 1, deleted);   // two old fixes, one signal
        Assert.Equal(3, rig.Count("fixes"));   // the one exactly at the cutoff stays: only what is older goes
        Assert.Equal(1, rig.Count("signals"));
        Assert.Equal(1, rig.Count("trips"));   // trips are never pruned, however old
        Assert.Equal(members, rig.Rows("SELECT id, recording_start FROM members"));   // coverage refers to when the Realm began recording, not to what it kept
        Assert.Equal(meta, rig.Rows("SELECT key, value FROM meta WHERE key <> 'clean_shutdown' ORDER BY key"));
        Assert.Equal(deleted, retention.LastDeleted);
        Assert.Equal(StoreRig.Start, retention.LastRunUtc);
    }

    // 0.3.1, D125: a tracker's positions are rows of `fixes` like a person's, so the same job ages them out; its member row stays.
    [Fact]
    public async Task ATrackersFixes_AgeOutWithTheOthers()
    {
        await using var rig = await StoreRig.StartAsync(ThirtyDays);
        var old = new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);
        await rig.StoreFixesAsync("tracker_pickup", [Fix(old), Fix(Cutoff.AddDays(1))]);

        var deleted = await rig.NewRetention().PruneAsync(CancellationToken.None);

        Assert.Equal(1, deleted);
        Assert.Equal(1, rig.Count("fixes"));
        Assert.Equal(1, rig.Count("members"));
    }

    [Fact]
    public async Task ARetentionOfZeroDays_KeepsEverything()
    {
        await using var rig = await StoreRig.StartAsync(OptionsBinding.Defaults with { RetentionFixDays = 0 });
        await rig.DiscoverAsync(Plans.Member("king", life360: Plans.KingTracker));
        await rig.StoreFixesAsync("king", [Fix(new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero))]);

        var deleted = await rig.NewRetention().PruneAsync(CancellationToken.None);

        Assert.Equal(0, deleted);
        Assert.Equal(1, rig.Count("fixes"));
    }

    [Fact]
    public async Task TheDeletes_GoInBatchesOfFiveThousand_WithTwoHundredMillisecondsBetween()
    {
        await using var rig = await StoreRig.StartAsync(ThirtyDays);
        rig.Exec("INSERT INTO members(id, first_seen_utc, last_configured_utc) VALUES ('king', 0, 0)");
        rig.Exec(
            "WITH RECURSIVE c(i) AS (SELECT 1 UNION ALL SELECT i + 1 FROM c WHERE i < 5200) "
            + "INSERT INTO fixes(member_id, ts, source, lat, lon) SELECT 'king', i, 'life360', 33.0, -84.0 FROM c");
        var clock = new ManualTimeProvider(StoreRig.Start);
        var retention = rig.NewRetention(clock);

        var run = retention.PruneAsync(CancellationToken.None);
        await clock.WaitForTimerAsync(TimeSpan.FromMilliseconds(200));   // the first batch is done and the job is giving a flush its turn

        Assert.False(run.IsCompleted);
        Assert.Equal(200, rig.Count("fixes"));
        clock.Advance(TimeSpan.FromMilliseconds(200));
        Assert.Equal(5200, await run);
        Assert.Equal(0, rig.Count("fixes"));
    }

    [Fact]
    public async Task TheFreedPages_AreGivenBackToTheFile()
    {
        await using var rig = await StoreRig.StartAsync(ThirtyDays);
        rig.Exec("INSERT INTO members(id, first_seen_utc, last_configured_utc) VALUES ('king', 0, 0)");
        rig.Exec(
            "WITH RECURSIVE c(i) AS (SELECT 1 UNION ALL SELECT i + 1 FROM c WHERE i < 4000) "
            + "INSERT INTO fixes(member_id, ts, source, lat, lon, address) SELECT 'king', i, 'life360', 33.0, -84.0, 'A long enough address to fill a page or two, 1 Example Road' FROM c");
        var pages = rig.Long("PRAGMA page_count");

        await rig.NewRetention().PruneAsync(CancellationToken.None);

        Assert.Equal(0, rig.Long("PRAGMA freelist_count"));
        Assert.True(rig.Long("PRAGMA page_count") < pages, "The file did not shrink");
    }

    [Fact]
    public async Task TheLog_IsCheckpointedOnTheFirstRun_AndThenOnceAWeek()
    {
        await using var rig = await StoreRig.StartAsync(ThirtyDays);
        // SQLite deletes the -wal file when the last connection closes, and the pooled connections close whenever another rig in the process clears the pools.
        // This one is never pooled and never in a transaction, so the file (and the frames in it) stay put for the whole test, and a checkpoint is not held up.
        // It is declared after the rig, so it is disposed before it.
        await using var logHolder = rig.HoldLogOpen();
        await rig.DiscoverAsync(Plans.Member("king", life360: Plans.KingTracker));
        var clock = new ManualTimeProvider(StoreRig.Start);
        var retention = rig.NewRetention(clock);
        await rig.StoreFixesAsync("king", [Fix(StoreRig.Start.AddDays(-1))]);
        Assert.True(rig.LogBytes() > 0, "The write should be in the log");

        await retention.PruneAsync(CancellationToken.None);
        Assert.Equal(0, rig.LogBytes());   // wal_checkpoint(TRUNCATE)

        clock.Advance(TimeSpan.FromDays(1));
        await rig.StoreFixesAsync("king", [Fix(StoreRig.Start.AddDays(-2))]);
        await retention.PruneAsync(CancellationToken.None);
        Assert.True(rig.LogBytes() > 0, "A day later the log is left alone");

        clock.Advance(TimeSpan.FromDays(6));
        await retention.PruneAsync(CancellationToken.None);
        Assert.Equal(0, rig.LogBytes());   // a week after the last checkpoint
    }

    [Fact]
    public async Task TheService_RunsFiveMinutesAfterStart_ThenEveryDayAtHalfPastThreeInHomeAssistantsZone()
    {
        await using var rig = await StoreRig.StartAsync(ThirtyDays);
        await rig.DiscoverInZoneAsync("America/Chicago", Plans.Member("king", life360: Plans.KingTracker));
        var clock = new ManualTimeProvider(StoreRig.Start);
        var retention = rig.NewRetention(clock);
        await rig.StoreFixesAsync("king", [Fix(Cutoff.AddDays(-1)), Fix(Cutoff.AddDays(1))]);

        await retention.StartAsync(CancellationToken.None);
        await clock.WaitForTimerAsync(TimeSpan.FromMinutes(5));
        Assert.Null(retention.LastRunUtc);   // not before the five minutes are up
        clock.Advance(TimeSpan.FromMinutes(5));
        Assert.True(SpinWait.SpinUntil(() => retention.LastRunUtc is not null, TimeSpan.FromSeconds(10)), "The first run did not happen");

        Assert.Equal(1, retention.LastDeleted);
        Assert.Equal(1, rig.Count("fixes"));

        // 15:05 UTC on Friday: the next 03:30 in Chicago (CDT, UTC-5) is Saturday 08:30 UTC.
        var next = new DateTimeOffset(2026, 10, 3, 8, 30, 0, TimeSpan.Zero) - clock.GetUtcNow();
        await rig.StoreFixesAsync("king", [Fix(Cutoff.AddDays(-2))]);
        await clock.WaitForTimerAsync(next);
        clock.Advance(next);
        Assert.True(SpinWait.SpinUntil(() => retention.LastRunUtc == new DateTimeOffset(2026, 10, 3, 8, 30, 0, TimeSpan.Zero), TimeSpan.FromSeconds(10)), "The daily run did not happen");

        Assert.Equal(1, retention.LastDeleted);
        await retention.StopAsync(CancellationToken.None);
        Assert.False(retention.Health.IsFaulted);
    }

    [Theory]
    [InlineData("2026-10-02T12:00:00Z", 2, "2026-10-03T08:30:00Z")]   // after 03:30 today: tomorrow's (CDT, UTC-5)
    [InlineData("2026-10-02T08:00:00Z", 2, "2026-10-02T08:30:00Z")]   // 03:00 local: half an hour from now
    [InlineData("2026-10-02T08:30:00Z", 2, "2026-10-03T08:30:00Z")]   // exactly 03:30: the next one is a day on
    [InlineData("2026-03-07T12:00:00Z", 2, "2026-03-08T08:30:00Z")]   // the night the clocks go forward at 02:00: 03:30 exists, at the new offset
    [InlineData("2026-10-31T12:00:00Z", 2, "2026-11-01T09:30:00Z")]   // the night they go back: 03:30 is standard time (UTC-6)
    [InlineData("2026-03-07T12:00:00Z", 3, "2026-03-08T09:00:00Z")]   // a zone whose clocks go forward at 03:00: 03:30 does not exist, the job runs at 04:00
    public void TheNextDailyRun_IsHalfPastThreeLocal(string now, int dstStartHour, string expected)
    {
        var zone = Zone(dstStartHour);

        var next = RetentionService.NextDailyRun(DateTimeOffset.Parse(now, CultureInfo.InvariantCulture), zone);

        Assert.Equal(DateTimeOffset.Parse(expected, CultureInfo.InvariantCulture), next);
    }

    [Fact]
    public async Task AFailingRun_IsLogged_AndTheServiceTriesAgain()
    {
        await using var rig = await StoreRig.StartAsync(ThirtyDays);
        rig.Exec("DROP TABLE signals");   // one of the raw tables is gone, so the run cannot finish
        var clock = new ManualTimeProvider(StoreRig.Start);
        var log = new RecordingLogger<RetentionService>();
        var retention = rig.NewRetention(clock, log);

        await retention.StartAsync(CancellationToken.None);
        await clock.WaitForTimerAsync(TimeSpan.FromMinutes(5));
        clock.Advance(TimeSpan.FromMinutes(5));
        Assert.True(SpinWait.SpinUntil(() => log.Messages(LogLevel.Error).Count > 0, TimeSpan.FromSeconds(10)), "The failure was not logged");
        Assert.True(retention.Health.IsFaulted);
        Assert.Null(retention.LastRunUtc);

        await clock.WaitForTimerAsync(ResilientLoop.InitialBackoff);   // the back-off of the loop
        clock.Advance(ResilientLoop.InitialBackoff);
        await clock.WaitForTimerAsync(TimeSpan.FromMinutes(5));   // and the job is waiting for its next run
        await retention.StopAsync(CancellationToken.None);

        Assert.Single(log.Messages(LogLevel.Error));
    }

    // DST starts on the second Sunday of March at <dstStartHour>:00 (standard time UTC-6) and ends on the first Sunday of November at 02:00 daylight time.
    private static TimeZoneInfo Zone(int dstStartHour)
    {
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
            new DateTime(2020, 1, 1),
            new DateTime(2035, 12, 31),
            TimeSpan.FromHours(1),
            TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, dstStartHour, 0, 0), 3, 2, DayOfWeek.Sunday),
            TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0), 11, 1, DayOfWeek.Sunday));
        return TimeZoneInfo.CreateCustomTimeZone("Test/Retention", TimeSpan.FromHours(-6), "Test", "Test standard", "Test daylight", [rule]);
    }

    private static RawFix Fix(DateTimeOffset at) => Drives.Fix(Drives.Life360Row(Plans.KingTracker, at, 0, 0));
}
