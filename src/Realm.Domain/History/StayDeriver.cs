namespace Realm.Domain;

/// <summary>
/// The thresholds of <see cref="StayDeriver"/> (0.3.0, D123). The defaults are the rules of the product: a place visit is at least 5 minutes at one zone, or within 100 m of one spot
/// when there is no zone.
/// </summary>
public sealed record StayOptions
{
    /// <summary>A stop shorter than this is not a visit (a red light, a pass through a zone, a drop-off at the kerb).</summary>
    public TimeSpan MinDuration { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>Fixes within this distance of the middle of the visit so far belong to it when they are at no zone.</summary>
    public double ClusterRadiusM { get; init; } = 100;

    /// <summary>A fix with a worse accuracy than this says nothing about where the member is, so it is left out (the same figure as <see cref="PlaceResolver.DecisionMaxAccuracyM"/>).</summary>
    public double MaxFixAccuracyM { get; init; } = PlaceResolver.DecisionMaxAccuracyM;

    /// <summary>
    /// A zone with a radius above this is a region (an arrival circle), not a place: it neither makes nor names a visit, so a stop inside it is named by its street or city.
    /// </summary>
    public double MaxZoneRadiusM { get; init; } = 2000;

    /// <summary>A drive that ends at a visit (or starts from one) moves the visit's edge to the drive's end (or start) when it is no more than this far from the visit's nearest fix.</summary>
    public TimeSpan TripBridgeMax { get; init; } = TimeSpan.FromHours(36);

    /// <summary>How near to a spot (not a zone) a drive must start or end to be the drive that arrives at or leaves it.</summary>
    public double TripSnapM { get; init; } = 150;

    /// <summary>The last visit of the stored data is going on now when its last fix is no older than this.</summary>
    public TimeSpan OngoingMax { get; init; } = TimeSpan.FromHours(3);

    /// <summary>The defaults above.</summary>
    public static StayOptions Default { get; } = new();
}

/// <summary>A place visit as the fixes and the drives say it, before it is clipped to a day or named.</summary>
/// <param name="PlaceId">The zone it was at; null at no zone.</param>
/// <param name="Lat">The zone's centre, or the middle of the fixes.</param>
/// <param name="Address">The newest stored address among its fixes; null when none carries one.</param>
/// <param name="IsOngoing">The member is still there: <paramref name="EndUtc"/> is the clock.</param>
public sealed record DerivedStay(
    string? PlaceId,
    double Lat,
    double Lon,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    int FixCount,
    string? Address,
    bool IsOngoing)
{
    /// <summary>The length of the visit.</summary>
    public TimeSpan Duration => EndUtc - StartUtc;
}

/// <summary>
/// Place visits ("stays") from stored fixes, the zones and the drives (0.3.0, D123; 02 section 6.9). Pure: no clock, no database.
/// <list type="number">
/// <item>The fixes that fall inside a drive are not part of any visit, and neither are fixes with a worse accuracy than <see cref="StayOptions.MaxFixAccuracyM"/>.</item>
/// <item>Consecutive fixes go together while they stay at the same zone (by <see cref="PlaceResolver"/>, with its exit margin), or, at no zone, within <see cref="StayOptions.ClusterRadiusM"/>
/// of the middle of the group; a drive between two fixes always starts a new visit.</item>
/// <item>A group shorter than the minimum that sits between two groups at the same place is noise (one stray fix) and is absorbed.</item>
/// <item>A drive that ends near the visit moves its start back to the end of that drive, and one that starts near it moves its end to the start of that drive: a phone that reports every
/// 30 minutes (or not at all while it stands still) still gives the arrival and the departure the Realm saw.</item>
/// <item>The last visit of the data is "ongoing" when the clock is not far past its last fix.</item>
/// <item>Visits shorter than the minimum are dropped.</item>
/// </list>
/// </summary>
public static class StayDeriver
{
    /// <summary>
    /// The visits in time order. <paramref name="fixes"/> and <paramref name="trips"/> need not be sorted; <paramref name="zones"/> are the places that exist now.
    /// </summary>
    /// <param name="now">The clock; null derives no ongoing visit.</param>
    public static IReadOnlyList<DerivedStay> Derive(
        IReadOnlyList<RawFix> fixes,
        IReadOnlyList<StatsTrip> trips,
        IReadOnlyList<RawPlace> zones,
        DateTimeOffset? now = null,
        StayOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(fixes);
        ArgumentNullException.ThrowIfNull(trips);
        ArgumentNullException.ThrowIfNull(zones);
        var opt = options ?? StayOptions.Default;
        var places = zones.Where(zone => zone.RadiusM > 0 && zone.RadiusM <= opt.MaxZoneRadiusM).ToList();
        var placeById = places.GroupBy(zone => zone.Id, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var drives = Merge([.. trips.OrderBy(trip => trip.StartUtc)]);
        var ordered = fixes.OrderBy(fix => fix.Ts).ToList();

        var groups = GroupFixes(ordered, drives, places, opt);
        Absorb(groups, drives, opt);
        var sorted = groups.OrderBy(group => group.First).ToList();
        var byStart = trips.OrderBy(trip => trip.StartUtc).ToList();

        var result = new List<DerivedStay>();
        for (var i = 0; i < sorted.Count; i++)
        {
            var group = sorted[i];
            var start = group.First;
            var end = group.Last;
            var previousLast = i > 0 ? sorted[i - 1].Last : (DateTimeOffset?)null;
            var nextFirst = i < sorted.Count - 1 ? sorted[i + 1].First : (DateTimeOffset?)null;

            var arrived = byStart
                .Where(trip => trip.EndUtc <= group.First && (previousLast is null || trip.EndUtc > previousLast.Value))
                .MaxBy(trip => trip.EndUtc);
            if (arrived is not null && group.First - arrived.EndUtc <= opt.TripBridgeMax && IsNear(arrived.EndPlaceId, arrived.EndLat, arrived.EndLon, group, placeById, opt))
            {
                start = arrived.EndUtc;
            }

            var left = byStart
                .Where(trip => trip.StartUtc >= group.Last && (nextFirst is null || trip.StartUtc < nextFirst.Value))
                .MinBy(trip => trip.StartUtc);
            if (left is not null && left.StartUtc - group.Last <= opt.TripBridgeMax && IsNear(left.StartPlaceId, left.StartLat, left.StartLon, group, placeById, opt))
            {
                end = left.StartUtc;
            }

            var ongoing = false;
            if (left is null && i == sorted.Count - 1 && now is { } clock && clock >= end && clock - end <= opt.OngoingMax)
            {
                end = clock;
                ongoing = true;
            }

            if (end - start < opt.MinDuration)
            {
                continue;
            }

            var zone = group.PlaceId is not null && placeById.TryGetValue(group.PlaceId, out var found) ? found : null;
            result.Add(new DerivedStay(
                group.PlaceId,
                zone?.Lat ?? group.Lat,
                zone?.Lon ?? group.Lon,
                start,
                end,
                group.Count,
                group.Address,
                ongoing));
        }

        return result;
    }

    // ---- 1 and 2: the fixes outside the drives, grouped --------------------------------------------------------------------------------------

    private static List<Group> GroupFixes(IReadOnlyList<RawFix> ordered, IReadOnlyList<(DateTimeOffset Start, DateTimeOffset End)> drives, IReadOnlyList<RawPlace> places, StayOptions opt)
    {
        var groups = new List<Group>();
        Group? current = null;
        RawFix? previous = null;
        IReadOnlyList<string> inside = [];
        foreach (var fix in ordered)
        {
            if (InsideDrive(drives, fix.Ts) || (fix.AccuracyM is { } accuracy && accuracy > opt.MaxFixAccuracyM))
            {
                continue;
            }

            var left = previous is not null && DriveBetween(drives, previous.Ts, fix.Ts);
            if (left)
            {
                inside = [];
            }

            var membership = PlaceResolver.Resolve(fix.Lat, fix.Lon, fix.AccuracyM, places, inside);
            inside = membership.ZoneIds;
            var placeId = membership.PlaceId;

            if (current is not null && !left && Joins(current, fix, placeId, opt))
            {
                current.Add(fix);
            }
            else
            {
                current = new Group(placeId, fix);
                groups.Add(current);
            }

            previous = fix;
        }

        return groups;
    }

    private static bool Joins(Group group, RawFix fix, string? placeId, StayOptions opt)
    {
        if (placeId is not null || group.PlaceId is not null)
        {
            return placeId is not null && string.Equals(placeId, group.PlaceId, StringComparison.Ordinal);
        }

        var reach = Math.Max(opt.ClusterRadiusM, Math.Min(fix.AccuracyM ?? 0, opt.MaxFixAccuracyM));
        return Geo.DistanceM(fix.Lat, fix.Lon, group.Lat, group.Lon) <= reach;
    }

    // ---- 3: one stray fix between two groups at the same place -------------------------------------------------------------------------------

    private static void Absorb(List<Group> groups, IReadOnlyList<(DateTimeOffset Start, DateTimeOffset End)> drives, StayOptions opt)
    {
        var again = true;
        while (again)
        {
            again = false;
            for (var j = 1; j < groups.Count - 1; j++)
            {
                var middle = groups[j];
                if (middle.Last - middle.First >= opt.MinDuration)
                {
                    continue;
                }

                var before = groups[j - 1];
                var after = groups[j + 1];
                if (!SamePlace(before, after, opt) || DriveBetween(drives, before.Last, after.First))
                {
                    continue;
                }

                before.Merge(middle);
                before.Merge(after);
                groups.RemoveRange(j, 2);
                again = true;
                break;
            }
        }
    }

    private static bool SamePlace(Group a, Group b, StayOptions opt) =>
        a.PlaceId is not null || b.PlaceId is not null
            ? a.PlaceId is not null && string.Equals(a.PlaceId, b.PlaceId, StringComparison.Ordinal)
            : Geo.DistanceM(a.Lat, a.Lon, b.Lat, b.Lon) <= opt.ClusterRadiusM;

    // ---- 4: does the end of a drive belong to this visit -------------------------------------------------------------------------------------

    private static bool IsNear(string? placeId, double? lat, double? lon, Group group, IReadOnlyDictionary<string, RawPlace> placeById, StayOptions opt)
    {
        if (group.PlaceId is { } zoneId && placeById.TryGetValue(zoneId, out var zone))
        {
            if (string.Equals(placeId, zoneId, StringComparison.Ordinal))
            {
                return true;
            }

            return lat is { } la && lon is { } lo && Geo.DistanceM(la, lo, zone.Lat, zone.Lon) <= zone.RadiusM + PlaceResolver.EndpointSnapM;
        }

        return lat is { } latitude && lon is { } longitude && Geo.DistanceM(latitude, longitude, group.Lat, group.Lon) <= opt.TripSnapM;
    }

    // ---- the drives as non-overlapping intervals ---------------------------------------------------------------------------------------------

    private static List<(DateTimeOffset Start, DateTimeOffset End)> Merge(IReadOnlyList<StatsTrip> sortedByStart)
    {
        var merged = new List<(DateTimeOffset Start, DateTimeOffset End)>();
        foreach (var trip in sortedByStart)
        {
            if (merged.Count > 0 && trip.StartUtc <= merged[^1].End)
            {
                merged[^1] = (merged[^1].Start, trip.EndUtc > merged[^1].End ? trip.EndUtc : merged[^1].End);
            }
            else
            {
                merged.Add((trip.StartUtc, trip.EndUtc));
            }
        }

        return merged;
    }

    private static bool InsideDrive(IReadOnlyList<(DateTimeOffset Start, DateTimeOffset End)> drives, DateTimeOffset at)
    {
        foreach (var (start, end) in drives)
        {
            if (start > at)
            {
                return false;
            }

            if (at <= end)
            {
                return true;
            }
        }

        return false;
    }

    // A drive that lies after a and before b (the fixes a and b are both outside every drive).
    private static bool DriveBetween(IReadOnlyList<(DateTimeOffset Start, DateTimeOffset End)> drives, DateTimeOffset a, DateTimeOffset b)
    {
        foreach (var (start, end) in drives)
        {
            if (start >= b)
            {
                return false;
            }

            if (end > a)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>A run of fixes at one place, with the running middle of them.</summary>
    private sealed class Group
    {
        private double _sumLat;
        private double _sumLon;

        public Group(string? placeId, RawFix first)
        {
            PlaceId = placeId;
            First = first.Ts;
            Last = first.Ts;
            Add(first);
        }

        public string? PlaceId { get; }

        public DateTimeOffset First { get; private set; }

        public DateTimeOffset Last { get; private set; }

        public int Count { get; private set; }

        public string? Address { get; private set; }

        private DateTimeOffset _addressAt = DateTimeOffset.MinValue;

        public double Lat => _sumLat / Count;

        public double Lon => _sumLon / Count;

        public void Add(RawFix fix)
        {
            _sumLat += fix.Lat;
            _sumLon += fix.Lon;
            Count++;
            if (fix.Ts < First)
            {
                First = fix.Ts;
            }

            if (fix.Ts > Last)
            {
                Last = fix.Ts;
            }

            if (!string.IsNullOrWhiteSpace(fix.Address) && fix.Ts >= _addressAt)
            {
                Address = fix.Address;
                _addressAt = fix.Ts;
            }
        }

        public void Merge(Group other)
        {
            _sumLat += other._sumLat;
            _sumLon += other._sumLon;
            Count += other.Count;
            if (other.First < First)
            {
                First = other.First;
            }

            if (other.Last > Last)
            {
                Last = other.Last;
            }

            if (other.Address is not null && other._addressAt >= _addressAt)
            {
                Address = other.Address;
                _addressAt = other._addressAt;
            }
        }
    }
}
