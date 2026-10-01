using System.Globalization;
using System.Text.Json;
using Xunit;
using static Realm.Domain.Tests.TripFixtures;

namespace Realm.Domain.Tests;

// 02 section 5.8 and the vectors T17 and T21 of 5.10. The threshold is 80 mph x 0.44704 = 35.7632 m/s (D39),
// an episode is >= 2 consecutive corroborated fixes at or above it spanning >= 30 s, it ends below
// threshold - 2.0 m/s or at a gap of more than 120 s, and a lone fast fix never counts.
public class SpeedingEpisodeTests
{
    private const double Threshold = 80 * FixParser.MphToMps;

    // A fix of a track: its reported speed (null: none) and the speed implied by the segment into it; the implied
    // speed is the reported one unless given, so a sample can contradict its own reported speed.
    private sealed record Sample(double T, double? Reported, double? Implied = null);

    // The points lie on the road: each is placed so that the segment into it implies the sample's implied speed.
    private static List<TrackPoint> Track(params Sample[] samples)
    {
        var points = new List<TrackPoint>();
        var north = 0.0;
        for (var i = 0; i < samples.Length; i++)
        {
            if (i > 0)
            {
                north += (samples[i].Implied ?? samples[i].Reported ?? 0) * (samples[i].T - samples[i - 1].T);
            }

            points.Add(new TrackPoint(At(samples[i].T), Lat(north), OriginLon, samples[i].Reported, samples[i].Reported));
        }

        return points;
    }

    // ---- T17 ---------------------------------------------------------------------------------------------------

    [Fact]
    public void T17_fixes_at_36_m_s_for_42_s_then_30_m_s_are_one_event()
    {
        var track = Track(new Sample(0, 30), new Sample(42, 36), new Sample(84, 36), new Sample(126, 30), new Sample(168, 30));

        var episode = Assert.Single(TripSpeeds.SpeedingEpisodes(track));

        Assert.Equal(At(42), episode.StartUtc);
        Assert.Equal(At(84), episode.EndUtc);
        Assert.Equal(36.0, episode.PeakMps);
        Assert.Equal(track[1].Lat, episode.Lat);
        Assert.Equal(track[1].Lon, episode.Lon);
    }

    [Fact]
    public void T17_a_single_40_m_s_fix_between_30_m_s_neighbours_is_ignored()
    {
        // The neighbours imply 30 m/s: the fix is believed (30 >= 0.7 x 40), but one fix is never an event.
        var track = Track(new Sample(0, 30), new Sample(42, 30), new Sample(84, 40, Implied: 30), new Sample(126, 30), new Sample(168, 30));

        Assert.Empty(TripSpeeds.SpeedingEpisodes(track));
        Assert.Equal(40.0, TripSpeeds.Corroborated(track)[2]);
    }

    [Fact]
    public void A_reported_speed_that_the_neighbours_contradict_is_ignored_for_top_speed()
    {
        // 45 m/s reported, 30 m/s implied on both sides: 30 < 0.7 x 45 = 31.5, so the fix has no speed at all.
        var track = Track(new Sample(0, 30), new Sample(42, 30), new Sample(84, 45, Implied: 30), new Sample(126, 30), new Sample(168, 30));

        var corroborated = TripSpeeds.Corroborated(track);

        Assert.Null(corroborated[2]);
        Assert.Equal(30.0, corroborated[TripSpeeds.TopSpeedIndex(corroborated) ?? -1]);
    }

    [Fact]
    public void Two_fixes_that_are_not_corroborated_make_no_event()
    {
        // 40 m/s reported twice, but the positions imply 20 m/s.
        var track = Track(new Sample(0, 30), new Sample(42, 40, Implied: 20), new Sample(84, 40, Implied: 20), new Sample(126, 30));

        Assert.Empty(TripSpeeds.SpeedingEpisodes(track));
    }

    // ---- T21: the threshold is 80 mph = 35.7632 m/s -----------------------------------------------------------

