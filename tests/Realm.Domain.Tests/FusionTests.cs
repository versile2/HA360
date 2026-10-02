using System.Globalization;
using Xunit;

namespace Realm.Domain.Tests;

// Expected values are the rules of 02 section 4.2 (candidates, the D22 discard, the winner and its tie order) and
// 4.3 (battery, speed, address), and the fixture table of 9.3 for the instant 2026-09-30T21:25:00-05:00. The fixes
// are hand-built RawFix values; ages are seconds before that instant.
public class FusionTests
{
    private const double HomeLat = 31.0990;
    private const double HomeLon = -85.3410;
    private const int OfflineAfterHours = 24;
    private const double MetresPerDegreeOfLatitude = 6_371_008.8 * Math.PI / 180;

    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-30T21:25:00-05:00", CultureInfo.InvariantCulture);

    private static double NorthOfHome(double metres) => HomeLat + (metres / MetresPerDegreeOfLatitude);

    private static RawFix Fix(
        FixSource source,
        int ageSeconds,
        double? accuracyM = null,
        double? speedMps = null,
        int? battery = null,
        bool? charging = null,
        int? batteryAgeSeconds = null,
        string? address = null,
        double lat = HomeLat,
        double lon = HomeLon) =>
        new(
            EntityId: $"device_tracker.{source}",
            Source: source,
            Ts: Now.AddSeconds(-ageSeconds),
            Lat: lat,
            Lon: lon,
            AccuracyM: accuracyM,
            SpeedMps: speedMps,
            BatteryPct: battery,
            Charging: charging,
            BatteryAsOfUtc: batteryAgeSeconds is { } batteryAge ? Now.AddSeconds(-batteryAge) : null,
            Address: address);

    private static FusedPosition Fused(params RawFix[] fixes)
    {
        var fused = Fuse.Position(fixes, Now, OfflineAfterHours);
        Assert.NotNull(fused);
        return fused;
    }

    // ---- freshest wins --------------------------------------------------------------------------------------

    [Fact]
    public void The_freshest_fix_wins_whatever_its_source_and_accuracy()
    {
        var companion = Fix(FixSource.Companion, 60, accuracyM: 10);
        var life360 = Fix(FixSource.Life360, 30);

        Assert.Equal(FixSource.Life360, Fused(companion, life360).WinnerSource);
        Assert.Equal(FixSource.Life360, Fused(life360, companion).WinnerSource);
        Assert.Equal(FixSource.Companion, Fused(Fix(FixSource.Companion, 30), Fix(FixSource.Life360, 60)).WinnerSource);
    }

    // No averaging and no smoothing: the pin stays at the winner's reported point.
    [Fact]
    public void The_output_is_the_winners_point_with_its_accuracy_and_time()
    {
        var older = Fix(FixSource.Life360, 120, lat: 31.0000, lon: -85.0000);
        var newer = Fix(FixSource.Companion, 20, accuracyM: 18, lat: 31.0002, lon: -85.0003);

        var fused = Fused(older, newer);

        Assert.Equal(31.0002, fused.Lat);
        Assert.Equal(-85.0003, fused.Lon);
        Assert.Equal(18.0, fused.AccuracyM);
        Assert.Equal(Now.AddSeconds(-20), fused.Ts);
        Assert.Equal(FixSource.Companion, fused.WinnerSource);
    }

    // The accuracy is the winner's own: a Life360 winner has none, so the halo never shows for it.
    [Fact]
    public void A_life360_winner_has_no_accuracy()
    {
        var fused = Fused(Fix(FixSource.Companion, 90, accuracyM: 12), Fix(FixSource.Life360, 30));

        Assert.Null(fused.AccuracyM);
    }

    // With one source the rule degenerates to the latest valid fix.
    [Fact]
    public void One_source_gives_its_fix()
    {
        var fused = Fused(Fix(FixSource.Life360, 600, lat: 31.5, lon: -85.5));

        Assert.Equal(31.5, fused.Lat);
        Assert.Equal(FixSource.Life360, fused.WinnerSource);
        Assert.Empty(fused.Alts);
    }

    [Fact]
    public void No_fix_gives_no_fused_position()
    {
        Assert.Null(Fuse.Position([], Now, OfflineAfterHours));
    }

    // ---- tie order ------------------------------------------------------------------------------------------

