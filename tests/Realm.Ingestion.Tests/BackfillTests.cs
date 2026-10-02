using System.Globalization;
using Microsoft.Extensions.Logging;
using Realm.Domain;
using Realm.Infrastructure.Backfill;
using Realm.Infrastructure.Ha;
using Realm.Infrastructure.Options;
using Xunit;

namespace Realm.Ingestion.Tests;

/// <summary>
/// 02 section 8 and 03 section 2.4: the backfill fills the gap from Home Assistant's history, once per start. Trackers are asked for in 12 hour chunks with
/// attributes and the phone sensors in 48 hour chunks without; <c>backfill_days = 0</c> turns it all off; the rows go through the one parser and the one
/// detector (replay), every insert is idempotent, a stored trip is never rewritten, and rebuilding from the same rows gives the same trips. The database is real
/// (a temporary SQLite file), Home Assistant is a fake with a scripted history, and all time is a manual clock. Every id is fictional.
/// </summary>
public sealed class BackfillTests
{
    private const string PhonePrefix = "binary_sensor.king_phone_";

    private static readonly DateTimeOffset Tuesday = new(2026, 9, 29, 14, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Wednesday = new(2026, 9, 30, 14, 0, 0, TimeSpan.Zero);

    private static readonly string[] Trackers = [Plans.KingTracker, Plans.KingPhone];
    private static readonly string[] SensorIds = [PhonePrefix + "interactive", PhonePrefix + "locked", PhonePrefix + "android_auto"];
    private static readonly CompanionSensors PhoneSensors = new(null, null, SensorIds[0], SensorIds[1], SensorIds[2]);

    // ---- what is asked for ------------------------------------------------------------------------------------------

    [Fact]
    public async Task TheHistory_IsAskedForInTwelveHourChunksForTrackers_AndFortyEightHourChunksForSensors()
    {
        await using var rig = await StoreRig.StartAsync(OptionsBinding.Defaults with { BackfillDays = 3 });
        await rig.DiscoverAsync(KingWithPhone());
        var horizon = StoreRig.Start - TimeSpan.FromDays(3);

        await rig.NewBackfill().RunOnceAsync(CancellationToken.None);

        var calls = rig.Gateway.HistoryCalls;
        foreach (var tracker in Trackers)
        {
            var mine = calls.Where(c => c.EntityId == tracker).ToList();
            Assert.Equal(
                Enumerable.Range(0, 6).Select(i => (horizon + TimeSpan.FromHours(12 * i), horizon + TimeSpan.FromHours(12 * (i + 1)))),
                mine.Select(c => (c.Start, c.End)));
            Assert.All(mine, c => Assert.True(c.WithAttributes));   // positions need the attributes
        }

        foreach (var sensor in SensorIds)
        {
            var mine = calls.Where(c => c.EntityId == sensor).ToList();
            Assert.Equal(
                [(horizon, horizon + TimeSpan.FromHours(48)), (horizon + TimeSpan.FromHours(48), StoreRig.Start)],   // 72 hours: a full chunk and the rest
                mine.Select(c => (c.Start, c.End)));
            Assert.All(mine, c => Assert.False(c.WithAttributes));   // a sensor's state and change time are all that is wanted
        }
    }

    [Fact]
    public async Task WithoutAStoredFix_TheHistoryStartsAtTheHorizon_AndWithOneItStartsFiveMinutesBeforeIt()
    {
        await using var rig = await StoreRig.StartAsync();
        var drive = Drives.Drive(Tuesday);
        await rig.StoreFixesAsync("king", drive);
        await rig.StoreFixesAsync("queen", [Drives.Fix(Drives.Life360Row(Plans.QueenTracker, StoreRig.Start.AddDays(-30), 0, 0))]);   // older than backfill_days
        await rig.DiscoverAsync(King(), Plans.Member("queen", life360: Plans.QueenTracker), Plans.Member("jack", life360: "device_tracker.life360_jack"));

        await rig.NewBackfill().RunOnceAsync(CancellationToken.None);

        var calls = rig.Gateway.HistoryCalls;
        Assert.Equal(drive[^1].Ts - TimeSpan.FromMinutes(5), calls.First(c => c.EntityId == Plans.KingTracker).Start);   // the previous run's last fix, less the overlap
        Assert.Equal(StoreRig.Start.AddDays(-10), calls.First(c => c.EntityId == Plans.QueenTracker).Start);   // never further back than backfill_days
        Assert.Equal(StoreRig.Start.AddDays(-10), calls.First(c => c.EntityId == "device_tracker.life360_jack").Start);
        Assert.All(calls, c => Assert.True(c.End <= StoreRig.Start));   // up to the moment of hydration, where the live detector takes over
    }

    [Fact]
    public async Task BackfillDaysZero_AsksForNothing_AndTheServiceEndsAtOnce()
    {
        await using var rig = await StoreRig.StartAsync(OptionsBinding.Defaults with { BackfillDays = 0 });
        rig.History.Add(Drives.DriveRows(Tuesday));
        await rig.DiscoverAsync(King());
        var log = new RecordingLogger<BackfillService>();
        var backfill = rig.NewBackfill(log);

        await backfill.RunOnceAsync(CancellationToken.None);
        await backfill.StartAsync(CancellationToken.None);
        Assert.True(SpinWait.SpinUntil(() => backfill.ExecuteTask?.IsCompleted == true, TimeSpan.FromSeconds(10)), "The service did not end");
        await backfill.StopAsync(CancellationToken.None);

        Assert.Empty(rig.Gateway.HistoryCalls);
        Assert.Equal(0, rig.Count("fixes"));
        Assert.Equal(0, rig.Count("trips"));
        Assert.Equal(0, backfill.RowsBackfilled);
        Assert.False(backfill.Health.IsFaulted);
        Assert.Contains("off", Assert.Single(log.Messages(LogLevel.Information)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheHostedService_WaitsForTheFirstDiscovery_ThenFillsTheGapOnce()
    {
        await using var rig = await StoreRig.StartAsync();
        rig.History.Add(Drives.DriveRows(Tuesday));
        var backfill = rig.NewBackfill();

        await backfill.StartAsync(CancellationToken.None);
        await rig.Time.WaitForTimerAsync(TimeSpan.FromSeconds(1));   // it is polling for the members
        Assert.Empty(rig.Gateway.HistoryCalls);

        await rig.DiscoverAsync(King());
        rig.Time.Advance(TimeSpan.FromSeconds(1));
        Assert.True(SpinWait.SpinUntil(() => backfill.ExecuteTask?.IsCompleted == true, TimeSpan.FromSeconds(10)), "The service did not finish");
        await backfill.StopAsync(CancellationToken.None);

        Assert.NotEmpty(rig.Gateway.HistoryCalls);
        Assert.Equal(1, rig.Count("trips"));
        Assert.Equal(1, backfill.TripsRecovered);
        Assert.False(backfill.Health.IsFaulted);
    }

    [Fact]
    public async Task AMemberThePipelineHasNotHydrated_IsLeftAlone()
    {
        await using var rig = await StoreRig.StartAsync();
        rig.Discovery.Publish(Plans.Discovery(members: King()));   // published, but the pipeline has not applied it yet: nothing is known of what is stored

        await rig.NewBackfill().RunOnceAsync(CancellationToken.None);

        Assert.Empty(rig.Gateway.HistoryCalls);
    }

    // ---- what is stored ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task TheGap_IsFilled_WithTheFixesAndTheTripsThatWereMissed()
    {
        await using var rig = await StoreRig.StartAsync();
        rig.History.Add(Drives.DriveRows(Tuesday));
        rig.History.Add(Drives.DriveRows(Wednesday));
        await rig.DiscoverAsync(King());
        var version = rig.State.Current.StatsVersion;
        var backfill = rig.NewBackfill();

        await backfill.RunOnceAsync(CancellationToken.None);

        Assert.Equal(60, rig.Count("fixes"));   // 30 rows a drive
        Assert.Equal(60, backfill.RowsBackfilled);
        Assert.Equal(2, backfill.TripsRecovered);
        Assert.Equal(version + 2, rig.State.Current.StatsVersion);
        Assert.Equal(Tuesday.AddMinutes(-10).ToUnixTimeMilliseconds(), rig.Long("SELECT recording_start FROM members WHERE id = 'king'"));
        var trips = await rig.Queries.GetTripsAsync(Tuesday.AddDays(-1), Wednesday.AddDays(1), "king", CancellationToken.None);
        Assert.Equal([Wednesday, Tuesday], trips.Select(t => t.StartUtc));   // back-dated to each departure fix
        Assert.All(trips, t => Assert.Equal(TripQuality.Dense, t.Quality));
    }

    [Fact]
    public async Task ASecondRun_ChangesNothing()
    {
        await using var rig = await StoreRig.StartAsync();
        rig.History.Add(Drives.DriveRows(Tuesday));
        await rig.DiscoverAsync(King());
        var backfill = rig.NewBackfill();
        await backfill.RunOnceAsync(CancellationToken.None);
        var fixes = rig.Rows($"SELECT {StoreRig.FixColumns} FROM fixes ORDER BY ts");
        var trips = rig.Rows($"SELECT {StoreRig.TripColumns} FROM trips ORDER BY start_ts");
        var version = rig.State.Current.StatsVersion;

        await backfill.RunOnceAsync(CancellationToken.None);
        await rig.NewBackfill().RunOnceAsync(CancellationToken.None);   // a different instance over the same rows

        Assert.Equal(fixes, rig.Rows($"SELECT {StoreRig.FixColumns} FROM fixes ORDER BY ts"));
        Assert.Equal(trips, rig.Rows($"SELECT {StoreRig.TripColumns} FROM trips ORDER BY start_ts"));
        Assert.Equal(version, rig.State.Current.StatsVersion);   // nothing new was written, so no one is told to refetch
        Assert.Equal(1, backfill.TripsRecovered);
    }

    [Fact]
    public async Task ReplayingTheSameHistory_RebuildsIdenticalTrips_EveryTime()
    {
        await using var rig = await StoreRig.StartAsync();
        rig.History.Add(Drives.DriveRows(Tuesday));
        rig.History.Add(Drives.DriveRows(Wednesday));
        await rig.DiscoverAsync(King());
        var backfill = rig.NewBackfill();
        await backfill.RunOnceAsync(CancellationToken.None);
        var first = rig.Rows($"SELECT {StoreRig.TripColumns} FROM trips ORDER BY start_ts");
        Assert.Equal(2, first.Count);

        rig.Exec("DELETE FROM trips");   // the fixes stay: the second run replays what is stored together with what it fetches
        await backfill.RunOnceAsync(CancellationToken.None);
        var second = rig.Rows($"SELECT {StoreRig.TripColumns} FROM trips ORDER BY start_ts");

        rig.Exec("DELETE FROM trips");
        await backfill.RunOnceAsync(CancellationToken.None);
        var third = rig.Rows($"SELECT {StoreRig.TripColumns} FROM trips ORDER BY start_ts");

        Assert.Equal(first, second);
        Assert.Equal(first, third);
        Assert.Equal(6, backfill.TripsRecovered);
    }

    [Fact]
    public async Task ATripThatTheFilledRangeWouldCut_IsReplayedWhole_SoItIsNotStoredTwice()
    {
        // A 40 minute drive whose phone went silent: it closed at its last fix, which is also the newest stored fix, so the range to fill starts
        // five minutes before the end, in the middle of the drive. Replaying only the last half hour would find a second, shorter trip.
        await using var rig = await StoreRig.StartAsync();
        var rows = Drives.LongDriveRows(Tuesday, 40);
        await rig.StoreFixesAsync("king", rows.Select(Drives.Fix));
        await rig.DiscoverAsync(King());
        Assert.True(await rig.Stats.RecordTripAsync("king", Drives.LongDriveTrip(Tuesday, 40), CancellationToken.None));   // written by the run that stored the fixes
        rig.History.Add(rows);
        var backfill = rig.NewBackfill();

        await backfill.RunOnceAsync(CancellationToken.None);

        Assert.Equal(1, rig.Count("trips"));
        Assert.Equal(0, backfill.TripsRecovered);
    }

    [Fact]
    public async Task AStoredTrip_IsNeverRewritten_NotEvenWhenTheHistoryNowKnowsMore()
    {
        await using var rig = await StoreRig.StartAsync();
        rig.History.Add(Drives.DriveRows(Tuesday));
        await rig.DiscoverAsync(KingWithPhone());
        var backfill = rig.NewBackfill();
        await backfill.RunOnceAsync(CancellationToken.None);
        Assert.True(rig.Long("SELECT phone_count IS NULL FROM trips") == 1);   // the phone's screen was not known then

        rig.History.Add(PhoneRows(Tuesday));
        await backfill.RunOnceAsync(CancellationToken.None);

        Assert.Equal(1, rig.Count("trips"));
        Assert.Equal(1, rig.Long("SELECT phone_count IS NULL FROM trips"));   // v1 recomputes nothing (02 section 7.7)
        Assert.Equal(5, rig.Count("signals"));   // the signals are stored for the drives that come later
    }

    [Fact]
    public async Task RowsAfterTheMomentOfHydration_AreLeftToTheLiveFeed()
    {
        await using var rig = await StoreRig.StartAsync();
        await rig.DiscoverAsync(King());
        rig.Time.Advance(TimeSpan.FromSeconds(30));   // the backfill gets going a little after the member was hydrated
        var hydrated = StoreRig.Start;
        rig.Gateway.History = (_, _, end, _) => end == hydrated
            ? [Drives.Life360Row(Plans.KingTracker, hydrated.AddSeconds(-20), 0, 0), Drives.Life360Row(Plans.KingTracker, hydrated.AddSeconds(10), 50, 0)]
            : [];

        await rig.NewBackfill().RunOnceAsync(CancellationToken.None);

        Assert.Equal(hydrated.AddSeconds(-20).ToUnixTimeMilliseconds(), rig.Long("SELECT ts FROM fixes"));   // the later one is the live detector's
        Assert.Equal(1, rig.Count("fixes"));
    }

    [Fact]
    public async Task TheStateAtTheStartOfARequest_IsNotAFix()
    {
        // Home Assistant answers a history request with the state at its start first. Here that is a state from before backfill_days: the companion's
        // would be stamped with the start, Life360's carries an older last_seen; neither is part of the range, and neither may move recording_start back.
        await using var rig = await StoreRig.StartAsync();
        rig.History.Add([Drives.CompanionRow(Plans.KingPhone, StoreRig.Start.AddDays(-12), 0), Drives.Life360Row(Plans.KingTracker, StoreRig.Start.AddDays(-12), 0, 0)]);
        await rig.DiscoverAsync(KingWithPhone());
        var backfill = rig.NewBackfill();

        await backfill.RunOnceAsync(CancellationToken.None);

        Assert.Equal(0, rig.Count("fixes"));
        Assert.Equal(0, backfill.RowsBackfilled);
        Assert.Equal(0, rig.Count("members"));
    }

    [Fact]
    public async Task ACompanionFix_IsAPositionReport_NotAnAttributeUpdate()
    {
        await using var rig = await StoreRig.StartAsync();
        var first = StoreRig.Start.AddHours(-30);
        rig.History.Add(
        [
            Drives.CompanionRow(Plans.KingPhone, first, 0),
            Drives.CompanionRow(Plans.KingPhone, first.AddHours(1), 0),   // same coordinates: accuracy or battery changed, the phone did not move
            Drives.CompanionRow(Plans.KingPhone, first.AddHours(14), 800),   // in a later chunk, and a new position
            Drives.CompanionRow(Plans.KingPhone, first.AddHours(15), 800),
        ]);
        await rig.DiscoverAsync(KingWithPhone());

        await rig.NewBackfill().RunOnceAsync(CancellationToken.None);

        Assert.Equal(
            [Ms(first), Ms(first.AddHours(14))],
            rig.Rows("SELECT ts FROM fixes WHERE source = 'companion' ORDER BY ts"));
    }

    [Fact]
    public async Task AnEntityWhoseHistoryCannotBeFetched_IsSkipped_AndTheRestCarryOn()
    {
        await using var rig = await StoreRig.StartAsync();
        rig.History.Add(Drives.DriveRows(Tuesday));
        await rig.DiscoverAsync(KingWithPhone());
        rig.Gateway.History = (id, start, end, withAttributes) => id == Plans.KingPhone
            ? throw new HttpRequestException("Home Assistant answered 500 for " + Plans.KingPhone)
            : rig.History.Answer(id, start, end, withAttributes);
        var log = new RecordingLogger<BackfillService>();

        await rig.NewBackfill(log).RunOnceAsync(CancellationToken.None);

        Assert.Equal(1, rig.Count("trips"));   // the Life360 history was used
        Assert.Single(rig.Gateway.HistoryCalls, c => c.EntityId == Plans.KingPhone);   // the rest of that entity's chunks were not tried
        var warning = Assert.Single(log.Messages(LogLevel.Warning));
        Assert.DoesNotContain(Plans.KingPhone, warning, StringComparison.Ordinal);   // the type of the failure, never its message
        Assert.DoesNotContain("500", warning, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ThePhonesTransitions_AreStoredOnce_AndTheirUseWhileDrivingIsCounted()
    {
        await using var rig = await StoreRig.StartAsync();
        rig.History.Add(Drives.DriveRows(Tuesday));
        rig.History.Add(PhoneRows(Tuesday));
        await rig.DiscoverAsync(KingWithPhone());
        var backfill = rig.NewBackfill();

        await backfill.RunOnceAsync(CancellationToken.None);
        await backfill.RunOnceAsync(CancellationToken.None);

        Assert.Equal(5, rig.Count("signals"));   // screen off, on, off; locked off; Android Auto off (a repeated state is not a transition)
        Assert.Equal(3, rig.Long("SELECT count(*) FROM signals WHERE kind = 'screen'"));
        Assert.Equal(1, rig.Long("SELECT phone_count FROM trips"));
    }

    private static string Ms(DateTimeOffset at) => at.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);

    // ---- members and rows -------------------------------------------------------------------------------------------

    private static ResolvedMember King() => Plans.Member("king", life360: Plans.KingTracker);

    private static ResolvedMember KingWithPhone() =>
        Plans.Member("king", life360: Plans.KingTracker, companion: Plans.KingPhone, sensors: PhoneSensors, phoneCapable: true);

    // The screen was off before the drive, on from a minute into it for two and a half minutes; the phone was unlocked and not connected to the car.
    private static List<HaEntitySnapshot> PhoneRows(DateTimeOffset depart) =>
    [
        Drives.SensorRow(SensorIds[0], "off", depart.AddMinutes(-5)),
        Drives.SensorRow(SensorIds[0], "on", depart.AddSeconds(60)),
        Drives.SensorRow(SensorIds[0], "off", depart.AddSeconds(200)),
        Drives.SensorRow(SensorIds[1], "off", depart.AddMinutes(-5)),
        Drives.SensorRow(SensorIds[2], "off", depart.AddMinutes(-5)),
    ];
}
