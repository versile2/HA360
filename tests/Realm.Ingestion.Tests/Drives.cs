using System.Globalization;
using System.Text.Json;
using Realm.Domain;

namespace Realm.Ingestion.Tests;

/// <summary>
/// Fictional drives and Home Assistant history rows for the tests of the trip and statistics services. A drive runs due north from a made-up point:
/// ten minutes of standing at the start (a Life360 fix a minute), ten fixes half a minute apart at 45 mph (a bit over six kilometres), the stopped fix
/// 120 m further on, and eight minutes of standing there. The detector closes it on the fix four minutes into the stop. Times are the caller's; there is no clock here.
/// </summary>
internal static class Drives
{
    public const double OriginLat = 33.1;
    public const double OriginLon = -84.7;
    public const double CruiseMph = 45;

    /// <summary>How long before the departure a drive's first fix is.</summary>
    public static readonly TimeSpan LeadIn = TimeSpan.FromMinutes(10);

    /// <summary>How long after the departure a drive's last fix is: five and a half minutes of driving, then eight of standing.</summary>
    public static readonly TimeSpan RunOut = TimeSpan.FromSeconds(330) + TimeSpan.FromMinutes(8);

    private const double MetresPerDegreeOfLatitude = 111_194.9;
    private const int CruiseFixes = 10;
    private const int CruiseStepSeconds = 30;

    /// <summary>The tracker states of one Life360 drive that leaves at <paramref name="depart"/>, oldest first.</summary>
    public static List<HaEntitySnapshot> DriveRows(DateTimeOffset depart, string entityId = Plans.KingTracker, double northBase = 0)
    {
        var rows = new List<HaEntitySnapshot>();
        for (var minute = -10; minute <= 0; minute++)
        {
            rows.Add(Life360Row(entityId, depart.AddMinutes(minute), northBase, 0));
        }

        var cruiseMps = CruiseMph * FixParser.MphToMps;
        for (var k = 1; k <= CruiseFixes; k++)
        {
            rows.Add(Life360Row(entityId, depart.AddSeconds(CruiseStepSeconds * k), northBase + (CruiseStepSeconds * k * cruiseMps), CruiseMph));
        }

        var stopNorth = northBase + (CruiseStepSeconds * CruiseFixes * cruiseMps) + 120;
        var stoppedAt = depart.AddSeconds(CruiseStepSeconds * (CruiseFixes + 1));
        for (var minute = 0; minute <= 8; minute++)
        {
            rows.Add(Life360Row(entityId, stoppedAt.AddMinutes(minute), stopNorth, 0));
        }

        return rows;
    }

    /// <summary>
    /// A long drive that ends in silence: the lead-in, then a fix every half minute at 45 mph for <paramref name="minutes"/> minutes, and nothing after
    /// (the phone went out of reach). The detector ends it ten minutes later at its last fix and holds it for fifteen more, in case another trip joins it.
    /// </summary>
    public static List<HaEntitySnapshot> LongDriveRows(DateTimeOffset depart, int minutes, string entityId = Plans.KingTracker)
    {
        var rows = new List<HaEntitySnapshot>();
        for (var minute = -10; minute <= 0; minute++)
        {
            rows.Add(Life360Row(entityId, depart.AddMinutes(minute), 0, 0));
        }

        var cruiseMps = CruiseMph * FixParser.MphToMps;
        for (var k = 1; k <= minutes * 2; k++)
        {
            rows.Add(Life360Row(entityId, depart.AddSeconds(CruiseStepSeconds * k), CruiseStepSeconds * k * cruiseMps, CruiseMph));
        }

        return rows;
    }

    /// <summary>The trip the detector closes for <see cref="LongDriveRows"/>, ended by the silence.</summary>
    public static DetectedTrip LongDriveTrip(DateTimeOffset depart, int minutes, string entityId = Plans.KingTracker) =>
        new TripDetector().Replay([.. LongDriveRows(depart, minutes, entityId).Select(Fix)], depart.AddMinutes(minutes + 16)).Closed.Single();

    /// <summary>The same drive as the fixes the parser makes of its rows (what the database holds after the live feed stored them).</summary>
    public static List<RawFix> Drive(DateTimeOffset depart, string entityId = Plans.KingTracker, double northBase = 0) =>
        [.. DriveRows(depart, entityId, northBase).Select(Fix)];

    /// <summary>The trip the detector closes for <see cref="Drive"/> (the default detector, no zones): what a live run would have handed to the recorder.</summary>
    public static DetectedTrip ClosedTrip(DateTimeOffset depart, string entityId = Plans.KingTracker, double northBase = 0) =>
        new TripDetector().Replay(Drive(depart, entityId, northBase), depart + RunOut + TimeSpan.FromSeconds(1)).Closed.Single();

    /// <summary>One Life360 tracker state: <paramref name="northM"/> metres north of the origin, <paramref name="mph"/> as Life360 reports speed.</summary>
    public static HaEntitySnapshot Life360Row(string entityId, DateTimeOffset seen, double northM, double mph) =>
        new(
            entityId,
            "not_home",
            new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["latitude"] = JsonSerializer.SerializeToElement(OriginLat + (northM / MetresPerDegreeOfLatitude)),
                ["longitude"] = JsonSerializer.SerializeToElement(OriginLon),
                ["gps_accuracy"] = JsonSerializer.SerializeToElement(12.0),
                ["speed"] = JsonSerializer.SerializeToElement(mph),
                ["battery_level"] = JsonSerializer.SerializeToElement(80),
                ["last_seen"] = JsonSerializer.SerializeToElement(seen.ToString("O", CultureInfo.InvariantCulture)),
            },
            seen,
            seen);

    /// <summary>A companion (phone) tracker state at a position; its time is the state's update time.</summary>
    public static HaEntitySnapshot CompanionRow(string entityId, DateTimeOffset at, double northM) =>
        new(
            entityId,
            "not_home",
            new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["latitude"] = JsonSerializer.SerializeToElement(OriginLat + (northM / MetresPerDegreeOfLatitude)),
                ["longitude"] = JsonSerializer.SerializeToElement(OriginLon),
                ["gps_accuracy"] = JsonSerializer.SerializeToElement(9.0),
            },
            at,
            at);

    /// <summary>A binary sensor state (<c>on</c> or <c>off</c>) that Home Assistant changed at <paramref name="at"/>.</summary>
    public static HaEntitySnapshot SensorRow(string entityId, string state, DateTimeOffset at) =>
        new(entityId, state, new Dictionary<string, JsonElement>(StringComparer.Ordinal), at, at);

    /// <summary>The fix the parser makes of a Life360 row.</summary>
    public static RawFix Fix(HaEntitySnapshot row) =>
        FixParser.ParseTracker(row, FixSource.Life360, DateTimeOffset.MaxValue) ?? throw new InvalidOperationException("The row is not a fix");
}
