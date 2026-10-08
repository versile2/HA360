using System.Globalization;
using System.Text.Json;
using Xunit;

namespace Realm.Domain.Tests;

// Expected values are the vectors and tables of 02 (section 1.3 units, section 1.6 rules, T21) and Python
// arithmetic (haversine offsets, 50 ft x 0.3048), never the code under test. HA's Life360 speed is mph (D39).
public class FixParserTests
{
    private const double HomeLat = 31.0990;
    private const double HomeLon = -85.3410;
    private const double MetresPerDegreeOfLatitude = 6_371_008.8 * Math.PI / 180;

    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-30T21:25:00-05:00", CultureInfo.InvariantCulture);

    private static DateTimeOffset At(string iso) => DateTimeOffset.Parse(iso, CultureInfo.InvariantCulture);

    // A point the given number of metres due north of the home point, for exact distances.
    private static double NorthOfHome(double metres) => HomeLat + (metres / MetresPerDegreeOfLatitude);

    private static HaEntitySnapshot Snapshot(string entityId, string state, DateTimeOffset? lastUpdated, params (string Key, object? Value)[] attributes)
    {
        var map = new Dictionary<string, JsonElement>();
        foreach (var (key, value) in attributes)
        {
            map[key] = JsonSerializer.SerializeToElement(value);
        }

        return new HaEntitySnapshot(entityId, state, map, lastUpdated, lastUpdated);
    }

    private static HaEntitySnapshot Life360(string state, params (string Key, object? Value)[] attributes) =>
        Snapshot("device_tracker.life360_alden", state, Now, attributes);

    private static HaEntitySnapshot Companion(DateTimeOffset lastUpdated, params (string Key, object? Value)[] attributes) =>
        Snapshot("device_tracker.alden_pixel", "home", lastUpdated, attributes);

    private static RawFix Previous(FixSource source, DateTimeOffset ts, double lat, double lon) =>
        new(EntityId: "device_tracker.previous", Source: source, Ts: ts, Lat: lat, Lon: lon);

    private static void AssertNear(double expected, double? actual, double tolerance)
    {
        Assert.NotNull(actual);
        Assert.InRange(actual.Value, expected - tolerance, expected + tolerance);
    }

    // ---- Life360 tracker ------------------------------------------------------------------------------------

    // T21: HA's Life360 speed is mph, converted once with x 0.44704 (D39). Dividing by 2.25 would give 35.556 for 80 mph.
    [Theory]
    [InlineData(80.0, 35.7632)]
    [InlineData(15.0, 6.7056)]
    [InlineData(60.0, 26.8224)]
    [InlineData(0.0, 0.0)]
    public void Life360_speed_is_mph_converted_to_metres_per_second(double mph, double expectedMps)
    {
        var fix = FixParser.ParseTracker(
            Life360("not_home", ("latitude", HomeLat), ("longitude", HomeLon), ("last_seen", "2026-09-30T21:24:00-05:00"), ("speed", mph)),
            FixSource.Life360,
            Now);

        Assert.NotNull(fix);
        Assert.Equal(expectedMps, fix.SpeedMps ?? double.NaN, 6);
    }

    // T21: an 80.0 mph fix is at the 80 mph speeding threshold of 35.7632 m/s; dividing by 2.25 would give 35.556 and miss it.
    [Fact]
    public void An_80_mph_fix_reaches_the_80_mph_speeding_threshold()
    {
        var fix = FixParser.ParseTracker(
            Life360("not_home", ("latitude", HomeLat), ("longitude", HomeLon), ("last_seen", "2026-09-30T21:24:00-05:00"), ("speed", 80.0)),
            FixSource.Life360,
            Now);

        Assert.NotNull(fix);
        Assert.True(fix.SpeedMps >= 35.7632);
        Assert.True(fix.SpeedMps > 35.556);
    }

    [Fact]
    public void Life360_speed_is_null_when_the_tracker_has_none()
    {
        var fix = FixParser.ParseTracker(
            Life360("home", ("latitude", HomeLat), ("longitude", HomeLon), ("last_seen", "2026-09-30T21:24:00-05:00"), ("speed", null)),
            FixSource.Life360,
            Now);

        Assert.NotNull(fix);
        Assert.Null(fix.SpeedMps);
    }

