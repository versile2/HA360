using System.Globalization;

namespace Realm.Domain;

/// <summary>What <see cref="HistoryAssembler"/> needs to build one member's day.</summary>
/// <param name="Fixes">The member's stored fixes around the day: the day's own, and the newest one before it and the first one after it (so a visit that spans midnight is seen whole). Any order.</param>
/// <param name="Trips">The member's stored trips around the day (a day either side is enough), so the drives that arrive at or leave a visit are known. Any order.</param>
/// <param name="Zones">The places that exist now (their display names).</param>
/// <param name="Now">The clock: the last visit of the data is "ongoing" when this is not far past its last fix.</param>
/// <param name="IncludeTrail">False for a day that is only listed (the range view), which saves building the path.</param>
public sealed record HistoryInput(
    string MemberId,
    DateOnly Day,
    TimeZoneInfo Zone,
    IReadOnlyList<RawFix> Fixes,
    IReadOnlyList<StatsTrip> Trips,
    IReadOnlyList<RawPlace> Zones,
    DateTimeOffset Now,
    bool IncludeTrail = true);

/// <summary>
/// Builds a member's day (0.3.0, D123): the visits of <see cref="StayDeriver"/> clipped to the local day, the drives that started in it, the trail of the drives and the two ends of the day.
/// Pure: the caller reads the stored fixes and trips (bounded by the day, with a margin) and gives the clock.
/// </summary>
public static class HistoryAssembler
{
    /// <summary>Fixes of a drive no further apart than this are joined by a solid line; further apart they are joined by a dashed one (a straight guess).</summary>
    public static readonly TimeSpan SolidMax = TimeSpan.FromMinutes(5);

    /// <summary>Fixes further apart than this are not joined at all: the trail has a gap.</summary>
    public static readonly TimeSpan GapBreak = TimeSpan.FromMinutes(60);

    /// <summary>A trail point closer than this to the last one kept is dropped (01 section 13: points closer than 15 m are thinned).</summary>
    public const double ThinM = 15;

    private static readonly TimeSpan KeepEvery = TimeSpan.FromSeconds(150);
    private static readonly TimeSpan AddressMaxAge = TimeSpan.FromMinutes(30);
    private const double AddressMaxDistanceM = 1000;

    /// <summary>The day of <paramref name="input"/>.</summary>
    public static HistoryDayVm Build(HistoryInput input, StayOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        var opt = options ?? StayOptions.Default;
        var bounds = HistoryDayMath.Bounds(input.Day, input.Zone);
        var fixes = input.Fixes.OrderBy(fix => fix.Ts).ToList();
        var places = input.Zones.Where(zone => zone.RadiusM > 0 && zone.RadiusM <= opt.MaxZoneRadiusM).ToList();
        var labelZones = places.Select(zone => new LabelZone(zone.Id, zone.Name, zone.Lat, zone.Lon)).ToList();
        var nameOf = places.GroupBy(zone => zone.Id, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First().Name, StringComparer.Ordinal);

        var entries = new List<HistoryEntry>();

        foreach (var stay in StayDeriver.Derive(fixes, input.Trips, input.Zones, input.Now, opt))
        {
            if (stay.EndUtc <= bounds.StartUtc || stay.StartUtc >= bounds.EndUtc)
            {
                continue;
            }

            var start = stay.StartUtc < bounds.StartUtc ? bounds.StartUtc : stay.StartUtc;
            var end = stay.EndUtc > bounds.EndUtc ? bounds.EndUtc : stay.EndUtc;
            if (end <= start)
            {
                continue;
            }

            var continuesInto = stay.EndUtc > bounds.EndUtc;
            var name = stay.PlaceId is not null && nameOf.TryGetValue(stay.PlaceId, out var zoneName) ? zoneName : null;
            entries.Add(new HistoryStay(
                Id: "s-" + stay.StartUtc.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture),
                StartUtc: start,
                EndUtc: end,
                Label: PlaceLabeler.Name(name, null, stay.Address, stay.Lat, stay.Lon, labelZones),
                PlaceId: stay.PlaceId,
                Lat: stay.Lat,
                Lon: stay.Lon,
                ContinuesFromPreviousDay: stay.StartUtc < bounds.StartUtc,
                ContinuesIntoNextDay: continuesInto,
                IsOngoing: stay.IsOngoing && !continuesInto));
        }

