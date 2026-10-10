namespace Realm.Domain.Tests;

/// <summary>Hand-made fixes, trips and zones for the Location History tests (0.3.0, D123). Every time is an offset from <see cref="Midnight"/> and no clock is read.</summary>
internal static class HistoryTestData
{
    public const double HomeLat = 31.0990;
    public const double HomeLon = -85.3410;
    public const double WorkLat = 31.1530;
    public const double WorkLon = -85.4080;
    public const string Entity = "device_tracker.alden_phone";

    private const double MetresPerDegreeOfLatitude = 6_371_008.8 * Math.PI / 180;

    /// <summary>2026-09-29 00:00 in UTC, a Tuesday.</summary>
    public static readonly DateTimeOffset Midnight = new(2026, 9, 29, 0, 0, 0, TimeSpan.Zero);

    public static readonly IReadOnlyList<RawPlace> Zones =
    [
        new("home", "Hearth Haven", HomeLat, HomeLon, 100, false),
        new("work", "Work", WorkLat, WorkLon, 150, false),
    ];

    public static DateTimeOffset At(int hour, int minute = 0) => Midnight.AddHours(hour).AddMinutes(minute);

    /// <summary>A latitude the given number of metres north of <paramref name="lat"/>.</summary>
    public static double North(double lat, double metres) => lat + (metres / MetresPerDegreeOfLatitude);

    public static RawFix Fix(DateTimeOffset at, double lat, double lon, double? accuracy = 15, string? address = null) =>
        new(Entity, FixSource.Companion, at, lat, lon, AccuracyM: accuracy, SpeedMps: 0, Address: address);

    /// <summary>A fix every <paramref name="stepMinutes"/> at a point, from <paramref name="from"/> to <paramref name="to"/> inclusive; a few metres of scatter.</summary>
    public static List<RawFix> Hold(DateTimeOffset from, DateTimeOffset to, double lat, double lon, int stepMinutes = 30, string? address = null)
    {
        var fixes = new List<RawFix>();
        var i = 0;
        for (var t = from; t <= to; t = t.AddMinutes(stepMinutes))
        {
            fixes.Add(Fix(t, North(lat, (i++ % 3) * 4), lon, address: address));
        }

        return fixes;
    }

    /// <summary>A closed drive between two points, with a straight line of fixes every 30 seconds.</summary>
    public static (StatsTrip Trip, List<RawFix> Fixes) Drive(DateTimeOffset start, DateTimeOffset end, double fromLat, double fromLon, double toLat, double toLon, string? fromPlace = null, string? toPlace = null)
    {
        var fixes = new List<RawFix>();
        var seconds = (int)(end - start).TotalSeconds;
        for (var s = 0; s <= seconds; s += 30)
        {
            var f = (double)s / seconds;
            fixes.Add(new RawFix(Entity, FixSource.Companion, start.AddSeconds(s), fromLat + ((toLat - fromLat) * f), fromLon + ((toLon - fromLon) * f), AccuracyM: 8, SpeedMps: 15, Driving: true));
        }

        var meters = Geo.DistanceM(fromLat, fromLon, toLat, toLon);
        var trip = new StatsTrip("alden", start, end, meters, TripQuality.Dense, DistanceBasis.Gps, 22, start.AddMinutes(1), null, 0, 0, fromPlace, toPlace, null, null, fromLat, fromLon, toLat, toLon);
        return (trip, fixes);
    }
}