    // The timestamp is last_seen, not last_updated: after an HA restart last_updated is later and would fake freshness.
    [Fact]
    public void Life360_time_is_last_seen_not_last_updated()
    {
        var fix = FixParser.ParseTracker(
            Life360("home", ("latitude", HomeLat), ("longitude", HomeLon), ("last_seen", "2026-09-30T21:10:00-05:00")),
            FixSource.Life360,
            Now);

        Assert.NotNull(fix);
        Assert.Equal(At("2026-09-30T21:10:00-05:00"), fix.Ts);
        Assert.Equal(FixSource.Life360, fix.Source);
        Assert.Equal("device_tracker.life360_alden", fix.EntityId);
        Assert.Equal(HomeLat, fix.Lat);
        Assert.Equal(HomeLon, fix.Lon);
    }

    // 02 describes last_seen both as an ISO string with an offset (1.3) and as epoch seconds (1.6): both are read.
    [Fact]
    public void Life360_last_seen_may_be_epoch_seconds()
    {
        // 2026-09-30T21:10:00-05:00 is 2026-10-01T02:10:00Z.
        var epochSeconds = new DateTimeOffset(2026, 10, 1, 2, 10, 0, TimeSpan.Zero).ToUnixTimeSeconds();

        var fix = FixParser.ParseTracker(
            Life360("home", ("latitude", HomeLat), ("longitude", HomeLon), ("last_seen", epochSeconds)),
            FixSource.Life360,
            Now);

        Assert.NotNull(fix);
        Assert.Equal(At("2026-09-30T21:10:00-05:00"), fix.Ts);
    }

    [Fact]
    public void Life360_without_last_seen_is_not_a_fix()
    {
        var fix = FixParser.ParseTracker(Life360("home", ("latitude", HomeLat), ("longitude", HomeLon)), FixSource.Life360, Now);

        Assert.Null(fix);
    }

    // An unavailable or unknown tracker is a connection signal, not a fix.
    [Theory]
    [InlineData("unavailable")]
    [InlineData("unknown")]
    public void An_unavailable_tracker_is_not_a_fix(string state)
    {
        var fix = FixParser.ParseTracker(
            Life360(state, ("latitude", HomeLat), ("longitude", HomeLon), ("last_seen", "2026-09-30T21:24:00-05:00")),
            FixSource.Life360,
            Now);

        Assert.Null(fix);
    }

    // Attributes with the same last_seen are a battery or wifi change, not a new fix.
    [Fact]
    public void A_repeated_last_seen_is_not_a_new_fix()
    {
        var first = FixParser.ParseTracker(
            Life360("home", ("latitude", HomeLat), ("longitude", HomeLon), ("last_seen", "2026-09-30T21:10:00-05:00"), ("battery_level", 50)),
            FixSource.Life360,
            Now);
        Assert.NotNull(first);

        var batteryChange = FixParser.ParseTracker(
            Life360("home", ("latitude", HomeLat), ("longitude", HomeLon), ("last_seen", "2026-09-30T21:10:00-05:00"), ("battery_level", 49)),
            FixSource.Life360,
            Now,
            previous: first);
        var newer = FixParser.ParseTracker(
            Life360("home", ("latitude", HomeLat), ("longitude", HomeLon), ("last_seen", "2026-09-30T21:11:00-05:00"), ("battery_level", 49)),
            FixSource.Life360,
            Now,
            previous: first);

        Assert.Null(batteryChange);
        Assert.NotNull(newer);
        Assert.Equal(At("2026-09-30T21:11:00-05:00"), newer.Ts);
    }

    // Life360's accuracy is a constant 15.2 m (50 ft) placeholder: null. Anything else is a real reading.
    [Theory]
    [InlineData(15.2, null)]
    [InlineData(15.24, null)]
    [InlineData(15.25, 15.25)]
    [InlineData(15.0, 15.0)]
    [InlineData(8.0, 8.0)]
    [InlineData(120.0, 120.0)]
    public void Life360_accuracy_placeholder_is_null(double reported, double? expected)
    {
        var fix = FixParser.ParseTracker(
            Life360("home", ("latitude", HomeLat), ("longitude", HomeLon), ("last_seen", "2026-09-30T21:24:00-05:00"), ("gps_accuracy", reported)),
            FixSource.Life360,
            Now);

        Assert.NotNull(fix);
        Assert.Equal(expected, fix.AccuracyM);
    }

