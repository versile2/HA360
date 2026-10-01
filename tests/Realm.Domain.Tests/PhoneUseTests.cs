using Xunit;
using static Realm.Domain.Tests.TripFixtures;

namespace Realm.Domain.Tests;

// 02 section 5.9 (D36) and the vectors T18 and T20 of 5.10. Phone use is the screen in use (interactive on,
// not locked, Android Auto not connected) while a track segment has both ends at 4.5 m/s or more; intersections
// closer than 20 s merge; a merged span of 10 s or more is one event. The count is null, never 0, when it could
// not be measured. Every trip here is on the fictional road of TripFixtures, 20 m/s unless said otherwise.
public class PhoneUseTests
{
    private const double Day = 86_400;

    private static PhoneSignal Screen(double t, bool? on) => new(At(t), PhoneSignalKind.Screen, on);

    private static PhoneSignal Locked(double t, bool? on) => new(At(t), PhoneSignalKind.Locked, on);

    private static PhoneSignal AndroidAuto(double t, bool? on) => new(At(t), PhoneSignalKind.AndroidAuto, on);

    // A trip of 60 s with a track point every 6 s at 20 m/s.
    private static DetectedTrip MovingTrip(double endS = 60) => Trip(0, endS, Points(0, endS, 20));

    // T18: the screen is on at the start of the trip (a reading an hour before says so), off after 14 s, on again
    // after another 8 s, off 15 s later.
    private static List<PhoneSignal> T18Signals() => [Screen(-3600, true), Screen(14, false), Screen(22, true), Screen(37, false)];

    // ---- T18 and T20 ---------------------------------------------------------------------------------------------

    [Fact]
    public void T18_screen_on_14_s_off_8_s_on_15_s_while_moving_is_one_event()
    {
        var result = PhoneUseDetector.Detect(MovingTrip(), T18Signals(), phoneCapable: true);

        Assert.Equal(1, result.PhoneCount);
        var phoneEvent = Assert.Single(result.Events);
        Assert.Equal(At(0), phoneEvent.StartUtc);      // a screen already on at the start counts from the start
        Assert.Equal(At(37), phoneEvent.EndUtc);
        Assert.Equal(29.0, phoneEvent.Seconds);
    }

    [Fact]
    public void T20_android_auto_on_for_the_whole_trip_is_a_measured_zero()
    {
        var signals = T18Signals();
        signals.Add(AndroidAuto(-60, true));

        var result = PhoneUseDetector.Detect(MovingTrip(), signals, phoneCapable: true);

        Assert.Equal(0, result.PhoneCount);      // 0, not null: the sensors exist and the screen state is known
        Assert.Empty(result.Events);
    }

    [Fact]
    public void T20_android_auto_on_for_the_first_20_s_only_leaves_the_later_screen_on_span()
    {
        var signals = T18Signals();
        signals.Add(AndroidAuto(-60, true));
        signals.Add(AndroidAuto(20, false));

        var result = PhoneUseDetector.Detect(MovingTrip(), signals, phoneCapable: true);

        Assert.Equal(1, result.PhoneCount);
        var phoneEvent = Assert.Single(result.Events);
        Assert.Equal(At(22), phoneEvent.StartUtc);     // the 0 to 14 s span was excluded
        Assert.Equal(At(37), phoneEvent.EndUtc);
        Assert.Equal(15.0, phoneEvent.Seconds);
    }

    [Theory]
    [InlineData(false)]   // no Android Auto sensor at all
    [InlineData(true)]    // a sensor whose state is unknown or unavailable
    public void An_absent_or_unknown_android_auto_sensor_excludes_nothing(bool sensorPresentButUnknown)
    {
        var signals = T18Signals();
        if (sensorPresentButUnknown)
        {
            signals.Add(AndroidAuto(-60, null));
            signals.Add(AndroidAuto(5, null));
        }

        var result = PhoneUseDetector.Detect(MovingTrip(), signals, phoneCapable: true);

        Assert.Equal(1, result.PhoneCount);
        Assert.Equal(29.0, Assert.Single(result.Events).Seconds);
    }

    [Fact]
    public void Android_auto_turning_on_part_way_excludes_only_the_time_after_it()
    {
        var signals = new List<PhoneSignal> { Screen(-60, true), AndroidAuto(-60, false), AndroidAuto(25, true) };

        var result = PhoneUseDetector.Detect(MovingTrip(), signals, phoneCapable: true);

        var phoneEvent = Assert.Single(result.Events);
        Assert.Equal(At(0), phoneEvent.StartUtc);
        Assert.Equal(At(25), phoneEvent.EndUtc);
    }

    // ---- the debounce: 20 s to merge, 10 s to count ---------------------------------------------------------------

