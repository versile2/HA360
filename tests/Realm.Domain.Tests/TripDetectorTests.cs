using System.Text.Json;
using Xunit;
using static Realm.Domain.Tests.TripFixtures;

namespace Realm.Domain.Tests;

// The vectors T1 to T12, T19 and T22 of 02 section 5.10 (T13 to T16 are v1.1 and not here; T17/T21 are in
// SpeedingEpisodeTests, T18/T20 in PhoneUseTests), plus the parameter boundaries of 02 section 5.1 to 5.6.
// The drives run due north from a fictional point (TripFixtures), so expected distances are plain sums of the
// northings; every other expected value is the number or rule written in 02, never the detector's own output.
public class TripDetectorTests
{
    // A drive from the origin to a stop, with the stop's time and northing for the tests that continue from it.
    private sealed record Leg(List<RawFix> Fixes, double StopT, double StopNorth);

    // Leave, then 20 m/s in 6 s steps, then a stopped fix (speed 0) 120 m after the last moving fix.
    private static Leg DriveAndStop(double departT, int cruiseFixes)
    {
        var fixes = Leave(departT);
        fixes.AddRange(Cruise(departT + 24, 276, 20, 6, cruiseFixes));
        var stopT = departT + 24 + (6 * (cruiseFixes + 1));
        var stopNorth = 276 + (120.0 * (cruiseFixes + 1));
        fixes.Add(Fix(stopT, stopNorth, mps: 0));
        return new Leg(fixes, stopT, stopNorth);
    }

    private static TripDetector Fed(IEnumerable<RawFix> fixes)
    {
        var detector = new TripDetector();
        foreach (var fix in fixes)
        {
            detector.Process(fix);
        }

        return detector;
    }

    // ---- T1 plain drive -------------------------------------------------------------------------------------

    [Fact]
    public void T1_a_plain_drive_is_one_trip_back_dated_to_the_departure_fix()
    {
        // Idle at the origin for more than ten minutes (a fix every 42 s up to t = 0).
        var fixes = Idle(-672, 0);

        // 6 s apart for 2 minutes, reaching 20 m/s at 30 s: speeds min(20, 4k), distance by the trapezoid rule, 2 100 m in all.
        var north = 0.0;
        for (var k = 1; k <= 20; k++)
        {
            var speed = Math.Min(20, 4 * k);
            north += (Math.Min(20, 4 * (k - 1)) + speed) / 2 * 6;
            fixes.Add(Fix(6 * k, north, mps: speed));
        }

        Assert.Equal(2100, north, 1e-9);

        // 42 s apart for 11 steps (7.7 minutes) to 8.2 km; the last fix is the stopped one, then silence.
        var step = (8200 - north) / 11;
        for (var m = 1; m <= 11; m++)
        {
            fixes.Add(Fix(120 + (42 * m), north + (m * step), mps: m < 11 ? step / 42 : 0));
        }

        var result = Replay(fixes, asOf: 120 + (42 * 11) + 3600);

        var trip = Assert.Single(result.Closed);
        Assert.Empty(result.Discarded);

        // The first fast fix (8 m/s) is the one at 12 s; the departure fix is the last fix before it within 60 m of
        // the anchor, which is the 6 s fix (12 m out, 4 m/s), at most 240 s before the first fast fix.
        Assert.Equal(At(6), trip.StartUtc);
        Assert.InRange((At(12) - trip.StartUtc).TotalSeconds, 0.0, 240.0);

        // EndUtc is the first stopped fix.
        Assert.Equal(At(120 + (42 * 11)), trip.EndUtc);
        Assert.Equal(TripEndedBy.Stop, trip.EndedBy);
        Assert.Equal(TripQuality.Dense, trip.Quality);
        Assert.False(trip.HasGap);

        // distance_gps_m within 3% of 8 200 (the road from the departure fix is 8 188 m exactly).
        Assert.InRange(trip.DistanceGpsM, 8200 * 0.97, 8200 * 1.03);
        Assert.Equal(8188, trip.DistanceGpsM, 0.01);

        Assert.Equal(20.0, trip.TopSpeedMps);
        Assert.Equal(0, trip.SpeedingCount);
        Assert.Null(trip.PhoneCount);
        Assert.Equal("life360", trip.SourceMask);
        Assert.All(result.Decisions, d => Assert.True(d.InTrack));
    }

    // ---- T2 red light, stop and resume rules ----------------------------------------------------------------

    [Fact]
    public void T2_a_red_light_is_merged_into_one_trip()
    {
        var leg = DriveAndStop(0, 29);
        var fixes = leg.Fixes;

        // Stopped for 100 s, then a fix 80 m away at 2 m/s (>= 1.5 m/s and >= 60 m from the stop point).
        fixes.Add(Fix(leg.StopT + 50, leg.StopNorth, mps: 0.3));
        fixes.Add(Fix(leg.StopT + 100, leg.StopNorth + 80, mps: 2.0));
        fixes.AddRange(Cruise(leg.StopT + 100, leg.StopNorth + 80, 20, 6, 20));
        var endT = leg.StopT + 100 + 126;
        var endNorth = leg.StopNorth + 80 + 2520;
        fixes.Add(Fix(endT, endNorth, mps: 0));

        var result = Replay(fixes, endT + 600);

        var trip = Assert.Single(result.Closed);
        Assert.Empty(result.Discarded);
        Assert.Equal(At(0), trip.StartUtc);
        Assert.Equal(At(endT), trip.EndUtc);
        Assert.Equal(endNorth, trip.DistanceGpsM, 0.01);
        Assert.Contains(trip.Track, p => p.Ts == At(leg.StopT));
    }

    [Theory]
    [InlineData(59, 2.0, false)]   // a crawl less than 60 m from the stop point is not a resume
    [InlineData(61, 2.0, true)]
    [InlineData(200, 1.4, false)]  // below 1.5 m/s is still stopped
    [InlineData(200, 1.5, true)]
    public void A_stopped_trip_resumes_with_1_5_m_s_and_60_m_from_the_stop(double metres, double mps, bool resumes)
    {
        var leg = DriveAndStop(0, 29);
        var detector = Fed(leg.Fixes);
        Assert.Equal(TripState.Settling, detector.State);

        detector.Process(Fix(leg.StopT + 50, leg.StopNorth + metres, mps: mps));

        Assert.Equal(resumes ? TripState.Driving : TripState.Settling, detector.State);
    }

    [Theory]
    [InlineData(1.4, false)]
    [InlineData(1.5, true)]
    public void A_fix_below_1_5_m_s_is_a_stop(double mps, bool stillDriving)
    {
        var leg = DriveAndStop(0, 29);
        var detector = Fed(leg.Fixes.Where(f => f.Ts < At(leg.StopT)));
        Assert.Equal(TripState.Driving, detector.State);

        detector.Process(Fix(leg.StopT, leg.StopNorth, mps: mps));

        Assert.Equal(stillDriving ? TripState.Driving : TripState.Settling, detector.State);
    }

    // ---- T3 drive-thru split --------------------------------------------------------------------------------