    // 50 ft is 15.24 m (feet x 0.3048): within 0.05 of 15.2, so still the placeholder.
    [Fact]
    public void Fifty_feet_in_metres_is_still_the_accuracy_placeholder()
    {
        var fiftyFeetInMetres = 50 * 0.3048;

        var fix = FixParser.ParseTracker(
            Life360("home", ("latitude", HomeLat), ("longitude", HomeLon), ("last_seen", "2026-09-30T21:24:00-05:00"), ("gps_accuracy", fiftyFeetInMetres)),
            FixSource.Life360,
            Now);

        Assert.NotNull(fix);
        Assert.Null(fix.AccuracyM);
    }

    [Fact]
    public void Life360_without_an_accuracy_has_none()
    {
        var fix = FixParser.ParseTracker(
            Life360("home", ("latitude", HomeLat), ("longitude", HomeLon), ("last_seen", "2026-09-30T21:24:00-05:00")),
            FixSource.Life360,
            Now);

        Assert.NotNull(fix);
        Assert.Null(fix.AccuracyM);
    }

    // The tracker attribute battery is read at the fix's own time; driving and address come from Life360 only.
    [Fact]
    public void Life360_battery_driving_and_address_are_read_from_the_attributes()
    {
        var fix = FixParser.ParseTracker(
            Life360(
                "not_home",
                ("latitude", HomeLat),
                ("longitude", HomeLon),
                ("last_seen", "2026-09-30T21:24:00-05:00"),
                ("battery_level", 19),
                ("battery_charging", true),
                ("driving", true),
                ("address", "Street Name, Alabama")),
            FixSource.Life360,
            Now);

        Assert.NotNull(fix);
        Assert.Equal(19, fix.BatteryPct);
        Assert.True(fix.Charging);
        Assert.Equal(At("2026-09-30T21:24:00-05:00"), fix.BatteryAsOfUtc);
        Assert.True(fix.Driving);
        Assert.Equal("Street Name, Alabama", fix.Address);
    }

    // Home Assistant leaves the battery unknown when Life360 reports a negative one; then there is no charging flag either.
    [Fact]
    public void An_unknown_battery_has_no_reading_and_no_charging_flag()
    {
        var fix = FixParser.ParseTracker(
            Life360("home", ("latitude", HomeLat), ("longitude", HomeLon), ("last_seen", "2026-09-30T21:24:00-05:00"), ("battery_level", null), ("battery_charging", true)),
            FixSource.Life360,
            Now);

        Assert.NotNull(fix);
        Assert.Null(fix.BatteryPct);
        Assert.Null(fix.Charging);
        Assert.Null(fix.BatteryAsOfUtc);
    }

    // ---- validity -------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(91.0, -85.0)]
    [InlineData(-90.5, -85.0)]
    [InlineData(31.0, 181.0)]
    [InlineData(31.0, -180.5)]
    public void A_position_outside_the_valid_range_is_not_a_fix(double lat, double lon)
    {
        var fix = FixParser.ParseTracker(
            Life360("home", ("latitude", lat), ("longitude", lon), ("last_seen", "2026-09-30T21:24:00-05:00")),
            FixSource.Life360,
            Now);

        Assert.Null(fix);
    }

    [Fact]
    public void A_state_without_a_position_is_not_a_fix()
    {
        var withoutLongitude = FixParser.ParseTracker(
            Life360("home", ("latitude", HomeLat), ("last_seen", "2026-09-30T21:24:00-05:00")),
            FixSource.Life360,
            Now);
        var withTextPosition = FixParser.ParseTracker(
            Life360("home", ("latitude", "31.0990"), ("longitude", "-85.3410"), ("last_seen", "2026-09-30T21:24:00-05:00")),
            FixSource.Life360,
            Now);

        Assert.Null(withoutLongitude);
        Assert.Null(withTextPosition);
    }

    // A timestamp in the future is clamped to now.
    [Fact]
    public void A_future_time_is_clamped_to_now()
    {
        var companion = FixParser.ParseTracker(
            Companion(Now.AddMinutes(5), ("latitude", HomeLat), ("longitude", HomeLon)),
            FixSource.Companion,
            Now);
        var life360 = FixParser.ParseTracker(
            Life360("home", ("latitude", HomeLat), ("longitude", HomeLon), ("last_seen", "2026-09-30T21:40:00-05:00")),
            FixSource.Life360,
            Now);

        Assert.NotNull(companion);
        Assert.NotNull(life360);
        Assert.Equal(Now, companion.Ts);
        Assert.Equal(Now, life360.Ts);
    }

    // ---- companion app --------------------------------------------------------------------------------------

