using System.Text.Json;

namespace Realm.Domain.Tests;

/// <summary>
/// Synthetic drives for the detector tests. The road runs due north from a fictional point, so the distance along
/// it equals the haversine distance (R = 6 371 008.8 m) exactly and expected distances are plain sums of the
/// northings. Times are seconds after <see cref="T0"/>; there is no clock anywhere.
/// </summary>
internal static class TripFixtures
{
    public const string PhoneEntity = "device_tracker.alden_phone";
    public const double OriginLat = 31.1000;
    public const double OriginLon = -97.3400;

    private const double EarthRadiusM = 6_371_008.8;
    private const double MetresPerDegreeOfLatitude = EarthRadiusM * Math.PI / 180;

    // Tuesday 2026-09-29 08:00 in Chicago.
    public static readonly DateTimeOffset T0 = new(2026, 9, 29, 13, 0, 0, TimeSpan.Zero);

    // A few metres of east-west scatter for a phone that is standing still.
    private static readonly double[] Scatter = [0, 3, -3, 2, -2];

    public static DateTimeOffset At(double seconds) => T0.AddSeconds(seconds);

    public static double Lat(double northM) => OriginLat + (northM / MetresPerDegreeOfLatitude);

    public static double Lon(double eastM) =>
        OriginLon + (eastM / (MetresPerDegreeOfLatitude * Math.Cos(OriginLat * Math.PI / 180)));

    /// <summary>One fix at <paramref name="north"/> metres north of the origin (and <paramref name="east"/> east of it), <paramref name="t"/> seconds after T0.</summary>
    public static RawFix Fix(
        double t,
        double north,
        double east = 0,
        FixSource source = FixSource.Life360,
        double? mps = null,
        double? accuracy = null,
        bool? driving = null,
        string? address = null,
        string entity = PhoneEntity) =>
        new(entity, source, At(t), Lat(north), Lon(east), AccuracyM: accuracy, SpeedMps: mps, Driving: driving, Address: address);

    /// <summary>A member standing at the origin: a fix every <paramref name="step"/> seconds from <paramref name="from"/> to <paramref name="to"/> inclusive, reporting <paramref name="mps"/> (0 unless null).</summary>
    public static List<RawFix> Idle(double from, double to, double step = 42, double north = 0, FixSource source = FixSource.Life360, double? mps = 0)
    {
        var fixes = new List<RawFix>();
        var i = 0;
        for (var t = from; t <= to + 1e-9; t += step)
        {
            // The last fix is exactly at the origin, so that distances from it are exact sums of northings.
            fixes.Add(Fix(t, north, t + step > to + 1e-9 ? 0 : Scatter[i++ % Scatter.Length], source, mps));
        }

        return fixes;
    }

    /// <summary>A steady drive north: <paramref name="count"/> fixes <paramref name="step"/> seconds apart, the first one step after (<paramref name="t0"/>, <paramref name="north0"/>), reporting <paramref name="mps"/>.</summary>
    public static List<RawFix> Cruise(
        double t0,
        double north0,
        double mps,
        double step,
        int count,
        FixSource source = FixSource.Life360,
        bool? driving = null,
        double? reported = null,
        bool omitSpeed = false)
    {
        var fixes = new List<RawFix>();
        for (var k = 1; k <= count; k++)
        {
            fixes.Add(Fix(
                t0 + (k * step),
                north0 + (k * step * mps),
                source: source,
                mps: omitSpeed ? null : reported ?? mps,
                driving: driving));
        }

        return fixes;
    }

    // The phone has stood at the origin for ten minutes (a fix every 42 s up to departT), then 6 s fixes at
    // 8, 12, 16 and 20 m/s. The first fast fix is at departT + 6 s, the second fast fix 84 m out and the third
    // 168 m out (>= 150 m: the start). The list ends at (departT + 24 s, 276 m) doing 20 m/s.
    public static List<RawFix> Leave(double departT)
    {
        var fixes = Idle(departT - 672, departT);
        double[] speeds = [8, 12, 16, 20];
        var north = 0.0;
        var previous = 0.0;
        for (var k = 1; k <= speeds.Length; k++)
        {
            north += (previous + speeds[k - 1]) / 2 * 6;
            previous = speeds[k - 1];
            fixes.Add(Fix(departT + (6 * k), north, mps: speeds[k - 1]));
        }

        return fixes;
    }

    /// <summary>A new detector replays the fixes and advances to <paramref name="asOf"/> seconds after T0.</summary>
    public static TripStep Replay(IEnumerable<RawFix> fixes, double asOf, TripDetector? detector = null) =>
        (detector ?? new TripDetector()).Replay(fixes, At(asOf));

    /// <summary>The same fixes fed one by one as a live feed would, then the tick at <paramref name="asOf"/>.</summary>
    public static TripStep Live(IEnumerable<RawFix> fixes, double asOf, TripDetector? detector = null)
    {
        var d = detector ?? new TripDetector();
        var steps = fixes.OrderBy(f => f.Ts).Select(d.Process).ToList();
        steps.Add(d.Tick(At(asOf)));
        return new TripStep(
            [.. steps.SelectMany(s => s.Decisions)],
            [.. steps.SelectMany(s => s.Retracted)],
            [.. steps.SelectMany(s => s.Closed)],
            [.. steps.SelectMany(s => s.Discarded)]);
    }

    /// <summary>Everything a step decided, as text, so two runs can be compared in full (records hold lists, which compare by reference).</summary>
    public static string Describe(TripStep step) => JsonSerializer.Serialize(step);

    /// <summary>A closed trip built by hand, for the rules that take a trip and not fixes (phone use, speeding).</summary>
    public static DetectedTrip Trip(
        double startS,
        double endS,
        IReadOnlyList<TrackPoint> track,
        TripQuality quality = TripQuality.Dense) =>
        new(
            StartUtc: At(startS),
            EndUtc: At(endS),
            DurationS: (int)(endS - startS),
            StartLat: Lat(0),
            StartLon: Lon(0),
            EndLat: Lat(0),
            EndLon: Lon(0),
            StartPlaceId: null,
            EndPlaceId: null,
            StartStreet: null,
            EndStreet: null,
            DistanceGpsM: 10_000,
            TopSpeedMps: null,
            TopSpeedAtUtc: null,
            TopSpeedStreet: null,
            SpeedingCount: null,
            PhoneCount: null,
            Quality: quality,
            HasGap: false,
            EndedBy: TripEndedBy.Stop,
            SourceMask: "life360",
            Track: track,
            SpeedingEpisodes: [],
            PhoneEvents: []);

    /// <summary>A track at a constant speed, one point every <paramref name="step"/> seconds from <paramref name="startS"/> to <paramref name="endS"/> inclusive, on the road.</summary>
    public static List<TrackPoint> Points(double startS, double endS, double mps, double step = 6)
    {
        var points = new List<TrackPoint>();
        for (var t = startS; t <= endS + 1e-9; t += step)
        {
            var north = (t - startS) * mps;
            points.Add(new TrackPoint(At(t), Lat(north), OriginLon, mps, mps));
        }

        return points;
    }
}