    [Fact]
    public void T3_a_stop_longer_than_180_s_splits_the_drive_and_the_second_trip_starts_by_rule_A()
    {
        var leg = DriveAndStop(0, 29);
        var fixes = leg.Fixes;

        // Stopped 240 s (fixes 60 s apart): the first trip closes with the fix at 180 s, at the stop's own time.
        for (var s = 60; s <= 240; s += 60)
        {
            fixes.Add(Fix(leg.StopT + s, leg.StopNorth, mps: 0));
        }

        // Then two fast fixes 40 m and 100 m out (100 m < 150 m: no start yet) and a third 170 m out (>= 150 m).
        fixes.Add(Fix(leg.StopT + 246, leg.StopNorth + 40, mps: 10));
        fixes.Add(Fix(leg.StopT + 252, leg.StopNorth + 100, mps: 10));
        fixes.Add(Fix(leg.StopT + 258, leg.StopNorth + 170, mps: 12));
        fixes.AddRange(Cruise(leg.StopT + 258, leg.StopNorth + 170, 15, 6, 40));
        var secondStopT = leg.StopT + 258 + (6 * 41);
        var secondStopNorth = leg.StopNorth + 170 + (90.0 * 41);
        fixes.Add(Fix(secondStopT, secondStopNorth, mps: 0));

        var detector = new TripDetector();
        var closed = new List<DetectedTrip>();
        foreach (var fix in fixes.OrderBy(f => f.Ts))
        {
            closed.AddRange(detector.Process(fix).Closed);
            if (fix.Ts == At(leg.StopT + 252))
            {
                Assert.Equal(TripState.Idle, detector.State);
            }

            if (fix.Ts == At(leg.StopT + 258))
            {
                Assert.Equal(TripState.Driving, detector.State);
            }
        }

        closed.AddRange(detector.Tick(At(secondStopT + 600)).Closed);

        Assert.Equal(2, closed.Count);
        Assert.Equal(At(leg.StopT), closed[0].EndUtc);
        Assert.Equal(leg.StopNorth, closed[0].DistanceGpsM, 0.01);

        // The second trip is back-dated to the last stopped fix before its first fast fix (within 60 m of the anchor).
        Assert.Equal(At(leg.StopT + 240), closed[1].StartUtc);
        Assert.Equal(At(secondStopT), closed[1].EndUtc);
        Assert.Equal(secondStopNorth - leg.StopNorth, closed[1].DistanceGpsM, 0.01);
        Assert.All(closed, t => Assert.Equal(TripEndedBy.Stop, t.EndedBy));
    }

    [Fact]
    public void The_stop_merge_time_is_exactly_180_s_by_the_30_s_tick()
    {
        var leg = DriveAndStop(0, 29);
        var detector = Fed(leg.Fixes);

        Assert.Empty(detector.Tick(At(leg.StopT + 179)).Closed);
        Assert.Equal(TripState.Settling, detector.State);

        var trip = Assert.Single(detector.Tick(At(leg.StopT + 180)).Closed);
        Assert.Equal(At(leg.StopT), trip.EndUtc);
        Assert.Equal(TripState.Idle, detector.State);
    }

    // ---- T4 jitter at home ----------------------------------------------------------------------------------

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void T4_jitter_at_home_never_makes_a_trip(bool reportsSpeed)
    {
        // An Android phone at home: a fix every 27 s, up to 20 m of scatter, reported speed 0 to 0.6 m/s, and one
        // fix 90 m from the one before (implied 3.3 m/s).
        (double North, double East)[] scatter = [(20, 0), (-20, 0), (0, 20), (0, -20), (14, 14), (-14, -14), (14, -14), (-14, 14)];
        var fixes = new List<RawFix>();
        for (var i = 0; i < 60; i++)
        {
            var (north, east) = scatter[i % scatter.Length];
            if (i == 30)
            {
                north += 90;
            }

            fixes.Add(Fix(
                27 * i,
                north,
                east,
                FixSource.Companion,
                mps: reportsSpeed ? (i % 4) * 0.2 : null,
                accuracy: 12));
        }

        var result = Replay(fixes, asOf: 27 * 60 + 3600);

        Assert.Empty(result.Closed);
        Assert.Empty(result.Discarded);
        Assert.All(result.Decisions, d => Assert.True(d.InTrack));
    }

    [Theory]
    [InlineData(149, false)]   // two fast fixes, but the second is less than 150 m from the anchor
    [InlineData(151, true)]
    public void A_start_needs_the_second_fast_fix_150_m_from_the_anchor(double north, bool starts)
    {
        var detector = Fed(Idle(-252, 0));

        detector.Process(Fix(6, 100, mps: 10));
        detector.Process(Fix(12, north, mps: 10));

        Assert.Equal(starts ? TripState.Driving : TripState.Idle, detector.State);
    }

    [Theory]
    [InlineData(6.7, false)]                    // just under 15 mph
    [InlineData(15 * FixParser.MphToMps, true)] // 15 mph is 6.7056 m/s: converted exactly, not rounded to 6.7 (D70)
    public void A_start_needs_two_fixes_at_15_mph_and_the_reported_speed_wins_over_the_implied_one(double mps, bool starts)
    {
        // The positions imply 16.7 m/s in both cases; the reported speed decides.
        var detector = Fed(Idle(-252, 0));

        detector.Process(Fix(6, 100, mps: mps));
        detector.Process(Fix(12, 200, mps: mps));

        Assert.Equal(starts ? TripState.Driving : TripState.Idle, detector.State);
    }

    [Theory]
    [InlineData(120, true)]
    [InlineData(121, false)]
    public void The_two_fast_fixes_of_a_start_are_at_most_120_s_apart(double gapS, bool starts)
    {
        var detector = Fed(Idle(-252, 0));

        detector.Process(Fix(6, 100, mps: 10));
        detector.Process(Fix(6 + gapS, 300, mps: 10));

        Assert.Equal(starts ? TripState.Driving : TripState.Idle, detector.State);
    }

    // ---- Rule B: the Life360 driving flag with a displacement ------------------------------------------------

    [Theory]
    [InlineData(390, false)]
    [InlineData(410, true)]
    public void The_driving_flag_starts_a_trip_only_with_400_m_of_displacement(double north, bool starts)
    {
        var detector = Fed(Idle(-252, 0));

        detector.Process(Fix(42, north, mps: 0, driving: true));

        Assert.Equal(starts ? TripState.Driving : TripState.Idle, detector.State);
    }

    [Fact]
    public void The_driving_flag_alone_never_makes_the_member_driving()
    {
        var detector = Fed(Idle(-252, 0));

        detector.Process(Fix(42, 100, mps: 0, driving: true));
        detector.Process(Fix(84, 100, mps: 0, driving: true));

        Assert.Equal(TripState.Idle, detector.State);
        Assert.False(detector.IsDriving(At(84)));
    }

    [Fact]
    public void A_slow_trip_started_by_the_flag_is_discarded_for_its_top_speed_below_15_mph()
    {
        // 450 m out at 3 m/s with the flag on, then 3 m/s for 7 more minutes: long and far enough, but never 15 mph.
        var fixes = Idle(-252, 0);
        fixes.Add(Fix(42, 450, mps: 3, driving: true));
        for (var k = 1; k <= 10; k++)
        {
            fixes.Add(Fix(42 + (42 * k), 450 + (126.0 * k), mps: 3, driving: true));
        }

        fixes.Add(Fix(42 + (42 * 11), 450 + (126.0 * 10), mps: 0, driving: true));
        var result = Replay(fixes, asOf: 42 + (42 * 11) + 3600);

        Assert.Empty(result.Closed);
        var discarded = Assert.Single(result.Discarded);
        Assert.True(discarded.DistanceGpsM >= 0.3 * 1609.344);
        Assert.True((discarded.EndUtc - discarded.StartUtc).TotalSeconds >= 120);
        Assert.Equal(3.0, discarded.TopSpeedMps);
    }