    [Fact]
    public void T21_a_pair_at_exactly_80_mph_counts_and_a_pair_just_below_does_not()
    {
        Assert.Equal(35.7632, Threshold, 1e-9);

        Assert.Single(TripSpeeds.SpeedingEpisodes(Track(new Sample(0, Threshold), new Sample(42, Threshold))));
        Assert.Empty(TripSpeeds.SpeedingEpisodes(Track(new Sample(0, 35.76), new Sample(42, 35.76))));
        Assert.Empty(TripSpeeds.SpeedingEpisodes(Track(new Sample(0, 79.99 * FixParser.MphToMps), new Sample(42, 79.99 * FixParser.MphToMps))));

        // Dividing mph by 2.25 would give 35.556 for 80 mph and miss it: that is the bug this vector guards against.
        Assert.Empty(TripSpeeds.SpeedingEpisodes(Track(new Sample(0, 80 / 2.25), new Sample(42, 80 / 2.25))));
    }

    [Fact]
    public void T21_a_drive_with_life360_fixes_at_80_0_mph_has_one_speeding_event()
    {
        // Three fixes 42 s apart, parsed from Life360 states that say speed 80.0 (mph): 35.7632 m/s, 1 502.05 m apart.
        var fixes = Leave(0);
        var metresPerStep = Threshold * 42;
        for (var k = 1; k <= 3; k++)
        {
            fixes.Add(ParsedLife360(24 + (42 * k), 276 + (metresPerStep * k), mph: 80.0));
        }

        var stopT = 24 + (42 * 4);
        fixes.Add(Fix(stopT, 276 + (metresPerStep * 4), mps: 0));

        var result = Replay(fixes, stopT + 3600);

        var trip = Assert.Single(result.Closed);
        Assert.Equal(1, trip.SpeedingCount);
        var episode = Assert.Single(trip.SpeedingEpisodes);
        Assert.Equal(At(66), episode.StartUtc);
        Assert.Equal(At(150), episode.EndUtc);
        Assert.Equal(Threshold, trip.TopSpeedMps ?? double.NaN, 1e-9);
    }

    private static RawFix ParsedLife360(double t, double north, double mph)
    {
        var snapshot = new HaEntitySnapshot(
            "device_tracker.life360_alden",
            "not_home",
            new Dictionary<string, JsonElement>
            {
                ["latitude"] = JsonSerializer.SerializeToElement(Lat(north)),
                ["longitude"] = JsonSerializer.SerializeToElement(OriginLon),
                ["speed"] = JsonSerializer.SerializeToElement(mph),
                ["last_seen"] = JsonSerializer.SerializeToElement(At(t).ToString("o", CultureInfo.InvariantCulture)),
            },
            At(t),
            At(t));
        return FixParser.ParseTracker(snapshot, FixSource.Life360, At(1_000_000))
            ?? throw new InvalidOperationException("The Life360 state did not parse.");
    }

    // ---- the minimum duration -----------------------------------------------------------------------------------

    [Theory]
    [InlineData(29, 0)]
    [InlineData(30, 1)]
    public void An_episode_spans_at_least_30_s_from_its_first_to_its_last_fix(double spanS, int expectedEvents)
    {
        var track = Track(new Sample(0, 36), new Sample(spanS, 36));

        Assert.Equal(expectedEvents, TripSpeeds.SpeedingEpisodes(track).Count);
    }

    [Fact]
    public void With_the_27_s_cadence_of_an_android_phone_three_fixes_are_needed()
    {
        Assert.Empty(TripSpeeds.SpeedingEpisodes(Track(new Sample(0, 36), new Sample(27, 36))));

        var episode = Assert.Single(TripSpeeds.SpeedingEpisodes(Track(new Sample(0, 36), new Sample(27, 36), new Sample(54, 36))));
        Assert.Equal(At(0), episode.StartUtc);
        Assert.Equal(At(54), episode.EndUtc);
    }

    // ---- hysteresis and gaps ------------------------------------------------------------------------------------

    [Fact]
    public void A_dip_to_34_m_s_does_not_end_the_episode()
    {
        // 34 m/s is above threshold - 2.0 = 33.7632.
        var track = Track(new Sample(0, 36), new Sample(42, 36), new Sample(84, 34), new Sample(126, 36), new Sample(168, 30));

        var episode = Assert.Single(TripSpeeds.SpeedingEpisodes(track));

        Assert.Equal(At(0), episode.StartUtc);
        Assert.Equal(At(126), episode.EndUtc);
        Assert.Equal(36.0, episode.PeakMps);
    }