    [Theory]
    [InlineData(19, 1)]   // separated by less than 20 s: one event
    [InlineData(20, 2)]   // 20 s or more: two events
    public void Spans_less_than_20_s_apart_merge_into_one_event(double offS, int expectedEvents)
    {
        var signals = new List<PhoneSignal> { Screen(-60, false), Screen(5, true), Screen(20, false), Screen(20 + offS, true), Screen(35 + offS, false) };

        var result = PhoneUseDetector.Detect(MovingTrip(endS: 120), signals, phoneCapable: true);

        Assert.Equal(expectedEvents, result.PhoneCount);
        Assert.Equal(expectedEvents, result.Events.Count);
    }

    [Theory]
    [InlineData(9, 0)]
    [InlineData(10, 1)]
    public void A_span_counts_from_10_s(double spanS, int expectedEvents)
    {
        var signals = new List<PhoneSignal> { Screen(-60, false), Screen(10, true), Screen(10 + spanS, false) };

        var result = PhoneUseDetector.Detect(MovingTrip(), signals, phoneCapable: true);

        Assert.Equal(expectedEvents, result.PhoneCount);
    }

    [Fact]
    public void The_10_s_minimum_is_the_screen_time_in_a_merged_span_not_the_time_between_its_ends()
    {
        // 5 s on, 8 s off, 5 s on: 10 s of use in a 18 s span, one event. 4 + 8 + 4 is 8 s of use: none.
        var enough = new List<PhoneSignal> { Screen(-60, false), Screen(10, true), Screen(15, false), Screen(23, true), Screen(28, false) };
        var tooLittle = new List<PhoneSignal> { Screen(-60, false), Screen(10, true), Screen(14, false), Screen(22, true), Screen(26, false) };

        var one = PhoneUseDetector.Detect(MovingTrip(), enough, phoneCapable: true);
        var none = PhoneUseDetector.Detect(MovingTrip(), tooLittle, phoneCapable: true);

        Assert.Equal(1, one.PhoneCount);
        Assert.Equal(10.0, Assert.Single(one.Events).Seconds);
        Assert.Equal(0, none.PhoneCount);
    }

    // ---- moving intervals --------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(4.4, 2)]   // a segment is moving only if both ends are at least 4.5 m/s: the slow stretch splits the use
    [InlineData(4.5, 1)]
    public void A_segment_is_moving_when_both_ends_do_4_5_m_s(double slowMps, int expectedEvents)
    {
        // Points at 24, 30 and 36 s are slow; the screen is on for the whole trip.
        var track = Points(0, 60, 20).Select(p => p.Ts == At(24) || p.Ts == At(30) || p.Ts == At(36) ? p with { SpeedMps = slowMps } : p).ToList();
        var trip = Trip(0, 60, track);

        var result = PhoneUseDetector.Detect(trip, [Screen(-60, true)], phoneCapable: true);

        Assert.Equal(expectedEvents, result.PhoneCount);
    }

    [Fact]
    public void Screen_time_while_the_car_is_stopped_is_not_phone_use()
    {
        // The track is at 20 m/s up to 18 s, then stopped from 24 s to 42 s, then moving again.
        var track = Points(0, 60, 20).Select(p => p.Ts >= At(24) && p.Ts <= At(36) ? p with { SpeedMps = 0 } : p).ToList();

        var screenOnWhileStopped = PhoneUseDetector.Detect(Trip(0, 60, track), [Screen(-60, false), Screen(20, true), Screen(40, false)], phoneCapable: true);

        // The screen is on from 20 s to 40 s, but every segment in that span has a stopped end (18 to 24 s, 36 to 42 s too).
        Assert.Equal(0, screenOnWhileStopped.PhoneCount);
    }

    [Fact]
    public void A_point_without_a_speed_is_not_moving()
    {
        var track = Points(0, 60, 20).Select(p => p with { SpeedMps = null }).ToList();

        var result = PhoneUseDetector.Detect(Trip(0, 60, track), [Screen(-60, true)], phoneCapable: true);

        Assert.Equal(0, result.PhoneCount);
    }

    [Theory]
    [InlineData(120, 1)]
    [InlineData(121, 0)]
    public void A_segment_longer_than_120_s_is_not_trusted_to_be_moving(double segmentS, int expectedEvents)
    {
        var track = new List<TrackPoint>(Points(0, 0, 20)) { Points(segmentS, segmentS, 20)[0] };

        var result = PhoneUseDetector.Detect(Trip(0, segmentS, track), [Screen(-60, true)], phoneCapable: true);

        Assert.Equal(expectedEvents, result.PhoneCount);
    }

    // ---- the screen and lock states --------------------------------------------------------------------------------------

    [Fact]
    public void A_screen_that_is_off_at_the_start_counts_from_when_it_turns_on()
    {
        var result = PhoneUseDetector.Detect(MovingTrip(), [Screen(-60, false), Screen(30, true)], phoneCapable: true);

        var phoneEvent = Assert.Single(result.Events);
        Assert.Equal(At(30), phoneEvent.StartUtc);
        Assert.Equal(At(60), phoneEvent.EndUtc);
    }