    // Fixes within 2 s of each other tie: the lower accuracy value wins (an unknown one ranks as 100 m), then
    // companion before life360. Beyond 2 s the fresher fix wins outright.
    [Theory]
    [InlineData(10, 12, 50.0, null, FixSource.Companion)]
    [InlineData(10, 12, 150.0, null, FixSource.Life360)]
    [InlineData(10, 12, null, null, FixSource.Companion)]
    [InlineData(12, 10, null, null, FixSource.Companion)]
    [InlineData(12, 10, 150.0, null, FixSource.Life360)]
    [InlineData(12, 10, 50.0, null, FixSource.Companion)]
    [InlineData(10, 10, 100.0, null, FixSource.Companion)]
    [InlineData(13, 10, 50.0, null, FixSource.Life360)]
    [InlineData(10, 13, 150.0, null, FixSource.Companion)]
    public void Within_two_seconds_the_accuracy_then_the_source_decides(int companionAge, int life360Age, double? companionAccuracy, double? life360Accuracy, FixSource expectedWinner)
    {
        var companion = Fix(FixSource.Companion, companionAge, accuracyM: companionAccuracy);
        var life360 = Fix(FixSource.Life360, life360Age, accuracyM: life360Accuracy);

        Assert.Equal(expectedWinner, Fused(companion, life360).WinnerSource);
        Assert.Equal(expectedWinner, Fused(life360, companion).WinnerSource);
    }

    // ---- D22: discard above 1 km ----------------------------------------------------------------------------

    // D22: a fix worse than 1 km is dropped when a strictly better fix under 5 minutes old exists, even if it is fresher.
    [Theory]
    [InlineData(1500.0, 240, FixSource.Life360)]
    [InlineData(1001.0, 240, FixSource.Life360)]
    [InlineData(1500.0, 299, FixSource.Life360)]
    [InlineData(1000.0, 240, FixSource.Companion)]
    [InlineData(1500.0, 300, FixSource.Companion)]
    [InlineData(1500.0, 360, FixSource.Companion)]
    [InlineData(500.0, 240, FixSource.Companion)]
    public void A_fix_worse_than_a_kilometre_is_discarded_when_a_better_recent_fix_exists(double companionAccuracy, int life360Age, FixSource expectedWinner)
    {
        // The companion fix is the fresher one (10 s); the Life360 fix has no accuracy, which ranks as 100 m.
        var companion = Fix(FixSource.Companion, 10, accuracyM: companionAccuracy);
        var life360 = Fix(FixSource.Life360, life360Age);

        Assert.Equal(expectedWinner, Fused(companion, life360).WinnerSource);
    }

    // A discarded fix is not an alternative either.
    [Fact]
    public void A_discarded_fix_is_not_in_the_alternatives()
    {
        var fused = Fused(Fix(FixSource.Companion, 10, accuracyM: 1500), Fix(FixSource.Life360, 240));

        Assert.Empty(fused.Alts);
    }

    // Only a strictly better fix discards: of two bad fixes the worse goes, the better stays and wins.
    [Fact]
    public void Of_two_fixes_worse_than_a_kilometre_only_the_worse_is_discarded()
    {
        var companion = Fix(FixSource.Companion, 100, accuracyM: 1500);
        var life360 = Fix(FixSource.Life360, 10, accuracyM: 2000);

        Assert.Equal(FixSource.Companion, Fused(companion, life360).WinnerSource);
    }

    // With nothing better to replace it, even a very poor fix is the position.
    [Fact]
    public void A_poor_fix_that_is_all_there_is_still_wins()
    {
        var fused = Fused(Fix(FixSource.Companion, 10, accuracyM: 3000));

        Assert.Equal(3000.0, fused.AccuracyM);
    }

    // ---- candidates older than the offline limit ------------------------------------------------------------

    // A fix older than ui_offline_after_hours stays a candidate only if nothing newer exists.
    [Fact]
    public void A_fix_older_than_the_offline_limit_is_dropped_when_something_newer_exists()
    {
        var old = Fix(FixSource.Life360, 30 * 3600, lat: 31.0);
        var older = Fix(FixSource.Companion, 26 * 3600, lat: 31.2);

        var fused = Fused(old, older);

        Assert.Equal(31.2, fused.Lat);
        Assert.Empty(fused.Alts);
    }

    // The member then shows as Offline with the last-known position.
    [Fact]
    public void A_single_fix_older_than_the_offline_limit_is_still_the_last_known_position()
    {
        var fused = Fused(Fix(FixSource.Life360, 30 * 3600, lat: 31.4));

        Assert.Equal(31.4, fused.Lat);
        Assert.Equal(Now.AddHours(-30), fused.Ts);
    }

