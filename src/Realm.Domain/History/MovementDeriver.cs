namespace Realm.Domain;

/// <summary>The thresholds of <see cref="MovementDeriver"/> (0.3.1, D125).</summary>
public sealed record MovementOptions
{
    /// <summary>A fix this far from where the tracker rested starts a move.</summary>
    public double StartM { get; init; } = 150;

    /// <summary>While moving, fixes within this distance of the latest "arrival" fix count as standing still there.</summary>
    public double RestM { get; init; } = 60;

    /// <summary>A move ends when the tracker has stood still this long (the move ends at the arrival fix).</summary>
    public TimeSpan RestFor { get; init; } = TimeSpan.FromMinutes(3);

    /// <summary>A move shorter than this (the length of its path) is jitter, not a move.</summary>
    public double MinPathM { get; init; } = 250;

    /// <summary>Two fixes further apart in time than this are not joined: the data cannot say what happened between them.</summary>
    public TimeSpan BridgeMax { get; init; } = TimeSpan.FromHours(6);

    /// <summary>A fix with a worse accuracy than this says nothing about where the tracker is.</summary>
    public double MaxFixAccuracyM { get; init; } = PlaceResolver.DecisionMaxAccuracyM;

    /// <summary>A move whose fixes are further apart than this is "coarse" (a rough line, the same word as a coarse trip).</summary>
    public TimeSpan CoarseGap { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>The defaults above.</summary>
    public static MovementOptions Default { get; } = new();
}

/// <summary>
/// The moves of a tracker from its stored fixes (0.3.1, D125). A tracker has no trip detector (no phone, no speed limit, no driver), so Location History derives its "Moved · 12.4 mi · 24 mins" lines
/// at read time from the fixes alone and hands them to the same assembler as a person's trips, as <see cref="StatsTrip"/> rows: the stays between them, the trail and the day totals then work unchanged.
/// A move starts at the last fix where the tracker rested and ends at the fix where it came to rest again. Pure: no clock, no database.
/// </summary>
public static class MovementDeriver
{
    /// <summary>
    /// The moves in time order. <paramref name="fixes"/> need not be sorted. <paramref name="zones"/> (the places that exist now) name the two ends of a move that start or end at a zone
    /// (within the snap distance of a trip, <see cref="PlaceResolver.EndpointSnapM"/>); without them the ends are named by address or by the nearest zone.
    /// </summary>
    public static IReadOnlyList<StatsTrip> Derive(string memberId, IReadOnlyList<RawFix> fixes, IReadOnlyList<RawPlace>? zones = null, MovementOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(fixes);
        var opt = options ?? MovementOptions.Default;
        var places = (zones ?? []).Where(zone => zone.RadiusM > 0 && zone.RadiusM <= 2000).ToList();
        var sorted = fixes
            .Where(fix => fix.AccuracyM is null || fix.AccuracyM <= opt.MaxFixAccuracyM)
            .OrderBy(fix => fix.Ts)
            .ToList();
        var result = new List<StatsTrip>();
        if (sorted.Count < 2)
        {
            return result;
        }

        var anchor = sorted[0];
        List<RawFix>? run = null;
        var rest = 0;
        for (var i = 1; i < sorted.Count; i++)
        {
            var fix = sorted[i];
            if (run is null)
            {
                if (fix.Ts - anchor.Ts <= opt.BridgeMax && Geo.DistanceM(anchor.Lat, anchor.Lon, fix.Lat, fix.Lon) >= opt.StartM)
                {
                    run = [anchor, fix];
                    rest = 1;
                }
                else
                {
                    anchor = fix;
                }

                continue;
            }

            if (fix.Ts - run[^1].Ts > opt.BridgeMax)
            {
                Close(memberId, run, rest, opt, places, result);
                run = null;
                anchor = fix;
                continue;
            }

            run.Add(fix);
            var arrival = run[rest];
            if (Geo.DistanceM(arrival.Lat, arrival.Lon, fix.Lat, fix.Lon) <= opt.RestM)
            {
                if (fix.Ts - arrival.Ts >= opt.RestFor)
                {
                    Close(memberId, run, rest, opt, places, result);
                    run = null;
                    anchor = fix;
                }
            }
            else
            {
                rest = run.Count - 1;
            }
        }

        if (run is not null)
        {
            // Still moving at the end of the data: the move ends at the newest fix.
            Close(memberId, run, run.Count - 1, opt, places, result);
        }

        return result;
    }

    // Adds the move from run[0] to run[end] when its path is long enough.
    private static void Close(string memberId, List<RawFix> run, int end, MovementOptions opt, IReadOnlyList<RawPlace> places, List<StatsTrip> result)
    {
        var meters = 0.0;
        var maxGap = TimeSpan.Zero;
        for (var i = 1; i <= end; i++)
        {
            meters += Geo.DistanceM(run[i - 1].Lat, run[i - 1].Lon, run[i].Lat, run[i].Lon);
            var gap = run[i].Ts - run[i - 1].Ts;
            if (gap > maxGap)
            {
                maxGap = gap;
            }
        }

        if (meters < opt.MinPathM)
        {
            return;
        }

        var first = run[0];
        var last = run[end];
        result.Add(new StatsTrip(
            MemberId: memberId,
            StartUtc: first.Ts,
            EndUtc: last.Ts,
            Meters: meters,
            Quality: maxGap > opt.CoarseGap ? TripQuality.Coarse : TripQuality.Dense,
            DistanceBasis: DistanceBasis.Gps,
            TopSpeedMps: null,
            TopSpeedAtUtc: null,
            TopSpeedStreet: null,
            SpeedingCount: null,
            PhoneCount: null,
            StartPlaceId: PlaceResolver.SnapEndpoint(first.Lat, first.Lon, places),
            EndPlaceId: PlaceResolver.SnapEndpoint(last.Lat, last.Lon, places),
            StartStreet: null,
            EndStreet: null,
            StartLat: first.Lat,
            StartLon: first.Lon,
            EndLat: last.Lat,
            EndLon: last.Lon));
    }
}