    // ---- T5 duplicated-position spike -----------------------------------------------------------------------

    // A drive at 24.6 m/s: A at 30 s, B duplicating A's position 12 s later, then C at 18 s after A, 442.8 m on.
    // B to C implies 442.8 / 6 = 73.8 m/s (165 mph); A to C is 24.6 m/s.
    private static (List<RawFix> Fixes, RawFix A, RawFix B, RawFix C) SpikeDrive(double? cSpeed, double bSpeed = 24.6)
    {
        var fixes = Leave(0);
        const double Speed = 24.6;
        var a = Fix(30, 276 + (6 * Speed), mps: Speed);
        var b = Fix(42, 276 + (6 * Speed), mps: bSpeed);                        // A's position, 12 s later
        var c = Fix(48, 276 + (6 * Speed) + (18 * Speed), mps: cSpeed);        // the real position, 18 s after A
        fixes.Add(a);
        fixes.Add(b);
        fixes.Add(c);
        fixes.AddRange(Cruise(48, 276 + (24 * Speed), Speed, 6, 20));
        fixes.Add(Fix(48 + (6 * 21), 276 + (24 * Speed) + (126 * Speed), mps: 0));
        return (fixes, a, b, c);
    }

    [Fact]
    public void T5_a_duplicated_position_is_retracted_and_the_real_fix_after_it_is_accepted()
    {
        var (fixes, a, b, c) = SpikeDrive(cSpeed: 24.6);
        var detector = new TripDetector();
        var steps = fixes.OrderBy(f => f.Ts).Select(detector.Process).ToList();

        // B went in with A's position; C's step retracts it and accepts C.
        var stepOfB = steps.Single(s => s.Decisions.Any(d => d.Fix == b));
        Assert.True(Assert.Single(stepOfB.Decisions).InTrack);
        var stepOfC = steps.Single(s => s.Decisions.Any(d => d.Fix == c));
        Assert.Equal(b, Assert.Single(stepOfC.Retracted));
        var decisionOfC = Assert.Single(stepOfC.Decisions);
        Assert.True(decisionOfC.InTrack);
        Assert.Null(decisionOfC.Reason);
        Assert.Empty(steps.Where(s => !ReferenceEquals(s, stepOfC)).SelectMany(s => s.Retracted));

        var trip = Assert.Single(detector.Tick(At(10_000)).Closed);

        // The retracted fix is not in the trip, nothing in it is faster than the true 24.6 m/s (no 165 mph), and
        // the distance is the road length from the departure fix (the duplicate is on the line, so it changes nothing).
        Assert.DoesNotContain(trip.Track, p => p.Ts == b.Ts);
        Assert.Contains(trip.Track, p => p.Ts == a.Ts);
        Assert.Contains(trip.Track, p => p.Ts == c.Ts);
        Assert.All(trip.Track, p => Assert.True((p.SpeedMps ?? 0) <= 24.6 + 1e-6));
        Assert.Equal(24.6, trip.TopSpeedMps ?? double.NaN, 1e-9);
        Assert.Equal(276 + (24 * 24.6) + (126 * 24.6), trip.DistanceGpsM, 0.01);
    }

    [Fact]
    public void T5_the_fix_after_a_duplicate_without_a_reported_speed_takes_the_implied_speed_from_the_good_fix()
    {
        var (fixes, _, b, c) = SpikeDrive(cSpeed: null);

        var result = Replay(fixes, 10_000);

        var trip = Assert.Single(result.Closed);
        Assert.Equal(b, Assert.Single(result.Retracted));
        var point = Assert.Single(trip.Track, p => p.Ts == c.Ts);
        Assert.Equal(24.6, point.SpeedMps ?? double.NaN, 1e-6);   // A to C, not 73.8 m/s
        Assert.Null(point.ReportedSpeedMps);
        Assert.Equal(24.6, trip.TopSpeedMps ?? double.NaN, 1e-6);
    }

    [Fact]
    public void T5_retracting_the_duplicate_also_undoes_what_it_did_to_the_state_of_the_trip()
    {
        // The stale duplicate says 0 m/s, which looks like a stop; once it is retracted the trip is driving again.
        var (fixes, _, b, c) = SpikeDrive(cSpeed: 24.6, bSpeed: 0);
        var detector = new TripDetector();

        foreach (var fix in fixes.OrderBy(f => f.Ts))
        {
            detector.Process(fix);
            if (fix == b)
            {
                Assert.Equal(TripState.Settling, detector.State);
            }

            if (fix == c)
            {
                Assert.Equal(TripState.Driving, detector.State);
            }
        }

        var trip = Assert.Single(detector.Tick(At(10_000)).Closed);
        Assert.DoesNotContain(trip.Track, p => p.Ts == b.Ts);
        Assert.Equal(TripEndedBy.Stop, trip.EndedBy);
    }

    [Fact]
    public void A_reported_speed_that_agrees_with_a_fast_jump_is_not_a_spike()
    {
        // The jump is 73.8 m/s and the phone itself says 58 m/s: within 30% (0.3 x 73.8 = 22.1), so the move is believed.
        var (fixes, _, b, c) = SpikeDrive(cSpeed: 58);

        var result = Replay(fixes.Where(f => f.Ts <= At(48)), 10_000);

        Assert.Empty(result.Retracted);
        Assert.All(result.Decisions.Where(d => d.Fix == b || d.Fix == c), d => Assert.True(d.InTrack));
    }

    [Fact]
    public void A_lone_far_fix_is_rejected_as_a_spike_and_the_trip_goes_on()
    {
        // The fix at 102 s lands 5 km off the road; the next one (108 s) is back on it.
        var leg = DriveAndStop(0, 29);
        var fixes = leg.Fixes.Where(f => f.Ts != At(102)).ToList();
        var far = Fix(102, 276 + (13 * 120.0) + 5000, mps: 20);
        fixes.Add(far);

        var result = Replay(fixes, leg.StopT + 600);

        var rejected = Assert.Single(result.Decisions, d => d.Fix == far);
        Assert.False(rejected.InTrack);
        Assert.Equal(TrackReason.Spike, rejected.Reason);
        var trip = Assert.Single(result.Closed);
        Assert.Equal(leg.StopNorth, trip.DistanceGpsM, 0.01);
        Assert.DoesNotContain(trip.Track, p => p.Ts == far.Ts);
        Assert.False(trip.HasGap);
    }

    [Fact]
    public void Three_mutually_consistent_rejects_accept_the_third_and_re_anchor_after_a_real_jump()
    {
        // Up to 96 s the phone is on the road (1 716 m). At 102 s it reappears 5 km further on: three fixes 6 s
        // apart, each 120 m on from the last. The first two are rejected as spikes, the third is accepted.
        var fixes = Leave(0);
        fixes.AddRange(Cruise(24, 276, 20, 6, 12));
        var jumped = new[]
        {
            Fix(102, 6716 + 120, mps: 20),
            Fix(108, 6716 + 240, mps: 20),
            Fix(114, 6716 + 360, mps: 20),
        };
        fixes.AddRange(jumped);
        fixes.AddRange(Cruise(114, 7076, 20, 6, 5));
        fixes.Add(Fix(150, 7076 + 720, mps: 0));

        var result = Replay(fixes, 150 + 600);

        Assert.Equal(TrackReason.Spike, Assert.Single(result.Decisions, d => d.Fix == jumped[0]).Reason);
        Assert.Equal(TrackReason.Spike, Assert.Single(result.Decisions, d => d.Fix == jumped[1]).Reason);
        Assert.True(Assert.Single(result.Decisions, d => d.Fix == jumped[2]).InTrack);
        var trip = Assert.Single(result.Closed);
        Assert.True(trip.HasGap);
        Assert.Equal(7076 + 720, trip.DistanceGpsM, 0.01);   // the chord of the jump is part of the distance
    }

