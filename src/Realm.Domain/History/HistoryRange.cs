namespace Realm.Domain;

/// <summary>
/// Builds the days of Location History from stored fixes and trips (0.3.0, D123). The caller reads one set of fixes and trips for the whole range, with a margin of a day either side, and
/// this slices them per day (a binary search each), so a range costs two queries and not two per day. Pure: Live and Demo both use it.
/// </summary>
public static class HistoryRange
{
    /// <summary>How far outside a day the fixes and trips are read, so a visit that spans midnight is seen whole.</summary>
    public static readonly TimeSpan Margin = TimeSpan.FromDays(1);

    /// <summary>The window to read for the days <paramref name="from"/> to <paramref name="to"/> (inclusive): their bounds, widened by <see cref="Margin"/>.</summary>
    public static TimeWindow ReadWindow(DateOnly from, DateOnly to, TimeZoneInfo zone) =>
        new(HistoryDayMath.Bounds(from, zone).StartUtc - Margin, HistoryDayMath.Bounds(to, zone).EndUtc + Margin);

    /// <summary>
    /// The days <paramref name="from"/> to <paramref name="to"/> (inclusive), newest day first. <paramref name="fixes"/> and <paramref name="trips"/> are what was read for
    /// <see cref="ReadWindow"/>, in any order. <paramref name="movement"/> says the trips are the moves of a tracker (<see cref="MovementDeriver"/>).
    /// </summary>
    public static IReadOnlyList<HistoryDayVm> Build(
        string memberId,
        DateOnly from,
        DateOnly to,
        TimeZoneInfo zone,
        IReadOnlyList<RawFix> fixes,
        IReadOnlyList<StatsTrip> trips,
        IReadOnlyList<RawPlace> zones,
        DateTimeOffset now,
        bool includeTrail,
        bool movement = false)
    {
        ArgumentNullException.ThrowIfNull(zone);
        var sortedFixes = fixes.OrderBy(fix => fix.Ts).ToList();
        var sortedTrips = trips.OrderBy(trip => trip.StartUtc).ToList();
        var days = new List<HistoryDayVm>();
        for (var day = to; day >= from; day = day.AddDays(-1))
        {
            var bounds = HistoryDayMath.Bounds(day, zone);
            var lo = bounds.StartUtc - Margin;
            var hi = bounds.EndUtc + Margin;
            days.Add(HistoryAssembler.Build(new HistoryInput(
                memberId,
                day,
                zone,
                Slice(sortedFixes, fix => fix.Ts, lo, hi),
                Slice(sortedTrips, trip => trip.StartUtc, lo, hi),
                zones,
                now,
                includeTrail,
                movement)));
        }

        return days;
    }

    // The items of a list sorted by time whose time is in [lo, hi): a binary search for the first, then a walk.
    private static List<T> Slice<T>(List<T> sorted, Func<T, DateTimeOffset> time, DateTimeOffset lo, DateTimeOffset hi)
    {
        var low = 0;
        var high = sorted.Count;
        while (low < high)
        {
            var middle = (low + high) / 2;
            if (time(sorted[middle]) < lo)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        var result = new List<T>();
        for (var i = low; i < sorted.Count && time(sorted[i]) < hi; i++)
        {
            result.Add(sorted[i]);
        }

        return result;
    }
}
