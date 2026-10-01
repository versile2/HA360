namespace Realm.Domain;

/// <summary>
/// Staleness rules for members and vehicles. Pure: every instant and threshold is a parameter, no clock is read.
/// </summary>
public static class FreshnessRules
{
    /// <summary>Heartbeat assumed when none can be observed.</summary>
    public const int DefaultHeartbeatMinutes = 30;

    /// <summary>Only gaps longer than this count as a stationary heartbeat, and the heartbeat is never below it.</summary>
    public const int MinHeartbeatMinutes = 5;

    /// <summary>Default of the option fusion_stale_grace_minutes.</summary>
    public const int DefaultGraceMinutes = 10;

    /// <summary>
    /// The member's stationary heartbeat: per source, the median of the gaps between consecutive fixes that
    /// are longer than 5 minutes; the largest of those medians, capped at twice the configured stale
    /// threshold. 30 minutes when no source has such a gap.
    /// </summary>
    /// <param name="fixTimesBySource">Stored fix times (in any order) of each of the member's live sources, normally the last 24 hours.</param>
    public static TimeSpan Heartbeat(IEnumerable<IReadOnlyList<DateTimeOffset>> fixTimesBySource, int uiStaleAfterMinutes)
    {
        var minimum = TimeSpan.FromMinutes(MinHeartbeatMinutes);
        TimeSpan? largest = null;
        foreach (var fixTimes in fixTimesBySource)
        {
            var ordered = fixTimes.Order().ToList();
            var gaps = new List<TimeSpan>();
            for (var i = 1; i < ordered.Count; i++)
            {
                var gap = ordered[i] - ordered[i - 1];
                if (gap > minimum)
                {
                    gaps.Add(gap);
                }
            }

            if (gaps.Count == 0)
            {
                continue;
            }

            gaps.Sort();
            var middle = gaps.Count / 2;
            var median = gaps.Count % 2 == 1
                ? gaps[middle]
                : TimeSpan.FromTicks((gaps[middle - 1].Ticks + gaps[middle].Ticks) / 2);
            if (largest is null || median > largest)
            {
                largest = median;
            }
        }

        if (largest is not { } observed)
        {
            return TimeSpan.FromMinutes(DefaultHeartbeatMinutes);
        }

        // Never below the 5-minute minimum: only gaps longer than that were counted.
        var maximum = TimeSpan.FromMinutes(2 * uiStaleAfterMinutes);
        return observed > maximum ? maximum : observed;
    }

    /// <summary>
    /// A member is Stale after max(configured threshold, heartbeat + grace): the configured threshold is a
    /// floor, so a healthy stationary phone is not Stale at every heartbeat.
    /// </summary>
    public static TimeSpan StaleAfter(int uiStaleAfterMinutes, TimeSpan heartbeat, int graceMinutes = DefaultGraceMinutes)
    {
        var floor = TimeSpan.FromMinutes(uiStaleAfterMinutes);
        var heartbeatBased = heartbeat + TimeSpan.FromMinutes(graceMinutes);
        return heartbeatBased > floor ? heartbeatBased : floor;
    }

    /// <summary>
    /// Static members are always Static. Otherwise NoFix if the member never reported, Offline if the last
    /// fix is older than <paramref name="offlineAfterHours"/>, Stale if older than <paramref name="staleAfter"/>, else Fresh.
    /// </summary>
    /// <param name="lastFixUtc">The time of the winning fix; null if the member never reported.</param>
    public static Freshness ForMember(MemberKind kind, DateTimeOffset now, DateTimeOffset? lastFixUtc, TimeSpan staleAfter, int offlineAfterHours)
    {
        if (kind == MemberKind.Static)
        {
            return Freshness.Static;
        }

        if (lastFixUtc is not { } lastFix)
        {
            return Freshness.NoFix;
        }

        var age = now - lastFix;
        if (age > TimeSpan.FromHours(offlineAfterHours))
        {
            return Freshness.Offline;
        }

        return age > staleAfter ? Freshness.Stale : Freshness.Fresh;
    }

    /// <summary>NoFix if the vehicle has no update time, Stale if its last update is older than the vehicle threshold, else Fresh.</summary>
    public static Freshness ForVehicle(DateTimeOffset now, DateTimeOffset? lastUpdateUtc, int vehicleStaleAfterMinutes)
    {
        if (lastUpdateUtc is not { } lastUpdate)
        {
            return Freshness.NoFix;
        }

        return now - lastUpdate > TimeSpan.FromMinutes(vehicleStaleAfterMinutes) ? Freshness.Stale : Freshness.Fresh;
    }
}