    [Theory]
    [InlineData(300, false)]   // 50 m/s from the last fix: fast, but not above 54 m/s
    [InlineData(348, true)]    // 58 m/s: a spike, and the fix before it (29 m/s from the one earlier) was the outlier
    public void A_jump_implying_more_than_54_m_s_is_a_spike(double metres, bool spike)
    {
        var detector = Fed([Fix(-12, 0, mps: 0), Fix(-6, 0, mps: 0)]);

        var step = detector.Process(Fix(0, metres));

        Assert.Equal(spike ? 1 : 0, step.Retracted.Count);
    }

    [Fact]
    public void A_reported_speed_of_85_m_s_that_the_positions_confirm_is_kept()
    {
        // 85 m/s is above 54 m/s (120 mph) but below the 90 m/s at which a reported speed is discarded.
        var leg = DriveAndStop(0, 29);
        var fixes = leg.Fixes.Where(f => f.Ts != At(102)).ToList();
        var fast = Fix(102, 276 + (12 * 120.0) + (85 * 6), mps: 85);
        fixes.Add(fast);

        var result = Replay(fixes, leg.StopT + 600);

        var trip = Assert.Single(result.Closed);
        var point = Assert.Single(trip.Track, p => p.Ts == fast.Ts);
        Assert.Equal(85.0, point.SpeedMps);
        Assert.Equal(85.0, point.ReportedSpeedMps);
    }

    [Fact]
    public void An_implied_speed_is_only_computed_for_fixes_at_most_120_s_apart()
    {
        // 125 s later and 1 200 m on would imply 9.6 m/s: with no reported speed that is no speed at all, so no start.
        var detector = Fed(Idle(-252, 0));

        detector.Process(Fix(125, 1200));
        detector.Process(Fix(131, 1320));

        Assert.Equal(TripState.Idle, detector.State);
    }

    [Fact]
    public void The_anchor_is_the_median_of_the_last_five_fixes_so_one_stray_fix_does_not_move_it()
    {
        // Five fixes at the origin and one 100 m off: the anchor stays at the origin, so 230 m out is far enough.
        var fixes = Idle(-252, -42);
        fixes.Add(Fix(0, 100, mps: 0));
        var detector = Fed(fixes);

        detector.Process(Fix(6, 190, mps: 10));
        detector.Process(Fix(12, 230, mps: 10));

        Assert.Equal(TripState.Driving, detector.State);
    }

    [Fact]
    public void A_speed_above_90_m_s_is_discarded_to_unknown()
    {
        var leg = DriveAndStop(0, 29);
        var fixes = leg.Fixes.Where(f => f.Ts != At(100)).ToList();
        var absurd = Fix(100, 276 + (76 * 20), mps: 100);
        fixes.Add(absurd);

        var result = Replay(fixes, leg.StopT + 600);

        var trip = Assert.Single(result.Closed);
        var point = Assert.Single(trip.Track, p => p.Ts == absurd.Ts);
        Assert.Null(point.SpeedMps);
        Assert.Null(point.ReportedSpeedMps);
        Assert.Equal(20.0, trip.TopSpeedMps ?? double.NaN, 1e-9);
    }

    // ---- T6 lingering flag, arrival shortcut ----------------------------------------------------------------

    [Fact]
    public void T6_a_lingering_driving_flag_does_not_extend_the_trip()
    {
        var leg = DriveAndStop(0, 29);

        // Arrival; the flag stays true for 9 minutes with speed 0.
        var fixes = leg.Fixes.Select(f => f.Ts >= At(leg.StopT) ? f with { Driving = true } : f).ToList();
        for (var s = 42; s <= 540; s += 42)
        {
            fixes.Add(Fix(leg.StopT + s, leg.StopNorth, mps: 0, driving: true));
        }

        var detector = new TripDetector();
        var closed = new List<DetectedTrip>();
        foreach (var fix in fixes.OrderBy(f => f.Ts))
        {
            closed.AddRange(detector.Process(fix).Closed);
            if (fix.Ts == At(leg.StopT))
            {
                Assert.True(detector.IsDriving(fix.Ts));
            }

            if (fix.Ts == At(leg.StopT + 168))
            {
                Assert.Equal(TripState.Settling, detector.State);
            }

            if (fix.Ts == At(leg.StopT + 210))
            {
                // 210 s after the first stopped fix: closed, not driving, nothing open.
                Assert.Equal(TripState.Idle, detector.State);
                Assert.False(detector.IsDriving(fix.Ts));
                Assert.Null(detector.OpenSinceUtc);
            }
        }

        var trip = Assert.Single(closed);
        Assert.Equal(At(leg.StopT), trip.EndUtc);
        Assert.Equal(TripState.Idle, detector.State);
        Assert.False(detector.IsDriving(fixes.Max(f => f.Ts)));
    }

    [Theory]
    [InlineData(44, true)]
    [InlineData(45, false)]
    public void A_stop_of_45_s_inside_a_drawn_zone_ends_the_driving_status_at_once(double secondsStopped, bool stillDriving)
    {
        var leg = DriveAndStop(0, 29);
        var home = new RawPlace("home", "Hearth Haven", Lat(leg.StopNorth), Lon(0), 100, false);
        var detector = new TripDetector { Zones = [home] };
        foreach (var fix in leg.Fixes)
        {
            detector.Process(fix);
        }

        detector.Process(Fix(leg.StopT + 30, leg.StopNorth, mps: 0));

        Assert.Equal(TripState.Settling, detector.State);
        Assert.Equal(stillDriving, detector.IsDriving(At(leg.StopT + secondsStopped)));
    }

    [Fact]
    public void A_stop_outside_every_zone_keeps_the_driving_status_until_the_trip_closes()
    {
        var leg = DriveAndStop(0, 29);
        var elsewhere = new RawPlace("work", "Cobblestone Court", Lat(leg.StopNorth + 5000), Lon(0), 100, false);
        var detector = new TripDetector { Zones = [elsewhere] };
        foreach (var fix in leg.Fixes)
        {
            detector.Process(fix);
        }

        detector.Process(Fix(leg.StopT + 30, leg.StopNorth, mps: 0));

        Assert.True(detector.IsDriving(At(leg.StopT + 100)));
        Assert.True(detector.IsDriving(At(leg.StopT + 179)));
    }

    // ---- T7 flag lag -----------------------------------------------------------------------------------------