    [Fact]
    public void A_dip_to_33_m_s_ends_the_episode_and_the_next_one_counts_separately()
    {
        var track = Track(new Sample(0, 36), new Sample(42, 36), new Sample(84, 33), new Sample(126, 36), new Sample(168, 36));

        var episodes = TripSpeeds.SpeedingEpisodes(track);

        Assert.Equal(2, episodes.Count);
        Assert.Equal(At(42), episodes[0].EndUtc);
        Assert.Equal(At(126), episodes[1].StartUtc);
        Assert.Equal(At(168), episodes[1].EndUtc);
    }

    [Fact]
    public void The_fast_fixes_between_slower_ones_do_not_join_into_one_event()
    {
        // 36, 30, 36: each fast fix is alone.
        var track = Track(new Sample(0, 36), new Sample(42, 30), new Sample(84, 36), new Sample(126, 30));

        Assert.Empty(TripSpeeds.SpeedingEpisodes(track));
    }

    [Theory]
    [InlineData(110, 1)]
    [InlineData(125, 2)]
    public void A_gap_of_more_than_120_s_ends_the_episode(double gapS, int expectedEvents)
    {
        var track = Track(new Sample(0, 36), new Sample(42, 36), new Sample(42 + gapS, 36), new Sample(84 + gapS, 36));

        Assert.Equal(expectedEvents, TripSpeeds.SpeedingEpisodes(track).Count);
    }

    // ---- which speeds count -------------------------------------------------------------------------------------

    [Fact]
    public void A_fix_without_a_reported_speed_uses_the_speed_implied_by_its_trailing_segment()
    {
        var track = Track(new Sample(0, 30), new Sample(42, null, Implied: 37), new Sample(84, null, Implied: 37));

        var corroborated = TripSpeeds.Corroborated(track);

        Assert.Equal(37.0, corroborated[1] ?? double.NaN, 1e-6);
        Assert.Equal(37.0, corroborated[2] ?? double.NaN, 1e-6);
        Assert.Single(TripSpeeds.SpeedingEpisodes(track));
    }

    [Fact]
    public void A_segment_longer_than_120_s_or_faster_than_54_m_s_corroborates_nothing()
    {
        var gap = Track(new Sample(0, 36), new Sample(200, 36));
        var corroboratedAcrossGap = TripSpeeds.Corroborated(gap);
        Assert.Null(corroboratedAcrossGap[0]);
        Assert.Null(corroboratedAcrossGap[1]);
        Assert.Null(TripSpeeds.TopSpeedIndex(corroboratedAcrossGap));

        var jump = Track(new Sample(0, 30), new Sample(42, 60, Implied: 100));
        Assert.Null(TripSpeeds.Corroborated(jump)[1]);
    }

    [Fact]
    public void The_top_speed_is_the_highest_corroborated_speed_and_a_tie_goes_to_the_earlier_fix()
    {
        var track = Track(new Sample(0, 20), new Sample(42, 31), new Sample(84, 25), new Sample(126, 31), new Sample(168, 22));

        var corroborated = TripSpeeds.Corroborated(track);

        Assert.Equal(1, TripSpeeds.TopSpeedIndex(corroborated));
        Assert.Null(TripSpeeds.TopSpeedIndex([]));
    }

    [Fact]
    public void An_empty_or_single_point_track_has_no_episodes_and_no_speeds()
    {
        Assert.Empty(TripSpeeds.SpeedingEpisodes([]));
        Assert.Empty(TripSpeeds.SpeedingEpisodes(Track(new Sample(0, 40))));
        Assert.Empty(TripSpeeds.Corroborated([]));
    }

    // ---- through the detector -----------------------------------------------------------------------------------

    [Theory]
    [InlineData(36.0, 1)]
    [InlineData(35.7, 0)]   // dense, below the threshold: a measured zero, not unknown
    public void A_dense_trip_counts_its_speeding_episodes_at_the_threshold(double mps, int expectedCount)
    {
        var fixes = Leave(0);
        fixes.AddRange(Cruise(24, 276, mps, 42, 5));
        var stopT = 24 + (42 * 6);
        fixes.Add(Fix(stopT, 276 + (mps * 42 * 6), mps: 0));

        var result = Replay(fixes, stopT + 3600);

        var trip = Assert.Single(result.Closed);
        Assert.Equal(expectedCount, trip.SpeedingCount);
        Assert.Equal(expectedCount, trip.SpeedingEpisodes.Count);
        Assert.Equal(mps, trip.TopSpeedMps ?? double.NaN, 1e-9);
    }
}