    // A fix within the limit stays a candidate beside the winner: it is the alternative.
    [Fact]
    public void An_older_fix_within_the_limit_is_an_alternative()
    {
        var winner = Fix(FixSource.Companion, 100);
        var other = Fix(FixSource.Life360, 1200, lat: 31.2);

        var fused = Fused(winner, other);

        Assert.Equal(FixSource.Companion, fused.WinnerSource);
        Assert.Equal([other], fused.Alts);
    }

    // ---- 4.3 battery ----------------------------------------------------------------------------------------

    // The newest non-null battery among the sources wins, not the winner's own.
    [Fact]
    public void Battery_is_the_newest_reading_among_the_sources()
    {
        var companion = Fix(FixSource.Companion, 20, battery: 79, charging: false, batteryAgeSeconds: 1800);
        var life360 = Fix(FixSource.Life360, 30, battery: 80, charging: true, batteryAgeSeconds: 1200);

        var fused = Fused(companion, life360);

        Assert.Equal(80, fused.BatteryPct);
        Assert.True(fused.Charging);
        Assert.Equal(Now.AddSeconds(-1200), fused.BatteryAsOfUtc);
    }

    // A source without a battery reading does not hide an older one.
    [Fact]
    public void A_source_without_a_battery_reading_is_skipped()
    {
        var companion = Fix(FixSource.Companion, 10);
        var life360 = Fix(FixSource.Life360, 300, battery: 55, charging: false);

        var fused = Fused(companion, life360);

        Assert.Equal(FixSource.Companion, fused.WinnerSource);
        Assert.Equal(55, fused.BatteryPct);
        Assert.False(fused.Charging);
        Assert.Equal(Now.AddSeconds(-300), fused.BatteryAsOfUtc);
    }

    // A reading takes its own time: the fix time when it has no separate battery time.
    [Fact]
    public void Battery_time_is_the_fix_time_unless_the_reading_has_its_own()
    {
        var withOwnTime = Fused(Fix(FixSource.Companion, 10, battery: 62, batteryAgeSeconds: 400));
        var withoutOwnTime = Fused(Fix(FixSource.Companion, 10, battery: 62));

        Assert.Equal(Now.AddSeconds(-400), withOwnTime.BatteryAsOfUtc);
        Assert.Equal(Now.AddSeconds(-10), withoutOwnTime.BatteryAsOfUtc);
    }

    // Ties prefer the companion source, and charging comes from the same source as the percentage.
    [Fact]
    public void Equal_battery_times_prefer_the_companion_and_charging_comes_from_the_same_source()
    {
        var life360 = Fix(FixSource.Life360, 30, battery: 20, charging: false, batteryAgeSeconds: 600);
        var companion = Fix(FixSource.Companion, 40, battery: 19, charging: true, batteryAgeSeconds: 600);

        var fused = Fused(life360, companion);

        Assert.Equal(19, fused.BatteryPct);
        Assert.True(fused.Charging);
    }

    // Only readings no older than 60 minutes count (inclusive); otherwise the battery is unknown.
    [Theory]
    [InlineData(3000, 50)]
    [InlineData(3600, 50)]
    [InlineData(3601, null)]
    [InlineData(7200, null)]
    public void A_battery_reading_counts_up_to_60_minutes_old(int batteryAgeSeconds, int? expectedPercent)
    {
        bool? expectedCharging = expectedPercent is null ? null : true;
        DateTimeOffset? expectedAsOf = expectedPercent is null ? null : Now.AddSeconds(-batteryAgeSeconds);

        var fused = Fused(Fix(FixSource.Companion, 10, battery: 50, charging: true, batteryAgeSeconds: batteryAgeSeconds));

        Assert.Equal(expectedPercent, fused.BatteryPct);
        Assert.Equal(expectedCharging, fused.Charging);
        Assert.Equal(expectedAsOf, fused.BatteryAsOfUtc);
    }

    // A reading with an unknown charging flag keeps it unknown.
    [Fact]
    public void Charging_is_unknown_when_the_chosen_reading_does_not_say()
    {
        var fused = Fused(Fix(FixSource.Life360, 10, battery: 40));

        Assert.Equal(40, fused.BatteryPct);
        Assert.Null(fused.Charging);
    }