    [Fact]
    public void T7_a_late_driving_flag_changes_nothing_the_start_is_by_rule_A_and_back_dated()
    {
        // The phone's last idle fix is 36 s before motion starts at t0 = 0 (the idle fixes are 42 s apart).
        var fixes = Idle(-666, -36);
        fixes.Add(Fix(0, 60, mps: 12));
        fixes.Add(Fix(6, 144, mps: 16));
        var third = Fix(12, 252, mps: 20);
        fixes.Add(third);

        // The flag turns on at t0 + 121 s.
        for (var k = 1; k <= 40; k++)
        {
            fixes.Add(Fix(12 + (6 * k), 252 + (120.0 * k), mps: 20, driving: 12 + (6 * k) >= 121));
        }

        fixes.Add(Fix(12 + (6 * 41), 252 + (120.0 * 40) + 120, mps: 0, driving: true));

        var detector = new TripDetector();
        foreach (var fix in fixes.OrderBy(f => f.Ts))
        {
            detector.Process(fix);
            if (fix.Ts == At(6))
            {
                Assert.Equal(TripState.Idle, detector.State);   // 144 m from the anchor: not yet 150 m
            }

            if (fix.Ts == At(12))
            {
                // Rule A fires with the third fast fix, about t0 + 12 s, long before any flag.
                Assert.Equal(TripState.Driving, detector.State);
                Assert.Equal(At(-36), detector.OpenSinceUtc);
            }
        }

        var trip = Assert.Single(detector.Tick(At(10_000)).Closed);
        Assert.Equal(At(-36), trip.StartUtc);                  // t0 minus at most one 42 s idle step
        Assert.InRange((At(0) - trip.StartUtc).TotalSeconds, 0.0, 42.0);
    }

    // ---- T8 never flagged ------------------------------------------------------------------------------------

    [Fact]
    public void T8_a_12_km_drive_is_detected_without_the_driving_flag()
    {
        var fixes = Leave(0);
        fixes.AddRange(Cruise(24, 276, 20, 42, 13));   // 42 s apart: 13 steps of 840 m
        var stopNorth = 276 + (14 * 840.0);
        var stopT = 24 + (42 * 14);
        fixes.Add(Fix(stopT, stopNorth, mps: 0));
        var flagFalse = fixes.Select(f => f with { Driving = false }).ToList();

        var result = Replay(flagFalse, stopT + 600);

        var trip = Assert.Single(result.Closed);
        Assert.InRange(trip.DistanceGpsM, 12_000.0, 12_200.0);
        Assert.Equal(stopNorth, trip.DistanceGpsM, 0.01);
        Assert.False(trip.HasGap);
    }

    // ---- T9 0,0 bursts of the vehicle tracker ----------------------------------------------------------------

    [Fact]
    public void T9_zero_zero_fixes_of_the_vehicle_tracker_are_dropped_at_parsing_and_cause_no_spike_or_trip()
    {
        // The FordPass tracker bursts 0,0 between real fixes: FixParser drops each one, so the detector never sees it.
        var parsedAway = 0;
        var fordPass = new List<RawFix>();
        var realFixes = DriveAndStop(0, 29).Fixes;
        foreach (var fix in realFixes)
        {
            var snapshot = new HaEntitySnapshot(
                "device_tracker.fordpass_vin_tracker",
                "home",
                new Dictionary<string, JsonElement>
                {
                    ["latitude"] = JsonSerializer.SerializeToElement(0.0),
                    ["longitude"] = JsonSerializer.SerializeToElement(0.0),
                },
                fix.Ts.AddSeconds(1),
                fix.Ts.AddSeconds(1));
            if (FixParser.ParseTracker(snapshot, FixSource.FordPass, fix.Ts.AddSeconds(2)) is { } parsed)
            {
                fordPass.Add(parsed);
            }
            else
            {
                parsedAway++;
            }
        }

        Assert.Equal(realFixes.Count, parsedAway);
        Assert.Empty(fordPass);

        var result = Replay([.. realFixes, .. fordPass], 10_000);

        Assert.All(result.Decisions, d => Assert.True(d.InTrack));
        Assert.Single(result.Closed);

        // And a vehicle tracker alone makes no trip: its fixes never feed a member's track.
        var vehicleOnly = realFixes.Select(f => f with { Source = FixSource.FordPass, EntityId = "device_tracker.fordpass_vin_tracker" });
        var nothing = Replay(vehicleOnly, 10_000);
        Assert.Empty(nothing.Closed);
        Assert.Empty(nothing.Discarded);
        Assert.All(nothing.Decisions, d => Assert.Equal(TrackReason.Priority, d.Reason));
    }

    // ---- T10 tunnel, T11 dead zone ---------------------------------------------------------------------------

    [Fact]
    public void T10_a_tunnel_of_7_minutes_keeps_one_trip_with_a_gap()
    {
        var fixes = Leave(0);
        fixes.AddRange(Cruise(24, 276, 20, 6, 18));           // to t = 132, 2 436 m
        var after = 132 + 420;                                // silence for 7 minutes (< 600 s)
        var resumeNorth = 2436 + 8400.0;                      // 8.4 km further on
        fixes.Add(Fix(after, resumeNorth, mps: 20));
        fixes.AddRange(Cruise(after, resumeNorth, 20, 6, 10));
        var stopT = after + 66;
        var stopNorth = resumeNorth + 1320;
        fixes.Add(Fix(stopT, stopNorth, mps: 0));

        var result = Replay(fixes, stopT + 600);

        var trip = Assert.Single(result.Closed);
        Assert.Empty(result.Discarded);
        Assert.True(trip.HasGap);
        Assert.Equal(TripEndedBy.Stop, trip.EndedBy);
        Assert.Equal(At(0), trip.StartUtc);
        Assert.Equal(stopNorth, trip.DistanceGpsM, 0.01);    // the chord across the gap is counted
        Assert.All(result.Decisions, d => Assert.True(d.InTrack));
    }

    [Theory]
    [InlineData(150, false)]
    [InlineData(170, false)]
    [InlineData(181, true)]
    public void A_gap_is_flagged_only_when_a_segment_is_longer_than_180_s(double silenceS, bool hasGap)
    {
        var fixes = Leave(0);
        fixes.AddRange(Cruise(24, 276, 20, 6, 10));           // to t = 84
        var resumeT = 84 + silenceS;
        var resumeNorth = 276 + (10 * 120.0) + (silenceS * 20);
        fixes.Add(Fix(resumeT, resumeNorth, mps: 20));
        fixes.Add(Fix(resumeT + 6, resumeNorth + 120, mps: 0));

        var result = Replay(fixes, resumeT + 600);

        var trip = Assert.Single(result.Closed);
        Assert.Equal(hasGap, trip.HasGap);
    }

    [Fact]
    public void T11_a_dead_zone_ends_the_trip_for_no_fix_and_merges_when_the_chord_is_slow_enough()
    {
        var (before, lastT, lastNorth) = DriveThenSilence(speedAtLastFix: 20);

        // Back after 12 minutes (720 s), 13 km further on: 18 m/s over the chord (<= 45 m/s).
        var fixes = new List<RawFix>([.. before, .. Resume(lastT + 720, lastNorth + 13_000, out var stopT, out var stopNorth)]);

        var result = Replay(fixes, stopT + 600);

        var trip = Assert.Single(result.Closed);
        Assert.Empty(result.Discarded);
        Assert.True(trip.HasGap);
        Assert.Equal(At(0), trip.StartUtc);
        Assert.Equal(At(stopT), trip.EndUtc);
        Assert.Equal(TripEndedBy.Stop, trip.EndedBy);
        Assert.Equal(stopNorth, trip.DistanceGpsM, 0.01);
    }

    [Fact]
    public void T11_a_dead_zone_does_not_merge_when_the_chord_needs_more_than_45_m_s()
    {
        var (before, lastT, lastNorth) = DriveThenSilence(speedAtLastFix: 20);

        // 50 km in 720 s is 69 m/s.
        var fixes = new List<RawFix>([.. before, .. Resume(lastT + 720, lastNorth + 50_000, out var stopT, out var stopNorth)]);

        var result = Replay(fixes, stopT + 600);

        Assert.Equal(2, result.Closed.Count);
        var first = result.Closed[0];
        Assert.Equal(TripEndedBy.NoFix, first.EndedBy);
        Assert.Equal(At(lastT), first.EndUtc);
        Assert.Equal(lastNorth, first.DistanceGpsM, 0.01);
        var second = result.Closed[1];
        Assert.Equal(TripEndedBy.Stop, second.EndedBy);
        Assert.Equal(At(stopT), second.EndUtc);
        Assert.Equal(At(lastT + 720), second.StartUtc);
    }