    // Android: speed is already m/s and is kept as is; the time is the state's last update; accuracy is as reported.
    [Fact]
    public void Android_companion_fix_keeps_metres_per_second_and_uses_the_update_time()
    {
        var updated = At("2026-09-30T21:24:30-05:00");

        var fix = FixParser.ParseTracker(
            Companion(updated, ("latitude", HomeLat), ("longitude", HomeLon), ("gps_accuracy", 12), ("speed", 24.1), ("course", 248.0), ("altitude", 210.5)),
            FixSource.Companion,
            Now);

        Assert.NotNull(fix);
        Assert.Equal(FixSource.Companion, fix.Source);
        Assert.Equal(updated, fix.Ts);
        Assert.Equal(12.0, fix.AccuracyM);
        Assert.Equal(24.1, fix.SpeedMps);
        Assert.Equal(248.0, fix.HeadingDeg);
        Assert.Equal(210.5, fix.AltitudeM);
        Assert.Null(fix.Driving);
        Assert.Null(fix.Address);
    }

    // iOS: no speed or course, and the battery is the tracker attribute.
    [Fact]
    public void Ios_companion_fix_has_no_speed_and_reads_the_battery_attribute()
    {
        var updated = At("2026-09-30T21:24:30-05:00");

        var fix = FixParser.ParseTracker(
            Companion(updated, ("latitude", HomeLat), ("longitude", HomeLon), ("gps_accuracy", 35), ("battery_level", 10)),
            FixSource.Companion,
            Now);

        Assert.NotNull(fix);
        Assert.Null(fix.SpeedMps);
        Assert.Null(fix.HeadingDeg);
        Assert.Equal(10, fix.BatteryPct);
        Assert.Equal(updated, fix.BatteryAsOfUtc);
    }

    [Fact]
    public void A_companion_state_without_an_update_time_is_not_a_fix()
    {
        var snapshot = new HaEntitySnapshot(
            "device_tracker.alden_pixel",
            "home",
            new Dictionary<string, JsonElement>
            {
                ["latitude"] = JsonSerializer.SerializeToElement(HomeLat),
                ["longitude"] = JsonSerializer.SerializeToElement(HomeLon),
            },
            null,
            null);

        Assert.Null(FixParser.ParseTracker(snapshot, FixSource.Companion, Now));
    }

    // ---- rule F0: restart echo ------------------------------------------------------------------------------

    // On the first snapshot after a restart a companion state within 1 m of the last stored fix is an echo.
    [Theory]
    [InlineData(0.0, false)]
    [InlineData(0.9, false)]
    [InlineData(1.1, true)]
    [InlineData(25.0, true)]
    public void Rule_F0_a_first_snapshot_within_a_metre_of_the_stored_fix_is_an_echo(double metresFromStored, bool accepted)
    {
        var stored = Previous(FixSource.Companion, Now.AddHours(-3), HomeLat, HomeLon);

        var fix = FixParser.ParseTracker(
            Companion(Now.AddHours(-2), ("latitude", NorthOfHome(metresFromStored)), ("longitude", HomeLon)),
            FixSource.Companion,
            Now,
            previous: stored,
            firstSnapshot: true);

        Assert.Equal(accepted, fix is not null);
    }

    [Fact]
    public void Rule_F0_a_first_snapshot_with_nothing_stored_is_a_fix()
    {
        var fix = FixParser.ParseTracker(
            Companion(Now.AddHours(-2), ("latitude", HomeLat), ("longitude", HomeLon)),
            FixSource.Companion,
            Now,
            previous: null,
            firstSnapshot: true);

        Assert.NotNull(fix);
    }

    // The echo guard is only for the first snapshot; later, a state 0.9 m away and minutes later is a new fix.
    [Fact]
    public void Rule_F0_does_not_apply_after_the_first_snapshot()
    {
        var stored = Previous(FixSource.Companion, Now.AddMinutes(-10), HomeLat, HomeLon);

        var fix = FixParser.ParseTracker(
            Companion(Now.AddMinutes(-5), ("latitude", NorthOfHome(0.9)), ("longitude", HomeLon)),
            FixSource.Companion,
            Now,
            previous: stored,
            firstSnapshot: false);

        Assert.NotNull(fix);
    }