    // ---- 4.3 speed ------------------------------------------------------------------------------------------

    // The winner's reported speed; failing that, the newest reported speed of another source within 90 s.
    [Fact]
    public void Speed_is_the_winners_reported_speed()
    {
        var companion = Fix(FixSource.Companion, 60, speedMps: 24.1);
        var life360 = Fix(FixSource.Life360, 75, speedMps: 20.0);

        Assert.Equal(24.1, Fused(companion, life360).SpeedMps);
    }

    [Theory]
    [InlineData(61, 24.0)]
    [InlineData(90, 24.0)]
    [InlineData(91, null)]
    [InlineData(600, null)]
    public void Speed_falls_back_to_another_source_only_within_90_seconds(int otherAgeSeconds, double? expectedSpeed)
    {
        var winner = Fix(FixSource.Companion, 60);
        var other = Fix(FixSource.Life360, otherAgeSeconds, speedMps: 24.0);

        Assert.Equal(expectedSpeed, Fused(winner, other).SpeedMps);
    }

    // The 90 s limit applies to the winner's own reading too: an old fix's speed is not a current speed.
    [Theory]
    [InlineData(90, 12.5)]
    [InlineData(91, null)]
    public void A_reported_speed_older_than_90_seconds_is_not_shown(int winnerAgeSeconds, double? expectedSpeed)
    {
        Assert.Equal(expectedSpeed, Fused(Fix(FixSource.Companion, winnerAgeSeconds, speedMps: 12.5)).SpeedMps);
    }

    // No implied-speed fallback: two moving points with no reported speed give no speed (R-112).
    [Fact]
    public void Speed_is_never_implied_from_positions()
    {
        var fused = Fused(Fix(FixSource.Companion, 10, lat: NorthOfHome(1000)), Fix(FixSource.Life360, 50));

        Assert.Null(fused.SpeedMps);
    }

    // ---- 4.3 address ----------------------------------------------------------------------------------------

    // The newest Life360 address within 250 m of the output position and no older than 30 minutes, the age measured
    // from the winning fix and not from now (D54).
    [Fact]
    public void Address_comes_from_a_life360_fix_near_the_winner()
    {
        var companion = Fix(FixSource.Companion, 30, accuracyM: 12);
        var life360 = Fix(FixSource.Life360, 100, address: "Street Name, Texas", lat: NorthOfHome(100));

        Assert.Equal("Street Name, Texas", Fused(companion, life360).Address);
    }

    [Theory]
    [InlineData(249.0, "Street Name, Texas")]
    [InlineData(251.0, null)]
    public void Address_must_be_within_250_metres_of_the_output_position(double metresFromWinner, string? expectedAddress)
    {
        var companion = Fix(FixSource.Companion, 30, accuracyM: 12);
        var life360 = Fix(FixSource.Life360, 100, address: "Street Name, Texas", lat: NorthOfHome(metresFromWinner));

        Assert.Equal(expectedAddress, Fused(companion, life360).Address);
    }

    // The 30 minutes run from the winner's timestamp (inclusive): the Life360 fix is this many seconds older than it.
    [Theory]
    [InlineData(1500, "Street Name, Texas")]
    [InlineData(1800, "Street Name, Texas")]
    [InlineData(1801, null)]
    [InlineData(2520, null)]
    public void Address_may_be_up_to_30_minutes_older_than_the_winning_fix(int secondsOlderThanWinner, string? expectedAddress)
    {
        const int winnerAgeSeconds = 10;
        var companion = Fix(FixSource.Companion, winnerAgeSeconds, accuracyM: 12);
        var life360 = Fix(FixSource.Life360, winnerAgeSeconds + secondsOlderThanWinner, address: "Street Name, Texas");

        Assert.Equal(expectedAddress, Fused(companion, life360).Address);
    }

    // D54: a Life360 fix 31 minutes older than the winner is too old for its street, however fresh the winner is.
    [Fact]
    public void An_address_fix_31_minutes_older_than_the_winner_is_dropped()
    {
        var companion = Fix(FixSource.Companion, 5 * 60, accuracyM: 12);
        var life360 = Fix(FixSource.Life360, (5 + 31) * 60, address: "Street Name, Texas");

        Assert.Null(Fused(companion, life360).Address);
    }