    [Fact]
    public void A_trip_that_was_not_moving_at_its_last_fix_does_not_merge()
    {
        // The last fix before the silence did 3 m/s (slow, not stopped): M2 needs the earlier trip to end while moving.
        var (before, lastT, lastNorth) = DriveThenSilence(speedAtLastFix: 3);

        var fixes = new List<RawFix>([.. before, .. Resume(lastT + 720, lastNorth + 13_000, out var stopT, out _)]);
        var result = Replay(fixes, stopT + 600);

        Assert.Equal(2, result.Closed.Count);
        Assert.Equal(TripEndedBy.NoFix, result.Closed[0].EndedBy);
    }

    [Fact]
    public void A_trip_with_no_fix_closes_at_600_s_and_is_reported_once_it_can_no_longer_merge()
    {
        var (before, lastT, _) = DriveThenSilence(speedAtLastFix: 20);
        var detector = Fed(before);

        Assert.Empty(detector.Tick(At(lastT + 599)).Closed);
        Assert.Equal(TripState.Driving, detector.State);

        // At 600 s the trip is closed at its last fix, but held for a merge for up to 900 s.
        Assert.Empty(detector.Tick(At(lastT + 600)).Closed);
        Assert.Equal(TripState.Idle, detector.State);
        Assert.Empty(detector.Tick(At(lastT + 900)).Closed);

        var trip = Assert.Single(detector.Tick(At(lastT + 901)).Closed);
        Assert.Equal(TripEndedBy.NoFix, trip.EndedBy);
        Assert.Equal(At(lastT), trip.EndUtc);
    }

    [Fact]
    public void Fixes_rejected_for_accuracy_do_not_keep_a_trip_open_past_600_s()
    {
        // CR1-011: a phone in a tunnel still reports every 30 s, but only 1 km accuracy fixes. There is no usable position, so no fix.
        var (before, lastT, lastNorth) = DriveThenSilence(speedAtLastFix: 20);
        var detector = Fed(before);

        for (var k = 1; k <= 19; k++)
        {
            var decision = Assert.Single(detector.Process(Fix(lastT + (30 * k), lastNorth + (600.0 * k), mps: 20, accuracy: 1000)).Decisions);
            Assert.Equal(TrackReason.Accuracy, decision.Reason);
        }

        Assert.Equal(TripState.Driving, detector.State);

        detector.Process(Fix(lastT + 600, lastNorth + 12_000, mps: 20, accuracy: 1000));

        Assert.Equal(TripState.Idle, detector.State);
        var trip = Assert.Single(detector.Tick(At(lastT + 901)).Closed);
        Assert.Equal(TripEndedBy.NoFix, trip.EndedBy);
        Assert.Equal(At(lastT), trip.EndUtc);
    }

    [Fact]
    public void A_fix_outranked_by_a_better_source_still_counts_as_a_fix_for_the_no_fix_close()
    {
        // 02 5.3 says "no fix at all": Life360 fixes dropped for Priority show the phone is alive, so they extend the 600 s.
        var fixes = Leave(0);
        fixes.AddRange(Cruise(24, 276, 20, 6, 18, source: FixSource.Companion));
        var (lastT, lastNorth) = (132.0, 276 + (20.0 * 108));
        var detector = Fed(fixes);

        var outranked = Assert.Single(detector.Process(Fix(lastT + 100, lastNorth + 2000, mps: 20)).Decisions);
        Assert.Equal(TrackReason.Priority, outranked.Reason);

        Assert.Empty(detector.Tick(At(lastT + 650)).Closed);
        Assert.Equal(TripState.Driving, detector.State);
        detector.Tick(At(lastT + 700));
        Assert.Equal(TripState.Idle, detector.State);
    }

    // A drive that goes silent while moving: Leave, 18 cruise fixes, then a last fix doing speedAtLastFix.
    private static (List<RawFix> Fixes, double LastT, double LastNorth) DriveThenSilence(double speedAtLastFix)
    {
        var fixes = Leave(0);
        fixes.AddRange(Cruise(24, 276, 20, 6, 18));
        fixes.Add(Fix(138, 276 + (19 * 120.0), mps: speedAtLastFix));
        return (fixes, 138, 276 + (19 * 120.0));
    }

    // The member is back: 6 s fixes at 20 m/s for 2 minutes, then a stop.
    private static List<RawFix> Resume(double t, double north, out double stopT, out double stopNorth)
    {
        var fixes = new List<RawFix> { Fix(t, north, mps: 20) };
        fixes.AddRange(Cruise(t, north, 20, 6, 20));
        stopT = t + 126;
        stopNorth = north + 2520;
        fixes.Add(Fix(stopT, stopNorth, mps: 0));
        return fixes;
    }

    // ---- T12 short hop and validity ---------------------------------------------------------------------------

    [Fact]
    public void T12_a_300_m_hop_at_8_m_s_is_discarded()
    {
        var fixes = Idle(-252, 0);
        for (var k = 1; k <= 6; k++)
        {
            fixes.Add(Fix(6 * k, 48.0 * k, mps: 8));
        }

        fixes.Add(Fix(42, 300, mps: 0));

        var result = Replay(fixes, 42 + 3600);

        Assert.Empty(result.Closed);
        var discarded = Assert.Single(result.Discarded);
        Assert.Equal(300, discarded.DistanceGpsM, 0.01);
    }

    // A dense trip of the given length and duration: standing still, two fast fixes (10 m/s, 100 m and 200 m out),
    // then a stopped fix at the far end, so that it is the length and the duration that decide validity.
    private static TripStep Hop(double metres, double seconds)
    {
        var fixes = Idle(-252, 0);
        fixes.Add(Fix(10, 100, mps: 10));
        fixes.Add(Fix(20, 200, mps: 10));
        fixes.Add(Fix(seconds, metres, mps: 0));
        return Replay(fixes, seconds + 3600);
    }

    [Theory]
    [InlineData(482.8, 130, false)]   // 0.3 mi is 482.8032 m (D70: converted exactly, not rounded to 483)
    [InlineData(482.81, 130, true)]
    [InlineData(700, 119, false)]     // and 2 minutes
    [InlineData(700, 121, true)]
    public void A_trip_is_valid_from_0_3_mi_and_120_s(double metres, double seconds, bool valid)
    {
        var result = Hop(metres, seconds);

        Assert.Equal(valid ? 1 : 0, result.Closed.Count);
        Assert.Equal(valid ? 0 : 1, result.Discarded.Count);
    }

    // ---- T19 DST ---------------------------------------------------------------------------------------------