    // It is a companion rule: a Life360 fix within a metre of the stored one, with a newer last_seen, is still a fix.
    [Fact]
    public void Rule_F0_does_not_apply_to_life360()
    {
        var stored = Previous(FixSource.Life360, At("2026-09-30T20:00:00-05:00"), HomeLat, HomeLon);

        var fix = FixParser.ParseTracker(
            Life360("home", ("latitude", HomeLat), ("longitude", HomeLon), ("last_seen", "2026-09-30T20:30:00-05:00")),
            FixSource.Life360,
            Now,
            previous: stored,
            firstSnapshot: true);

        Assert.NotNull(fix);
    }

    // ---- duplicates ------------------------------------------------------------------------------

    // 02 section 1.6: a companion fix is the state's update time "when coordinates changed", so the same coordinates are never a new fix, however long
    // after the previous one (CR1-006).
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(60)]
    [InlineData(3600)]
    public void A_companion_state_with_unchanged_coordinates_is_never_a_new_fix(int secondsAfterPrevious)
    {
        var previous = Previous(FixSource.Companion, Now.AddHours(-2), HomeLat, HomeLon);

        var fix = FixParser.ParseTracker(
            Companion(previous.Ts.AddSeconds(secondsAfterPrevious), ("latitude", HomeLat), ("longitude", HomeLon)),
            FixSource.Companion,
            Now,
            previous: previous);

        Assert.Null(fix);
    }

    // The attribute-only update the review names: accuracy, altitude and battery changed, the position did not.
    [Fact]
    public void A_companion_attribute_only_update_is_not_a_new_fix_but_the_next_move_is()
    {
        var previous = Previous(FixSource.Companion, Now.AddMinutes(-30), HomeLat, HomeLon);

        var attributesOnly = FixParser.ParseTracker(
            Companion(Now.AddMinutes(-10), ("latitude", HomeLat), ("longitude", HomeLon), ("gps_accuracy", 8), ("altitude", 211.0), ("battery_level", 54)),
            FixSource.Companion,
            Now,
            previous: previous);
        var moved = FixParser.ParseTracker(
            Companion(Now.AddMinutes(-5), ("latitude", NorthOfHome(30)), ("longitude", HomeLon), ("gps_accuracy", 8)),
            FixSource.Companion,
            Now,
            previous: previous);

        Assert.Null(attributesOnly);
        Assert.NotNull(moved);
        Assert.Equal(Now.AddMinutes(-5), moved.Ts);
    }

    [Fact]
    public void A_different_position_within_two_seconds_is_not_a_duplicate()
    {
        var previous = Previous(FixSource.Companion, Now.AddMinutes(-5), HomeLat, HomeLon);

        var fix = FixParser.ParseTracker(
            Companion(previous.Ts.AddSeconds(1), ("latitude", NorthOfHome(10)), ("longitude", HomeLon)),
            FixSource.Companion,
            Now,
            previous: previous);

        Assert.NotNull(fix);
    }

    // ---- zones ----------------------------------------------------------------------------------------------

    // 1.2 step 7 and 1.9: the id is the entity id without "zone.", the name is trimmed, passive zones are kept.
    [Fact]
    public void A_zone_state_becomes_a_raw_place()
    {
        var zone = Snapshot(
            "zone.jester_hall",
            "1",
            Now,
            ("latitude", 31.1040),
            ("longitude", -85.3560),
            ("radius", 100.0),
            ("passive", true),
            ("friendly_name", "The Jester's Hall "));

        var place = FixParser.ParseZone(zone);

        Assert.NotNull(place);
        Assert.Equal("jester_hall", place.Id);
        Assert.Equal("The Jester's Hall", place.Name);
        Assert.Equal(31.1040, place.Lat);
        Assert.Equal(-85.3560, place.Lon);
        Assert.Equal(100.0, place.RadiusM);
        Assert.True(place.Passive);
    }

    [Fact]
    public void A_zone_is_active_unless_it_says_passive()
    {
        var zone = Snapshot("zone.home", "2", Now, ("latitude", HomeLat), ("longitude", HomeLon), ("radius", 100), ("friendly_name", "Hearth Haven"));

        var place = FixParser.ParseZone(zone);

        Assert.NotNull(place);
        Assert.False(place.Passive);
    }

    [Fact]
    public void Something_that_is_not_a_complete_zone_is_not_a_place()
    {
        var notAZone = Snapshot("person.alden", "home", Now, ("latitude", HomeLat), ("longitude", HomeLon), ("radius", 100));
        var withoutRadius = Snapshot("zone.home", "0", Now, ("latitude", HomeLat), ("longitude", HomeLon));

        Assert.Null(FixParser.ParseZone(notAZone));
        Assert.Null(FixParser.ParseZone(withoutRadius));
    }
}