    // D54: exactly 30 minutes older than the winner is kept, although the fix itself is 35 minutes before now.
    [Fact]
    public void An_address_fix_exactly_30_minutes_older_than_the_winner_is_kept()
    {
        var companion = Fix(FixSource.Companion, 5 * 60, accuracyM: 12);
        var life360 = Fix(FixSource.Life360, (5 + 30) * 60, address: "Street Name, Texas");

        Assert.Equal("Street Name, Texas", Fused(companion, life360).Address);
    }

    // D54: a stale member keeps the street of their last fix: a winner 60 minutes before now still carries its own address.
    [Fact]
    public void A_stale_winner_keeps_its_own_address()
    {
        var fused = Fused(Fix(FixSource.Life360, 60 * 60, address: "Street Name, Texas"));

        Assert.Equal(Now.AddMinutes(-60), fused.Ts);
        Assert.Equal("Street Name, Texas", fused.Address);
    }

    // A Life360 fix a second newer than the winner (a tie the winner takes on accuracy) is never too old, but the
    // 250 m rule still applies to it.
    [Theory]
    [InlineData(251.0)]
    [InlineData(1000.0)]
    public void An_address_fix_newer_than_the_winner_but_beyond_250_metres_is_ignored(double metresFromWinner)
    {
        var companion = Fix(FixSource.Companion, 10, accuracyM: 12);
        var life360 = Fix(FixSource.Life360, 9, address: "Street Name, Texas", lat: NorthOfHome(metresFromWinner));

        var fused = Fused(companion, life360);

        Assert.Equal(FixSource.Companion, fused.WinnerSource);
        Assert.Null(fused.Address);
    }

    // Only a Life360 fix carries an address, and an empty one is none.
    [Fact]
    public void Only_a_life360_address_counts()
    {
        var companion = Fix(FixSource.Companion, 10, address: "Not From Life360, TX");
        var life360 = Fix(FixSource.Life360, 20, address: "  ");

        Assert.Null(Fused(companion, life360).Address);
    }

    // A winner that is itself a Life360 fix with an address has that address (distance 0).
    [Fact]
    public void A_life360_winner_with_an_address_has_it()
    {
        var fused = Fused(Fix(FixSource.Life360, 20, address: "I-35"));

        Assert.Equal("I-35", fused.Address);
    }

    // ---- the fixture instant through the real fusion --------------------------------------------------------

    private static TimeSpan FixtureStaleAfter => FreshnessRules.StaleAfter(30, TimeSpan.FromMinutes(30));

    private static Freshness FreshnessOf(FusedPosition fused) =>
        FreshnessRules.ForMember(MemberKind.Live, Now, fused.Ts, FixtureStaleAfter, OfflineAfterHours);

    private static PlaceMembership PlaceOf(FusedPosition fused) =>
        PlaceResolver.Resolve(fused.Lat, fused.Lon, fused.AccuracyM, DemoZoneTable.Drawn, []);

    // 02 section 9.3, king: at home, 19%, charging, accuracy 18 m, fix age 0, and a second (older) Life360 source.
    [Fact]
    public void Fixture_king_is_at_home_charging_and_fresh()
    {
        var companion = Fix(FixSource.Companion, 0, accuracyM: 18, battery: 19, charging: true, lat: 31.0990, lon: -85.3410);
        var life360 = Fix(FixSource.Life360, 42, lat: 31.09905, lon: -85.34105, battery: 20, charging: false);

        var fused = Fused(companion, life360);

        Assert.Equal(31.0990, fused.Lat);
        Assert.Equal(-85.3410, fused.Lon);
        Assert.Equal(18.0, fused.AccuracyM);
        Assert.Equal(19, fused.BatteryPct);
        Assert.True(fused.Charging);
        Assert.Equal(TimeSpan.Zero, Now - fused.Ts);
        Assert.Equal("home", PlaceOf(fused).PlaceId);
        Assert.Equal(Freshness.Fresh, FreshnessOf(fused));
    }

