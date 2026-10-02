using Realm.Domain;

namespace Realm.Infrastructure.Ha;

/// <summary>
/// The outcome of one discovery run (02 section 1.2): who the members and vehicles are, which entities feed each of them, the zones, and the entity ids the
/// websocket must watch. It holds entity ids and options only, no state values, so it is safe to log by count.
/// </summary>
/// <param name="TimeZone">HA's IANA time zone id.</param>
/// <param name="HaVersion">HA's version text; null when HA did not say.</param>
/// <param name="WatchList">Sorted, distinct; zones are in it or they never update live (R-096).</param>
/// <param name="Warnings">Fixed sentences that name a member id or an option key, never a position; the refresher logs each one once.</param>
public sealed record HaDiscoveryResult(
    string TimeZone,
    string? HaVersion,
    IReadOnlyList<ResolvedMember> Members,
    IReadOnlyList<ResolvedVehicle> Vehicles,
    IReadOnlyList<RawPlace> Zones,
    IReadOnlyList<string> WatchList,
    IReadOnlyList<string> Warnings)
{
    /// <summary>What is known before the first discovery has finished.</summary>
    public static readonly HaDiscoveryResult Empty = new("UTC", null, [], [], [], [], []);
}

/// <summary>The Android companion sensors of one phone that exist in HA (02 section 1.2 step 5); null when the sensor is absent.</summary>
public sealed record CompanionSensors(string? BatteryLevel, string? BatteryState, string? Interactive, string? DeviceLocked, string? AndroidAuto);

/// <summary>One member with every entity that feeds it (02 section 2.3). A null entity id means that source does not exist for this member.</summary>
/// <param name="Id">The options id, or <c>l360_xxxxxxxx</c> / <c>ha_xxxxxxxx</c> for a member discovered without an options entry (02 section 2.2).</param>
/// <param name="Color">Option colour, else the next colour of the member palette (01 section 7.5).</param>
/// <param name="UserId">The HA user id of the person; only ever compared, never shown or logged.</param>
/// <param name="PhoneCapable">The phone's screen sensor exists, so a missing phone-use count means "not recorded yet" (02 section 6.3).</param>
/// <param name="AvatarUpstream">The servable picture of the member (an HA <c>image/serve</c> path or a Life360 HTTPS URL), chosen by the member's avatar option; null for none.</param>
public sealed record ResolvedMember(
    string Id,
    string DisplayName,
    string? LoreTitle,
    MemberKind Kind,
    string Color,
    int SortOrder,
    bool InDrivingReport,
    string? PersonId,
    string? UserId,
    string? Life360TrackerId,
    string? CompanionTrackerId,
    CompanionSensors? Sensors,
    bool PhoneCapable,
    string? AvatarUpstream,
    string? StaticLabel,
    string? StaticAddress,
    double? StaticLat,
    double? StaticLon,
    bool StaticShowAddress);

/// <summary>One vehicle of the options. A placeholder (<c>integration: none</c>) has no entities.</summary>
/// <param name="SensorIds">The <c>sensor.{prefix}_*</c> entities of 02 section 1.2 step 6 that exist in HA.</param>
public sealed record ResolvedVehicle(
    string Id,
    string Name,
    string? LoreTitle,
    VehicleGlyph Glyph,
    bool IsPlaceholder,
    string? PlaceholderNote,
    int SortOrder,
    string? Prefix,
    string? TrackerId,
    IReadOnlyList<string> SensorIds);