        var withAddress = fixes.Where(fix => !string.IsNullOrWhiteSpace(fix.Address)).ToList();
        var labeler = new PlaceLabeler(labelZones, (trip, end) => AddressNear(withAddress, trip, end));
        var dayTrips = input.Trips
            .Where(trip => trip.StartUtc >= bounds.StartUtc && trip.StartUtc < bounds.EndUtc)
            .OrderBy(trip => trip.StartUtc)
            .ToList();
        var trail = new List<HistoryTrailSegment>();
        foreach (var trip in dayTrips)
        {
            var id = "d-" + trip.StartUtc.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
            entries.Add(new HistoryDrive(
                Id: id,
                StartUtc: trip.StartUtc,
                EndUtc: trip.EndUtc,
                FromLabel: labeler.Label(trip, end: false),
                ToLabel: labeler.Label(trip, end: true),
                Meters: trip.Meters,
                TopSpeedMps: trip.TopSpeedMps,
                SpeedingCount: trip.SpeedingCount,
                PhoneCount: trip.PhoneCount,
                Coarse: trip.Quality == TripQuality.Coarse,
                StartLat: trip.StartLat,
                StartLon: trip.StartLon,
                EndLat: trip.EndLat,
                EndLon: trip.EndLon));
            if (input.IncludeTrail)
            {
                trail.AddRange(Segments(id, trip, fixes));
            }
        }

