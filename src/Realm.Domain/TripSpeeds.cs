namespace Realm.Domain;

/// <summary>
/// Speeds of a trip track (02 section 5.8): which reported speeds are corroborated by the positions, the top
/// speed, and the sampled speeding episodes. Pure functions of the track.
/// </summary>
public static class TripSpeeds
{
    // A segment is usable for speeds only when its fixes are at most this far apart in time.
    private const double MaxSegmentGapS = 120;

    // A reported speed counts only if an adjacent segment's implied speed is at least this share of it.
    private const double CorroborationShare = 0.7;

    /// <summary>
    /// The corroborated speed of each track fix, or null when the fix has none. A reported speed counts only if
    /// at least one adjacent segment (at most 120 s long) has an implied speed of at least 0.7 times it; a fix
    /// without a reported speed uses the implied speed of its trailing segment. A segment implied faster than
    /// the plausible maximum is never used.
    /// </summary>
    public static IReadOnlyList<double?> Corroborated(IReadOnlyList<TrackPoint> track, TripOptions? options = null)
    {
        var maxPlausible = (options ?? new TripOptions()).MaxPlausibleSpeedMps;

        // implied[i] is the speed of the segment from point i-1 to point i; null when it is unusable.
        var implied = new double?[track.Count];
        for (var i = 1; i < track.Count; i++)
        {
            var seconds = (track[i].Ts - track[i - 1].Ts).TotalSeconds;
            if (seconds > 0 && seconds <= MaxSegmentGapS)
            {
                var speed = Geo.DistanceM(track[i - 1].Lat, track[i - 1].Lon, track[i].Lat, track[i].Lon) / seconds;
                implied[i] = speed <= maxPlausible ? speed : null;
            }
        }

        var result = new double?[track.Count];
        for (var i = 0; i < track.Count; i++)
        {
            if (track[i].ReportedSpeedMps is { } reported)
            {
                var floor = CorroborationShare * reported;
                var before = implied[i];
                var after = i + 1 < track.Count ? implied[i + 1] : null;
                result[i] = (before >= floor) || (after >= floor) ? reported : null;
            }
            else
            {
                result[i] = implied[i];
            }
        }

        return result;
    }

    /// <summary>
    /// The index of the highest corroborated speed, or null when there is none. A tie goes to the earlier fix.
    /// </summary>
    public static int? TopSpeedIndex(IReadOnlyList<double?> corroborated)
    {
        int? best = null;
        for (var i = 0; i < corroborated.Count; i++)
        {
            if (corroborated[i] is { } speed && (best is null || speed > corroborated[best.Value]))
            {
                best = i;
            }
        }

        return best;
    }

    /// <summary>
    /// The sampled speeding episodes of a track: a maximal run of at least two consecutive corroborated fixes
    /// (at most 120 s apart) at or above the threshold, spanning at least the minimum time. The episode ends at
    /// a fix below the threshold minus the re-arm margin, or at a gap of more than 120 s. A lone fast fix never
    /// counts, so the count is a floor.
    /// </summary>
    public static IReadOnlyList<SpeedingEpisode> SpeedingEpisodes(
        IReadOnlyList<TrackPoint> track,
        DrivingOptions? driving = null,
        TripOptions? trips = null)
    {
        var d = driving ?? new DrivingOptions();
        var corroborated = Corroborated(track, trips);
        var episodes = new List<SpeedingEpisode>();

        var run = new List<int>();   // consecutive corroborated fixes at or above the threshold, before an episode starts
        var inEpisode = false;
        var first = 0;
        var last = 0;
        var peak = 0.0;
        int? previous = null;

        void EndEpisode()
        {
            episodes.Add(new SpeedingEpisode(track[first].Ts, track[last].Ts, peak, track[first].Lat, track[first].Lon));
            inEpisode = false;
        }

        for (var i = 0; i < track.Count; i++)
        {
            if (corroborated[i] is not { } speed)
            {
                continue;
            }

            if (previous is { } p && (track[i].Ts - track[p].Ts).TotalSeconds > MaxSegmentGapS)
            {
                if (inEpisode)
                {
                    EndEpisode();
                }

                run.Clear();
            }

            previous = i;
            if (inEpisode)
            {
                if (speed >= d.SpeedingMps - d.SpeedingRearmMps)
                {
                    last = i;
                    peak = Math.Max(peak, speed);
                }
                else
                {
                    EndEpisode();
                    run.Clear();
                }

                continue;
            }

            if (speed < d.SpeedingMps)
            {
                run.Clear();
                continue;
            }

            run.Add(i);
            if (run.Count >= 2 && (track[i].Ts - track[run[0]].Ts).TotalSeconds >= d.SpeedingMinS)
            {
                inEpisode = true;
                first = run[0];
                last = i;
                peak = run.Max(k => corroborated[k]!.Value);
                run.Clear();
            }
        }

        if (inEpisode)
        {
            EndEpisode();
        }

        return episodes;
    }
}