    // Queen: driving 24.1 m/s on "I-35", 62%, accuracy 12 m, fix age 1 min; the street comes from her Life360 fix.
    [Fact]
    public void Fixture_queen_is_driving_on_the_interstate_and_fresh()
    {
        var companion = Fix(FixSource.Companion, 60, accuracyM: 12, speedMps: 24.1, battery: 62, charging: false, lat: 31.0560, lon: -85.4647);
        var life360 = Fix(FixSource.Life360, 75, address: "I-35", speedMps: 24.0, lat: 31.0561, lon: -85.4647);

        var fused = Fused(companion, life360);

        Assert.Equal(31.0560, fused.Lat);
        Assert.Equal(-85.4647, fused.Lon);
        Assert.Equal(12.0, fused.AccuracyM);
        Assert.Equal(24.1, fused.SpeedMps);
        Assert.Equal(62, fused.BatteryPct);
        Assert.Equal("I-35", AddressParser.Parse(fused.Address)?.Street);
        Assert.Equal(TimeSpan.FromMinutes(1), Now - fused.Ts);
        Assert.Null(PlaceOf(fused).PlaceId);
        Assert.Equal(Freshness.Fresh, FreshnessOf(fused));
    }

    // Jester: at the jester's hall, 12%, accuracy 22 m, fix age 3 min, address "48 Larkspur Lane, Millbrook, TX".
    [Fact]
    public void Fixture_jester_is_at_the_jesters_hall_and_fresh()
    {
        var companion = Fix(FixSource.Companion, 180, accuracyM: 22, battery: 12, charging: false, lat: 31.1040, lon: -85.3560);
        var life360 = Fix(FixSource.Life360, 200, address: "48 Larkspur Lane, Millbrook, TX", lat: 31.1040, lon: -85.3560);

        var fused = Fused(companion, life360);

        Assert.Equal(22.0, fused.AccuracyM);
        Assert.Equal(12, fused.BatteryPct);
        Assert.Null(fused.SpeedMps);
        Assert.Equal("48 Larkspur Lane, Millbrook, TX", fused.Address);
        Assert.Equal(TimeSpan.FromMinutes(3), Now - fused.Ts);
        Assert.Equal("jester_hall", PlaceOf(fused).PlaceId);
        Assert.Equal(Freshness.Fresh, FreshnessOf(fused));
    }

    // Cryptid: out, 10%, accuracy 35 m, fix age 42 min: Stale (42 is above the 40 minute threshold), in no zone. The street
    // of the fixture ("Eastgate Avenue") is kept: the Life360 address is 3 minutes older than the winning fix, and the
    // 30 minutes of the address rule run from the winner, not from now (D54, 02 section 4.3).
    [Fact]
    public void Fixture_cryptid_is_stale_and_out_and_keeps_the_street_of_its_last_fix()
    {
        var companion = Fix(FixSource.Companion, 42 * 60, accuracyM: 35, battery: 10, charging: false, lat: 31.3382, lon: -82.7291);
        var life360 = Fix(FixSource.Life360, 45 * 60, address: "Eastgate Avenue, Pinebrook, TX", lat: 31.3382, lon: -82.7291);

        var fused = Fused(companion, life360);

        Assert.Equal(35.0, fused.AccuracyM);
        Assert.Equal(10, fused.BatteryPct);
        Assert.Equal(TimeSpan.FromMinutes(42), Now - fused.Ts);
        Assert.Equal(Freshness.Stale, FreshnessOf(fused));
        Assert.Null(PlaceOf(fused).PlaceId);
        Assert.Equal("Eastgate Avenue, Pinebrook, TX", fused.Address);
        Assert.Equal("Eastgate Avenue", AddressParser.Parse(fused.Address)?.Street);
    }

    // 02 section 9.3: only two zones are occupied at the frozen instant (home and the jester's hall); the pickup, 7.78 m
    // from the king's point, is inside home with no accuracy (a vehicle's accuracy is unknown).
    [Fact]
    public void Fixture_places_occupied_are_home_and_the_jesters_hall()
    {
        var king = Fused(Fix(FixSource.Companion, 0, accuracyM: 18, lat: 31.0990, lon: -85.3410));
        var queen = Fused(Fix(FixSource.Companion, 60, accuracyM: 12, lat: 31.0560, lon: -85.4647));
        var jester = Fused(Fix(FixSource.Companion, 180, accuracyM: 22, lat: 31.1040, lon: -85.3560));
        var cryptid = Fused(Fix(FixSource.Companion, 42 * 60, accuracyM: 35, lat: 31.3382, lon: -82.7291));

        var occupied = new[] { king, queen, jester, cryptid }
            .Select(f => PlaceOf(f).PlaceId)
            .Append(PlaceResolver.Resolve(31.09907, -85.34100, null, DemoZoneTable.Drawn, []).PlaceId)
            .OfType<string>()
            .Distinct()
            .Order()
            .ToList();

        Assert.Equal(["home", "jester_hall"], occupied);
    }
}