    [Fact]
    public void T19_a_trip_across_the_fall_back_hour_has_its_duration_on_utc_and_its_week_by_local_start()
    {
        // Sat 2026-10-31 22:00 CDT (UTC-5) to Sun 2026-11-01 03:30 CST (UTC-6): 5.5 h on the wall clock, 6.5 h elapsed.
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");
        var start = new DateTimeOffset(2026, 11, 1, 3, 0, 0, TimeSpan.Zero);
        var offset = (start - T0).TotalSeconds;
        var fixes = Idle(offset - 672, offset);
        for (var k = 1; k <= 390; k++)
        {
            fixes.Add(Fix(offset + (60 * k), 1200.0 * k, mps: k == 390 ? 0 : 20));
        }

        var result = Replay(fixes, offset + 23_400 + 3600);

        var trip = Assert.Single(result.Closed);
        Assert.Equal(start, trip.StartUtc);
        Assert.Equal(start.AddHours(6.5), trip.EndUtc);
        Assert.Equal(23_400, trip.DurationS);

        var localStart = TimeZoneInfo.ConvertTime(trip.StartUtc, zone);
        var localEnd = TimeZoneInfo.ConvertTime(trip.EndUtc, zone);
        Assert.Equal(DayOfWeek.Saturday, localStart.DayOfWeek);
        Assert.Equal(22, localStart.Hour);
        Assert.Equal(DayOfWeek.Sunday, localEnd.DayOfWeek);
        Assert.Equal(3, localEnd.Hour);
        Assert.Equal(30, localEnd.Minute);

        // With Sunday weeks, the local start (Saturday) is in the week of Oct 25; its UTC start (Sunday Nov 1) would not be.
        var wednesday = new DateTimeOffset(2026, 11, 4, 18, 0, 0, TimeSpan.Zero);
        Assert.Equal(1, WeekMath.WeekOffsetOf(trip.StartUtc, wednesday, DayOfWeek.Sunday, zone));
    }

    // ---- T22 implied speed -----------------------------------------------------------------------------------

    [Fact]
    public void T22_a_fix_without_a_speed_attribute_uses_the_speed_implied_by_its_neighbours()
    {
        // No fix reports a speed; the positions imply 20 m/s.
        var fixes = Idle(-252, 0, mps: null);
        fixes.AddRange(Cruise(0, 0, 20, 6, 30, omitSpeed: true));
        fixes.Add(Fix(186, 3720, mps: null));

        var result = Replay(fixes, 186 + 3600);

        var trip = Assert.Single(result.Closed);
        var moving = trip.Track.Where(p => p.Ts > At(0) && p.Ts < At(186)).ToList();
        Assert.NotEmpty(moving);
        Assert.All(moving, p => Assert.Equal(20.0, p.SpeedMps ?? double.NaN, 1e-6));
        Assert.All(trip.Track, p => Assert.Null(p.ReportedSpeedMps));
        Assert.Equal(20.0, trip.TopSpeedMps ?? double.NaN, 1e-6);
    }

    // ---- 5.1 and 5.2: the track filters and the source priority ----------------------------------------------

    [Fact]
    public void An_exact_repeat_of_the_last_fix_is_dropped_and_an_older_fix_is_dropped()
    {
        var detector = new TripDetector();
        var first = Fix(0, 0, mps: 0);
        detector.Process(first);

        var repeat = Assert.Single(detector.Process(first).Decisions);
        var older = Assert.Single(detector.Process(Fix(-10, 5, mps: 0)).Decisions);

        Assert.False(repeat.InTrack);
        Assert.Equal(TrackReason.Dropped, repeat.Reason);
        Assert.False(older.InTrack);
        Assert.Equal(TrackReason.Dropped, older.Reason);
    }

    [Theory]
    [InlineData(100.1, false)]
    [InlineData(100.0, true)]
    [InlineData(null, true)]
    public void A_fix_with_an_accuracy_over_100_m_stays_out_of_the_track(double? accuracy, bool inTrack)
    {
        var detector = new TripDetector();
        detector.Process(Fix(0, 0, mps: 0));

        var decision = Assert.Single(detector.Process(Fix(42, 10, mps: 0, accuracy: accuracy)).Decisions);

        Assert.Equal(inTrack, decision.InTrack);
        Assert.Equal(inTrack ? null : TrackReason.Accuracy, decision.Reason);
    }

    [Fact]
    public void The_better_source_feeds_the_track_and_the_next_one_takes_over_after_120_s()
    {
        // Rank for an Android phone: its companion (reports a speed), then Life360.
        var detector = new TripDetector();
        (RawFix Fix, TrackReason? Reason)[] expected =
        [
            (Fix(0, 0, source: FixSource.Companion, mps: 0), null),
            (Fix(27, 0, source: FixSource.Companion, mps: 0), null),
            (Fix(30, 0, mps: 0), TrackReason.Priority),                                 // Life360 while the companion is alive
            (Fix(140, 0, mps: 0), TrackReason.Priority),                                // 113 s after the companion's last fix: still within 120 s
            (Fix(150, 0, mps: 0), null),                                 // 123 s: the companion went quiet, Life360 takes over
            (Fix(160, 0, source: FixSource.Companion, mps: 0), null),    // the companion is back and takes back
            (Fix(170, 0, mps: 0), TrackReason.Priority),
        ];

        AssertDecisions(detector, expected);
    }

    [Fact]
    public void An_iphone_companion_without_a_speed_ranks_below_life360()
    {
        var detector = new TripDetector();
        (RawFix Fix, TrackReason? Reason)[] expected =
        [
            (Fix(0, 0, mps: 0), null),                                   // Life360
            (Fix(30, 0, source: FixSource.Companion), TrackReason.Priority),            // the companion never reports a speed: Life360 is live
            (Fix(100, 0, source: FixSource.Companion), TrackReason.Priority),           // 100 s after Life360's last fix
            (Fix(130, 0, source: FixSource.Companion), null),            // 130 s: Life360 went quiet, the companion feeds the track
            (Fix(140, 0, mps: 0), null),                                 // Life360 is back (nothing better is live)
        ];

        AssertDecisions(detector, expected);
    }

    [Fact]
    public void An_android_companion_fix_without_a_speed_still_ranks_with_its_source_not_with_the_fix()
    {
        // CR1-005: the rank belongs to the source. This companion has reported a speed, so a fix that lacks one is not an iPhone's.
        var detector = new TripDetector();
        (RawFix Fix, TrackReason? Reason)[] expected =
        [
            (Fix(0, 0, source: FixSource.Companion, mps: 0), null),
            (Fix(27, 0, source: FixSource.Companion), null),             // no speed attribute this time: still first rank
            (Fix(30, 0, mps: 0), TrackReason.Priority),                                 // Life360 is outranked by it
        ];

        AssertDecisions(detector, expected);
    }

    [Fact]
    public void A_fix_rejected_from_the_track_does_not_silence_the_sources_below_it()
    {
        // CR1-005: a better source's fix that is rejected (here for accuracy) never fed the track, so Life360 is not held back by it.
        var detector = new TripDetector();
        (RawFix Fix, TrackReason? Reason)[] expected =
        [
            (Fix(0, 0, source: FixSource.Companion, mps: 0), null),
            (Fix(50, 0, source: FixSource.Companion, mps: 0, accuracy: 500), TrackReason.Accuracy),
            (Fix(130, 0, mps: 0), null),                                 // 130 s after the last companion fix that counted
        ];

        AssertDecisions(detector, expected);
    }

    private static void AssertDecisions(TripDetector detector, IEnumerable<(RawFix Fix, TrackReason? Reason)> expected)
    {
        foreach (var (fix, reason) in expected)
        {
            var decision = Assert.Single(detector.Process(fix).Decisions);
            Assert.Equal(reason is null, decision.InTrack);
            Assert.Equal(reason, decision.Reason);
        }
    }

    // ---- 5.6 coarse trips -------------------------------------------------------------------------------------

