using Realm.Domain;
using Realm.Infrastructure.Ha;
using Realm.Infrastructure.Options;
using Xunit;

namespace Realm.Ingestion.Tests;

/// <summary>
/// 03 section 2.10 and 02 section 6: the statistics service over a real database. A closed trip is written with its phone-use count and counts the
/// statistics version up; the week report and the driver's week are the pure rules (<see cref="StatsRules"/>) over the trips that were persisted, memoized
/// per statistics version; a driver nobody recorded is unknown (null), not zero. Every id is fictional.
/// </summary>
public sealed class StatsServiceTests
{
    private const DayOfWeek Monday = DayOfWeek.Monday;
    private const string Sensors = "binary_sensor.king_phone_";

    // The week of the rig's clock (Friday 2026-10-02) starts on Monday 2026-09-28 in UTC.
    private static readonly DateTimeOffset Tuesday = new(2026, 9, 29, 14, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Wednesday = new(2026, 9, 30, 14, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset LastSaturday = new(2026, 9, 26, 14, 0, 0, TimeSpan.Zero);

    private static readonly CompanionSensors PhoneSensors = new(null, null, Sensors + "interactive", Sensors + "locked", Sensors + "android_auto");

    [Fact]
    public async Task ATripThatIsRecorded_IsWritten_CountsTheStatsVersionUp_AndIsAnnounced()
    {
        await using var rig = await StoreRig.StartAsync();
        await rig.DiscoverAsync(King());
        var trip = Drives.ClosedTrip(Tuesday);
        var versionBefore = rig.State.Current.StatsVersion;
        var announced = 0;
        rig.Notifier.Changed += () => Interlocked.Increment(ref announced);
        rig.Time.Advance(TimeSpan.FromSeconds(2));   // the discovery announced a change a moment ago; announcements are at most one a second

        var written = await rig.Stats.RecordTripAsync("king", trip, CancellationToken.None);

        Assert.True(written);
        Assert.Equal(1, rig.Count("trips"));
        Assert.Equal(trip.StartUtc.ToUnixTimeMilliseconds(), rig.Long("SELECT start_ts FROM trips"));
        Assert.Equal(versionBefore + 1, rig.State.Current.StatsVersion);
        Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref announced) >= 1, TimeSpan.FromSeconds(5)), "The change was not announced");
    }

    [Fact]
    public async Task ATripThatIsAlreadyStored_ChangesNothing_AndDoesNotBumpTheVersion()
    {
        await using var rig = await StoreRig.StartAsync();
        await rig.DiscoverAsync(King());
        var trip = Drives.ClosedTrip(Tuesday);
        Assert.True(await rig.Stats.RecordTripAsync("king", trip, CancellationToken.None));
        var version = rig.State.Current.StatsVersion;

        var again = await rig.Stats.RecordTripAsync("king", trip, CancellationToken.None);

        Assert.False(again);
        Assert.Equal(1, rig.Count("trips"));
        Assert.Equal(version, rig.State.Current.StatsVersion);
    }

    [Fact]
    public async Task TheStoredTrip_CarriesTheAlgorithmVersionAndTheDeriveHash_OfTheOptions()
    {
        await using var rig = await StoreRig.StartAsync();
        await rig.DiscoverAsync(King());
        await rig.Stats.RecordTripAsync("king", Drives.ClosedTrip(Tuesday), CancellationToken.None);

        Assert.Equal(1, rig.Long("SELECT algo_version FROM trips"));
        var hash = rig.Text("SELECT derive_hash FROM trips");
        Assert.Matches("^[0-9a-f]{16}$", hash);

        // The hash follows the options that influence the derived numbers (02 section 7.7) and nothing else.
        await using var other = await StoreRig.StartAsync(OptionsBinding.Defaults with { DrivingSpeedingMinSeconds = 99 });
        await other.DiscoverAsync(King());
        await other.Stats.RecordTripAsync("king", Drives.ClosedTrip(Tuesday), CancellationToken.None);
        Assert.NotEqual(hash, other.Text("SELECT derive_hash FROM trips"));
    }

    [Fact]
    public async Task TheWeekReport_IsBuiltFromThePersistedTrips()
    {
        await using var rig = await StoreRig.StartAsync();
        await rig.DiscoverAsync(King());
        await rig.StoreFixesAsync("king", [.. Drives.Drive(Tuesday), .. Drives.Drive(Wednesday)]);
        var tuesday = Drives.ClosedTrip(Tuesday);
        var wednesday = Drives.ClosedTrip(Wednesday);
        await rig.Stats.RecordTripAsync("king", tuesday, CancellationToken.None);
        await rig.Stats.RecordTripAsync("king", wednesday, CancellationToken.None);

        var report = await rig.Stats.GetWeekReportAsync(0, Monday, CancellationToken.None);

        var king = Assert.Single(report.Drivers);
        Assert.Equal(2, king.Drives);
        Assert.Equal(2, report.Totals.Drives);
        Assert.Equal(tuesday.DistanceGpsM + wednesday.DistanceGpsM, king.Meters!.Value, 3);
        Assert.Equal(WeekCoverage.Partial, report.Coverage);   // the Realm began recording on Tuesday
        Assert.True(report.IsCurrent);
        Assert.Equal(new DateTimeOffset(2026, 9, 28, 0, 0, 0, TimeSpan.Zero), report.Start);
        Assert.Equal(0, king.Events[EventKeys.Speeding]);   // measured: dense trips below the limit
    }

    [Fact]
    public async Task ADriversWeek_ListsTheirTripsNewestFirst_AndIsNullForSomeoneWhoDoesNotDrive()
    {
        await using var rig = await StoreRig.StartAsync();
        await rig.DiscoverAsync(King(), Plans.Member("queen", life360: Plans.QueenTracker, inDrivingReport: false));
        await rig.StoreFixesAsync("king", [.. Drives.Drive(Tuesday), .. Drives.Drive(Wednesday)]);
        await rig.Stats.RecordTripAsync("king", Drives.ClosedTrip(Tuesday), CancellationToken.None);
        await rig.Stats.RecordTripAsync("king", Drives.ClosedTrip(Wednesday), CancellationToken.None);

        var week = await rig.Stats.GetDriverWeekAsync("king", 0, Monday, CancellationToken.None);

        Assert.NotNull(week);
        Assert.Equal(2, week.Summary.Drives);
        Assert.Equal(new[] { Wednesday, Tuesday }, week.Trips.Select(t => t.StartUtc));
        Assert.Null(await rig.Stats.GetDriverWeekAsync("queen", 0, Monday, CancellationToken.None));   // not in the report
        Assert.Null(await rig.Stats.GetDriverWeekAsync("nobody", 0, Monday, CancellationToken.None));
    }

    [Fact]
    public async Task ADriverNobodyRecorded_IsUnknown_ButADriverWhoWasRecordedAndDidNotDrive_IsZero()
    {
        await using var rig = await StoreRig.StartAsync();
        await rig.DiscoverAsync(King(phone: true), Plans.Member("queen", life360: Plans.QueenTracker));
        await rig.StoreFixesAsync("king", [Drives.Fix(Drives.Life360Row(Plans.KingTracker, LastSaturday, 0, 0))]);   // recording began before the week; no trips

        var report = await rig.Stats.GetWeekReportAsync(0, Monday, CancellationToken.None);

        var king = report.Drivers.Single(d => d.MemberId == "king");
        Assert.True(king.Covered);
        Assert.Equal(0, king.Drives);
        Assert.Equal(0, king.Events[EventKeys.Speeding]);
        Assert.Equal(0, king.Events[EventKeys.Phone]);   // a phone-capable driver measured zero phone use

        var queen = report.Drivers.Single(d => d.MemberId == "queen");
        Assert.False(queen.Covered);
        Assert.Null(queen.Drives);   // nothing was recorded: unknown, never 0
        Assert.Null(queen.Meters);
        Assert.Null(queen.Events[EventKeys.Speeding]);
        Assert.Null(queen.Events[EventKeys.Phone]);
    }

    [Fact]
    public async Task AWeekBeforeTheRecordingBegan_IsUnknownToo()
    {
        await using var rig = await StoreRig.StartAsync();
        await rig.DiscoverAsync(King());
        await rig.StoreFixesAsync("king", Drives.Drive(Tuesday));

        var previous = await rig.Stats.GetWeekReportAsync(1, Monday, CancellationToken.None);

        Assert.Equal(WeekCoverage.NoRecord, previous.Coverage);
        var king = Assert.Single(previous.Drivers);
        Assert.Null(king.Drives);
        Assert.False(previous.IsCurrent);
    }

    [Fact]
    public async Task TheTripsOfAWeek_AreReadOncePerStatsVersion()
    {
        await using var rig = await StoreRig.StartAsync();
        await rig.DiscoverAsync(King());
        await rig.StoreFixesAsync("king", Drives.Drive(Tuesday));
        await rig.Stats.RecordTripAsync("king", Drives.ClosedTrip(Tuesday), CancellationToken.None);

        var first = await rig.Stats.GetWeekReportAsync(0, Monday, CancellationToken.None);
        var second = await rig.Stats.GetWeekReportAsync(0, Monday, CancellationToken.None);
        await rig.Stats.GetDriverWeekAsync("king", 0, Monday, CancellationToken.None);

        Assert.Equal(1, rig.Stats.TripReads);   // the second report and the driver's week came from the memo
        Assert.Equal(first.Totals, second.Totals);

        await rig.StoreFixesAsync("king", Drives.Drive(Wednesday));
        await rig.Stats.RecordTripAsync("king", Drives.ClosedTrip(Wednesday), CancellationToken.None);   // counts the version up: the memo is stale
        var third = await rig.Stats.GetWeekReportAsync(0, Monday, CancellationToken.None);

        Assert.Equal(2, rig.Stats.TripReads);
        Assert.Equal(2, third.Totals.Drives);

        await rig.Stats.GetWeekReportAsync(1, Monday, CancellationToken.None);   // another week is another memo entry
        Assert.Equal(3, rig.Stats.TripReads);
    }

    [Fact]
    public async Task APhoneThatWasInUseWhileDriving_IsCounted_FromTheSignalsOfTheTrip()
    {
        await using var rig = await StoreRig.StartAsync();
        await rig.DiscoverAsync(King(phone: true));
        rig.Writer.EnqueueSignal("king", new PhoneSignal(Tuesday.AddMinutes(-5), PhoneSignalKind.Screen, false));
        rig.Writer.EnqueueSignal("king", new PhoneSignal(Tuesday.AddSeconds(60), PhoneSignalKind.Screen, true));
        rig.Writer.EnqueueSignal("king", new PhoneSignal(Tuesday.AddSeconds(200), PhoneSignalKind.Screen, false));   // not flushed: the recorder commits the queue first

        await rig.Stats.RecordTripAsync("king", Drives.ClosedTrip(Tuesday), CancellationToken.None);

        Assert.Equal(1, rig.Long("SELECT phone_count FROM trips"));
        Assert.Equal(1, rig.Long("SELECT count(*) FROM trip_events WHERE kind = 'phone'"));
    }

    [Fact]
    public async Task TimeWithAndroidAutoConnected_IsNotPhoneUse()
    {
        await using var rig = await StoreRig.StartAsync();
        await rig.DiscoverAsync(King(phone: true));
        await rig.StoreSignalsAsync(
            "king",
            [
                new PhoneSignal(Tuesday.AddMinutes(-5), PhoneSignalKind.Screen, false),
                new PhoneSignal(Tuesday.AddMinutes(-4), PhoneSignalKind.AndroidAuto, true),
                new PhoneSignal(Tuesday.AddSeconds(60), PhoneSignalKind.Screen, true),
                new PhoneSignal(Tuesday.AddSeconds(200), PhoneSignalKind.Screen, false),
            ]);

        await rig.Stats.RecordTripAsync("king", Drives.ClosedTrip(Tuesday), CancellationToken.None);

        Assert.Equal(0, rig.Long("SELECT phone_count FROM trips"));   // known to be zero, not unknown
        Assert.Equal(0, rig.Count("trip_events"));
    }

    [Fact]
    public async Task PhoneUse_IsUnknown_WhenThePhoneCannotSayAndWhenItsScreenWasNotKnown()
    {
        await using var rig = await StoreRig.StartAsync();
        await rig.DiscoverAsync(King(phone: true), Plans.Member("queen", life360: Plans.QueenTracker, phoneCapable: false));

        // The king's phone can report, but nothing of its screen was ever stored; the queen has no phone sensors.
        await rig.Stats.RecordTripAsync("king", Drives.ClosedTrip(Tuesday), CancellationToken.None);
        await rig.Stats.RecordTripAsync("queen", Drives.ClosedTrip(Tuesday, Plans.QueenTracker), CancellationToken.None);

        Assert.Equal(2, rig.Count("trips"));
        Assert.Equal(2, rig.Long("SELECT count(*) FROM trips WHERE phone_count IS NULL"));
    }

    [Fact]
    public async Task TheTimeZone_IsHomeAssistants_AndUtcWhenItIsUnknown()
    {
        await using var rig = await StoreRig.StartAsync();
        Assert.Equal(TimeZoneInfo.Utc, rig.Stats.Zone);   // before the first discovery

        await rig.DiscoverInZoneAsync("America/Chicago", King());
        Assert.Equal("America/Chicago", rig.Stats.Zone.Id);

        await rig.DiscoverInZoneAsync("Nowhere/Land", King());
        Assert.Equal(TimeZoneInfo.Utc, rig.Stats.Zone);   // a zone this machine does not know reads as UTC rather than failing every page
    }

    [Fact]
    public async Task TheTimeZone_IsKeptInTheMetaTable_AsHomeAssistantReportsIt()
    {
        await using var rig = await StoreRig.StartAsync();

        await rig.DiscoverInZoneAsync("America/Chicago", King());
        await rig.Writer.FlushAsync();
        Assert.Equal("America/Chicago", rig.Text("SELECT value FROM meta WHERE key = 'ha_time_zone'"));

        await rig.DiscoverInZoneAsync("Europe/London", King());   // HA's zone changed: the one row is replaced
        await rig.Writer.FlushAsync();
        Assert.Equal("Europe/London", rig.Text("SELECT value FROM meta WHERE key = 'ha_time_zone'"));
        Assert.Equal(1, rig.Long("SELECT count(*) FROM meta WHERE key = 'ha_time_zone'"));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    public async Task OnlyTheLastFourWeeks_AreKept(int weekOffset)
    {
        await using var rig = await StoreRig.StartAsync();
        await rig.DiscoverAsync(King());

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => rig.Stats.GetWeekReportAsync(weekOffset, Monday, CancellationToken.None).AsTask());
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => rig.Stats.GetDriverWeekAsync("king", weekOffset, Monday, CancellationToken.None).AsTask());
    }

    private static ResolvedMember King(bool phone = false) =>
        Plans.Member("king", life360: Plans.KingTracker, companion: phone ? Plans.KingPhone : null, sensors: phone ? PhoneSensors : null, phoneCapable: phone);
}