    [Theory]
    [InlineData(true, 0)]    // locked: the screen shows the lock screen, not use
    [InlineData(false, 1)]
    [InlineData(null, 1)]    // unknown excludes nothing
    public void A_locked_phone_is_not_in_use_and_an_unknown_lock_state_excludes_nothing(bool? locked, int expectedEvents)
    {
        var result = PhoneUseDetector.Detect(MovingTrip(), [Screen(-60, true), Locked(-60, locked)], phoneCapable: true);

        Assert.Equal(expectedEvents, result.PhoneCount);
    }

    [Fact]
    public void Unlocking_part_way_starts_the_use_then()
    {
        var result = PhoneUseDetector.Detect(MovingTrip(), [Screen(-60, true), Locked(-60, true), Locked(30, false)], phoneCapable: true);

        var phoneEvent = Assert.Single(result.Events);
        Assert.Equal(At(30), phoneEvent.StartUtc);
        Assert.Equal(At(60), phoneEvent.EndUtc);
    }

    [Fact]
    public void Signals_after_the_end_of_the_trip_and_in_any_order_change_nothing()
    {
        List<PhoneSignal> signals = [Screen(500, false), Screen(37, false), Screen(-3600, true), Screen(22, true), Screen(14, false), Screen(900, null)];

        var result = PhoneUseDetector.Detect(MovingTrip(), signals, phoneCapable: true);

        Assert.Equal(1, result.PhoneCount);
        Assert.Equal(At(37), Assert.Single(result.Events).EndUtc);
    }

    // ---- null, not 0 ---------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_member_who_is_not_phone_capable_has_no_count()
    {
        var result = PhoneUseDetector.Detect(MovingTrip(), T18Signals(), phoneCapable: false);

        Assert.Null(result.PhoneCount);
        Assert.Empty(result.Events);
    }

    [Fact]
    public void A_coarse_trip_has_no_count()
    {
        var coarse = Trip(0, 600, Points(0, 600, 20, step: 300), TripQuality.Coarse);

        var result = PhoneUseDetector.Detect(coarse, T18Signals(), phoneCapable: true);

        Assert.Null(result.PhoneCount);
    }

    [Theory]
    [InlineData("none")]                // no reading at all
    [InlineData("after the start")]     // the first reading comes after the trip started
    [InlineData("8 days before")]       // older than the 7 days of 5.9
    [InlineData("unavailable before")]  // the last reading before the trip is unavailable
    public void An_unknown_screen_state_before_the_trip_gives_no_count(string situation)
    {
        List<PhoneSignal> signals = situation switch
        {
            "none" => [],
            "after the start" => [Screen(10, true)],
            "8 days before" => [Screen(-8 * Day, true)],
            _ => [Screen(-3600, true), Screen(-60, null)],
        };

        var result = PhoneUseDetector.Detect(MovingTrip(), signals, phoneCapable: true);

        Assert.Null(result.PhoneCount);
        Assert.Empty(result.Events);
    }

    [Fact]
    public void A_reading_6_days_before_still_tells_the_state_at_the_start()
    {
        var result = PhoneUseDetector.Detect(MovingTrip(), [Screen(-6 * Day, true)], phoneCapable: true);

        Assert.Equal(1, result.PhoneCount);
    }

    [Fact]
    public void A_sensor_that_was_unavailable_for_part_of_the_trip_gives_no_count()
    {
        var result = PhoneUseDetector.Detect(MovingTrip(), [Screen(-3600, true), Screen(30, null), Screen(40, true)], phoneCapable: true);

        Assert.Null(result.PhoneCount);
        Assert.Empty(result.Events);
    }

    [Fact]
    public void A_screen_that_stays_off_all_trip_is_a_measured_zero()
    {
        var result = PhoneUseDetector.Detect(MovingTrip(), [Screen(-3600, false)], phoneCapable: true);

        Assert.Equal(0, result.PhoneCount);
        Assert.Empty(result.Events);
    }

    [Fact]
    public void An_outage_after_the_trip_does_not_matter()
    {
        var result = PhoneUseDetector.Detect(MovingTrip(), [Screen(-3600, true), Screen(61, null)], phoneCapable: true);

        Assert.Equal(1, result.PhoneCount);
    }

    // ---- through the detector ------------------------------------------------------------------------------------------------

    [Fact]
    public void A_trip_found_by_the_detector_takes_its_phone_count_from_the_same_rule()
    {
        // Leave, 40 cruise fixes 6 s apart at 20 m/s, stop: the screen is on for the first 40 s of the drive.
        var fixes = Idle(-672, 0);
        fixes.AddRange(Cruise(0, 0, 20, 6, 40));
        fixes.Add(Fix(246, 4920, mps: 0));
        var trip = Assert.Single(Replay(fixes, 5000).Closed);

        var result = PhoneUseDetector.Detect(trip, [Screen(-60, false), Screen(70, true), Screen(110, false)], phoneCapable: true);

        var phoneEvent = Assert.Single(result.Events);
        Assert.Equal(At(70), phoneEvent.StartUtc);
        Assert.Equal(At(110), phoneEvent.EndUtc);
        Assert.Equal(40.0, phoneEvent.Seconds);
    }
}