        var ordered = entries.OrderBy(entry => entry.StartUtc).ThenBy(entry => entry is HistoryDrive ? 1 : 0).ToList();
        var recorded = ordered.Count > 0 || fixes.Any(fix => fix.Ts >= bounds.StartUtc && fix.Ts < bounds.EndUtc);
        return new HistoryDayVm(
            input.MemberId,
            input.Day,
            bounds.StartUtc,
            bounds.EndUtc,
            ordered,
            trail,
            recorded,
            ordered.Count == 0 ? null : StartOf(ordered[0], trail),
            ordered.Count == 0 ? null : EndOf(ordered[^1], trail));
    }

    // ---- the two ends of the day ---------------------------------------------------------------------------------------------------------------

    private static HistoryEndPoint? StartOf(HistoryEntry first, IReadOnlyList<HistoryTrailSegment> trail) =>
        first switch
        {
            HistoryStay stay => new HistoryEndPoint(stay.Lat, stay.Lon, stay.StartUtc),
            HistoryDrive { StartLat: { } lat, StartLon: { } lon } drive => new HistoryEndPoint(lat, lon, drive.StartUtc),
            HistoryDrive drive => trail.FirstOrDefault(segment => segment.EntryId == drive.Id) is { } segment ? new HistoryEndPoint(segment.Points[0][1], segment.Points[0][0], drive.StartUtc) : null,
            _ => null,
        };

    private static HistoryEndPoint? EndOf(HistoryEntry last, IReadOnlyList<HistoryTrailSegment> trail) =>
        last switch
        {
            HistoryStay stay => new HistoryEndPoint(stay.Lat, stay.Lon, stay.EndUtc),
            HistoryDrive { EndLat: { } lat, EndLon: { } lon } drive => new HistoryEndPoint(lat, lon, drive.EndUtc),
            HistoryDrive drive => trail.LastOrDefault(segment => segment.EntryId == drive.Id) is { } segment ? new HistoryEndPoint(segment.Points[^1][1], segment.Points[^1][0], drive.EndUtc) : null,
            _ => null,
        };

    // ---- the trail of one drive ----------------------------------------------------------------------------------------------------------------

    private static List<HistoryTrailSegment> Segments(string entryId, StatsTrip trip, IReadOnlyList<RawFix> sortedFixes)
    {
        var points = Thin(Within(sortedFixes, trip.StartUtc, trip.EndUtc));
        if (points.Count < 2)
        {
            // No path was stored (a coarse trip, or the fixes are gone): the two ends, joined by a dashed guess.
            return trip is { StartLat: { } startLat, StartLon: { } startLon, EndLat: { } endLat, EndLon: { } endLon }
                ? [new HistoryTrailSegment(entryId, true, [[startLon, startLat], [endLon, endLat]])]
                : [];
        }

        var segments = new List<HistoryTrailSegment>();
        var run = new List<double[]> { new[] { points[0].Lon, points[0].Lat } };
        for (var i = 1; i < points.Count; i++)
        {
            var gap = points[i].Ts - points[i - 1].Ts;
            var point = new[] { points[i].Lon, points[i].Lat };
            if (gap > GapBreak)
            {
                Flush(segments, entryId, run);
                run = [point];
            }
            else if (gap > SolidMax)
            {
                Flush(segments, entryId, run);
                segments.Add(new HistoryTrailSegment(entryId, true, [[points[i - 1].Lon, points[i - 1].Lat], point]));
                run = [point];
            }
            else
            {
                run.Add(point);
            }
        }

        Flush(segments, entryId, run);
        return segments;
    }

    private static void Flush(List<HistoryTrailSegment> segments, string entryId, List<double[]> run)
    {
        if (run.Count >= 2)
        {
            segments.Add(new HistoryTrailSegment(entryId, false, run));
        }
    }

    // The fixes in [from, to], oldest first (the list is sorted): a binary search for the first, then a walk.
    private static List<RawFix> Within(IReadOnlyList<RawFix> sorted, DateTimeOffset from, DateTimeOffset to)
    {
        var low = 0;
        var high = sorted.Count;
        while (low < high)
        {
            var middle = (low + high) / 2;
            if (sorted[middle].Ts < from)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        var result = new List<RawFix>();
        for (var i = low; i < sorted.Count && sorted[i].Ts <= to; i++)
        {
            result.Add(sorted[i]);
        }

        return result;
    }

    // Drops the points closer than ThinM to the last one kept, except the ends, a point that follows or precedes a long wait (so a gap is measured between real fixes) and one
    // that is more than KeepEvery after the last kept (so a stop in the middle of a drive still has points).
    private static List<RawFix> Thin(IReadOnlyList<RawFix> fixes)
    {
        var kept = new List<RawFix>(fixes.Count);
        for (var i = 0; i < fixes.Count; i++)
        {
            var fix = fixes[i];
            if (kept.Count == 0 || i == fixes.Count - 1)
            {
                kept.Add(fix);
                continue;
            }

            var last = kept[^1];
            var before = fix.Ts - fixes[i - 1].Ts;
            var after = fixes[i + 1].Ts - fix.Ts;
            if (Geo.DistanceM(last.Lat, last.Lon, fix.Lat, fix.Lon) >= ThinM || fix.Ts - last.Ts > KeepEvery || before > SolidMax || after > SolidMax)
            {
                kept.Add(fix);
            }
        }

        return kept;
    }

    // The stored address near an end of a trip: the newest fix with an address at or before the point, at most 30 minutes old and 1 km away (the same rule as the Driving list).
    private static string? AddressNear(IReadOnlyList<RawFix> withAddress, StatsTrip trip, bool end)
    {
        var at = end ? trip.EndUtc : trip.StartUtc;
        var lat = end ? trip.EndLat : trip.StartLat;
        var lon = end ? trip.EndLon : trip.StartLon;
        for (var i = withAddress.Count - 1; i >= 0; i--)
        {
            var fix = withAddress[i];
            if (fix.Ts > at)
            {
                continue;
            }

            if (at - fix.Ts > AddressMaxAge)
            {
                return null;
            }

            return lat is null || lon is null || Geo.DistanceM(fix.Lat, fix.Lon, lat.Value, lon.Value) <= AddressMaxDistanceM ? fix.Address : null;
        }

        return null;
    }
}