    [Fact]
    public void A_phone_that_reports_every_5_minutes_makes_a_coarse_trip()
    {
        // An iPhone alone: 1 800 m in 300 s is 6 m/s, twice in a row, then it stays put.
        var fixes = new List<RawFix>
        {
            Fix(0, 0, source: FixSource.Companion),
            Fix(300, 1800, source: FixSource.Companion),
            Fix(600, 3600, source: FixSource.Companion),
            Fix(900, 3600, source: FixSource.Companion),
            Fix(1200, 3600, source: FixSource.Companion),
        };

        var result = Replay(fixes, 5000);

        var trip = Assert.Single(result.Closed);
        Assert.Equal(TripQuality.Coarse, trip.Quality);
        Assert.Equal(TripEndedBy.Coarse, trip.EndedBy);
        Assert.Equal(At(0), trip.StartUtc);
        Assert.Equal(At(600), trip.EndUtc);
        Assert.Equal(3600, trip.DistanceGpsM, 0.01);
        Assert.Null(trip.TopSpeedMps);
        Assert.Null(trip.SpeedingCount);
        Assert.Null(trip.PhoneCount);
        Assert.Equal("companion", trip.SourceMask);
    }

    [Theory]
    [InlineData(1800, 300, true)]
    [InlineData(1510, 300, true)]
    [InlineData(1400, 300, false)]   // 4.67 m/s: below 5.0 m/s
    [InlineData(900, 300, false)]    // below 1 000 m
    [InlineData(5000, 901, false)]   // more than 900 s apart
    [InlineData(5000, 850, true)]    // 850 s apart is still sparse data
    [InlineData(950, 150, false)]    // 6.3 m/s but only 950 m
    [InlineData(1050, 150, true)]
    [InlineData(1200, 120, false)]   // 120 s or less is dense data, not a sparse pair
    public void A_sparse_pair_is_a_coarse_trip_from_1000_m_5_m_s_and_up_to_900_s(double metres, double seconds, bool coarse)
    {
        var fixes = new List<RawFix>
        {
            Fix(0, 0, source: FixSource.Companion),
            Fix(seconds, metres, source: FixSource.Companion),
            Fix(seconds + 2000, metres, source: FixSource.Companion),
        };

        var result = Replay(fixes, seconds + 5000);

        Assert.Equal(coarse ? 1 : 0, result.Closed.Count);
    }

    // ---- places and streets -----------------------------------------------------------------------------------

    [Fact]
    public void A_trip_names_its_endpoints_by_zone_and_by_the_nearest_life360_street()
    {
        var leg = DriveAndStop(0, 29);
        var fixes = leg.Fixes.Select(f =>
            f.Ts == At(0) ? f with { Address = "48 Larkspur Lane, Millbrook, AL" }
            : f.Ts == At(24) ? f with { Address = "Interstate 65, Pinebrook, AL" }
            : f.Ts == At(leg.StopT) ? f with { Address = "Eastgate Avenue, Pinebrook, AL, USA" }
            : f).ToList();
        var detector = new TripDetector
        {
            Zones =
            [
                new RawPlace("home", "Hearth Haven", Lat(0), Lon(0), 60, false),
                new RawPlace("work", "Cobblestone Court", Lat(leg.StopNorth), Lon(0), 80, false),
            ],
        };

        var result = Replay(fixes, leg.StopT + 600, detector);

        var trip = Assert.Single(result.Closed);
        Assert.Equal("home", trip.StartPlaceId);
        Assert.Equal("work", trip.EndPlaceId);
        Assert.Equal("48 Larkspur Lane", trip.StartStreet);
        Assert.Equal("Eastgate Avenue", trip.EndStreet);
        Assert.Equal(At(24), trip.TopSpeedAtUtc);
        Assert.Equal("Interstate 65", trip.TopSpeedStreet);
    }

    [Theory]
    [InlineData(18, "48 Larkspur Lane")]    // the fix 168 m from the start carries the address: within 250 m
    [InlineData(24, null)]                  // 276 m from the start: too far
    public void A_trip_end_takes_the_street_of_a_life360_address_within_250_m(double addressAtS, string? street)
    {
        var leg = DriveAndStop(0, 29);
        var fixes = leg.Fixes.Select(f => f.Ts == At(addressAtS) ? f with { Address = "48 Larkspur Lane, Millbrook, AL" } : f).ToList();

        var trip = Assert.Single(Replay(fixes, leg.StopT + 600).Closed);

        Assert.Equal(street, trip.StartStreet);
    }

    [Theory]
    [InlineData(-300, "48 Larkspur Lane")]   // 300 s before the start of the trip
    [InlineData(-301, null)]
    public void A_trip_end_takes_the_street_of_a_life360_address_within_5_minutes(double addressAtS, string? street)
    {
        var leg = DriveAndStop(0, 29);
        var fixes = leg.Fixes.ToList();
        fixes.Add(Fix(addressAtS, 0, mps: 0, address: "48 Larkspur Lane, Millbrook, AL"));

        var trip = Assert.Single(Replay(fixes, leg.StopT + 600).Closed);

        Assert.Equal(street, trip.StartStreet);
    }

    // ---- determinism ------------------------------------------------------------------------------------------

    private static List<RawFix> ABusyMorning()
    {
        var fixes = new List<RawFix>();
        fixes.AddRange(DriveAndStop(0, 29).Fixes);                           // a plain drive
        fixes.AddRange(SpikeDrive(cSpeed: null).Fixes.Select(f => Shift(f, 5000)));      // with a duplicated position
        var tunnel = Leave(10_000);
        tunnel.AddRange(Cruise(10_024, 276, 20, 6, 18));
        tunnel.AddRange(Resume(10_024 + 108 + 420, 276 + 2160 + 8400, out _, out _));
        fixes.AddRange(tunnel);                                              // with a tunnel
        fixes.Add(Fix(20_000, 0, source: FixSource.Companion));              // a lone iPhone fix
        return fixes;
    }

    private static RawFix Shift(RawFix fix, double seconds) => fix with { Ts = fix.Ts.AddSeconds(seconds) };

    [Fact]
    public void Replaying_the_same_fixes_twice_gives_identical_trips()
    {
        var fixes = ABusyMorning();

        var first = Replay(fixes, 30_000);
        var second = Replay(fixes, 30_000);

        Assert.True(first.Closed.Count >= 3);
        Assert.Equal(Describe(first), Describe(second));
    }

    [Fact]
    public void Live_processing_and_replay_agree_and_input_order_does_not_matter()
    {
        var fixes = ABusyMorning();

        var replayed = Replay(fixes, 30_000);
        var live = Live(fixes, 30_000);
        var reversed = Replay(Enumerable.Reverse(fixes), 30_000);

        Assert.Equal(Describe(replayed), Describe(live));
        Assert.Equal(Describe(replayed), Describe(reversed));
    }

    [Fact]
    public void Replay_leaves_the_detector_ready_for_live_fixes()
    {
        var leg = DriveAndStop(0, 29);
        var detector = new TripDetector();
        var replayed = detector.Replay(leg.Fixes.Where(f => f.Ts < At(leg.StopT)), At(leg.StopT - 6));
        Assert.Empty(replayed.Closed);
        Assert.Equal(TripState.Driving, detector.State);

        var step = detector.Process(Fix(leg.StopT, leg.StopNorth, mps: 0));
        var trip = Assert.Single(detector.Tick(At(leg.StopT + 200)).Closed);

        Assert.Empty(step.Closed);
        Assert.Equal(At(leg.StopT), trip.EndUtc);
    }
}
