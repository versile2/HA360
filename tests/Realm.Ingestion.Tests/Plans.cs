using Realm.Domain;
using Realm.Infrastructure.Ha;

namespace Realm.Ingestion.Tests;

/// <summary>Builds the discovery results the ingestion tests start from. Every id is fictional.</summary>
internal static class Plans
{
    public const string KingTracker = "device_tracker.life360_king";
    public const string QueenTracker = "device_tracker.life360_queen";
    public const string KingPhone = "device_tracker.king_phone";

    /// <summary>A live member with a Life360 tracker and, optionally, a companion tracker, the Android sensors and a picture.</summary>
    public static ResolvedMember Member(
        string id,
        string? life360 = null,
        string? companion = null,
        CompanionSensors? sensors = null,
        string? avatar = null,
        string? userId = null,
        bool inDrivingReport = true,
        int sortOrder = 0,
        bool phoneCapable = false) =>
        new(
            Id: id,
            DisplayName: char.ToUpperInvariant(id[0]) + id[1..],
            LoreTitle: null,
            Kind: MemberKind.Live,
            Color: "#C0FFEE",
            SortOrder: sortOrder,
            InDrivingReport: inDrivingReport,
            PersonId: null,
            UserId: userId,
            Life360TrackerId: life360,
            CompanionTrackerId: companion,
            Sensors: sensors,
            PhoneCapable: phoneCapable,
            AvatarUpstream: avatar,
            StaticLabel: null,
            StaticAddress: null,
            StaticLat: null,
            StaticLon: null,
            StaticShowAddress: false);

    /// <summary>
    /// A discovery result. Without an explicit <paramref name="watch"/> list, the watch list holds every entity of the members and the vehicles and the
    /// zone entities, as a real discovery's does.
    /// </summary>
    public static HaDiscoveryResult Discovery(
        string zone = "UTC",
        IReadOnlyList<RawPlace>? zones = null,
        IReadOnlyList<string>? watch = null,
        IReadOnlyList<ResolvedVehicle>? vehicles = null,
        params ResolvedMember[] members)
    {
        var placed = zones ?? [];
        var cars = vehicles ?? [];
        var ids = members
            .SelectMany(m => new[] { m.Life360TrackerId, m.CompanionTrackerId, m.Sensors?.BatteryLevel, m.Sensors?.BatteryState, m.Sensors?.Interactive, m.Sensors?.DeviceLocked, m.Sensors?.AndroidAuto })
            .Concat(cars.SelectMany(v => v.SensorIds.Append(v.TrackerId)))
            .Concat(placed.Select(z => "zone." + z.Id))
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        return new HaDiscoveryResult(zone, "2026.9.1", members, cars, placed, watch ?? ids, []);
    }
}
