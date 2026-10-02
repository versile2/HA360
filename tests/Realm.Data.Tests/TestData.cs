using Realm.Domain;

namespace Realm.Data.Tests;

/// <summary>Fictional rows for the writer and query tests (the cast of the demo: the king, the queen, the wagon).</summary>
internal static class TestData
{
    public static readonly DateTimeOffset Start = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    public static RawFix Fix(int second, FixSource source = FixSource.Life360, double lat = 38.5, string? address = "1 Example Rd, Highmeadow")
    {
        return new RawFix(
            EntityId: "device_tracker.life360_alden",
            Source: source,
            Ts: Start.AddSeconds(second),
            Lat: lat,
            Lon: -98.5,
            AccuracyM: 12.5,
            SpeedMps: 3.5,
            HeadingDeg: 90,
            AltitudeM: 410,
            BatteryPct: 80,
            Charging: false,
            BatteryAsOfUtc: null,
            Driving: true,
            Address: address);
    }

    public static DetectedTrip Trip(int startMinute, TripQuality quality = TripQuality.Dense)
    {
        var start = Start.AddMinutes(startMinute);
        return new DetectedTrip(
            StartUtc: start,
            EndUtc: start.AddMinutes(20),
            DurationS: 1200,
            StartLat: 38.5,
            StartLon: -98.5,
            EndLat: 38.6,
            EndLon: -98.4,
            StartPlaceId: "home",
            EndPlaceId: null,
            StartStreet: "Example Rd",
            EndStreet: null,
            DistanceGpsM: 12345.5,
            TopSpeedMps: 31.25,
            TopSpeedAtUtc: start.AddMinutes(9),
            TopSpeedStreet: "County Rd 1",
            SpeedingCount: 1,
            PhoneCount: 1,
            Quality: quality,
            HasGap: false,
            EndedBy: TripEndedBy.Stop,
            SourceMask: "companion,life360",
            Track: [],
            SpeedingEpisodes: [new SpeedingEpisode(start.AddMinutes(8), start.AddMinutes(10), 36.5, 38.55, -98.45)],
            PhoneEvents: [new PhoneUseEvent(start.AddMinutes(3), start.AddMinutes(4), 42)]);
    }
}
