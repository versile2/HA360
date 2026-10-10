using Realm.Domain;

namespace Realm.Infrastructure.Ha;

/// <summary>
/// The outcome of one discovery run (02 section 1.2): who the members and vehicles are (the roster entries that are on the map), which entities feed each of
/// them, the zones, and the entity ids the websocket must watch. It holds entity ids and roster settings only, no state values, so it is safe to log by count.
/// </summary>
/// <param name="TimeZone">HA's IANA time zone id.</param>
/// <param name="HaVersion">HA's version text; null when HA did not say.</param>
/// <param name="WatchList">Sorted, distinct; zones are in it or they never update live (R-096).</param>
/// <param name="Warnings">Fixed sentences that name an entity or a member id, never a position; the refresher logs each one once.</param>
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
/// <param name="Id">The roster entry's id: its entity id with the dot made an underscore (<c>person_alden</c>).</param>
/// <param name="Color">The roster colour (01 section 7.5).</param>
/// <param name="UserId">The HA user id of the person; only ever compared, never shown or logged.</param>
/// <param name="PhoneCapable">The phone's screen sensor exists, so a missing phone-use count means "not recorded yet" (02 section 6.3).</param>
/// <param name="AvatarUpstream">The servable picture of the member (an HA <c>image/serve</c> path or a Life360 HTTPS URL); null for none.</param>
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
    bool StaticShowAddress,
    string? Icon = null);

/// <summary>One tracker pin: a roster entry in Trackers, followed by the position of its device tracker.</summary>
/// <param name="TrackerId">The device tracker whose position the vehicle shows; null when the entry has none.</param>
/// <param name="Source">How the tracker is read: <see cref="FixSource.Life360"/> for a Life360 tracker, else <see cref="FixSource.Companion"/>.</param>
/// <param name="Color">The roster colour in effect; the pin's face takes it (0.2.1).</param>
/// <param name="Icon">The owner's choice of picture (<see cref="RosterIcons"/>); null: automatic.</param>
/// <param name="AvatarUpstream">The servable picture of the source; null for none.</param>
public sealed record ResolvedVehicle(
    string Id,
    string Name,
    string? LoreTitle,
    VehicleGlyph Glyph,
    int SortOrder,
    string? TrackerId,
    FixSource Source = FixSource.Companion,
    string Color = "",
    string? Icon = null,
    string? AvatarUpstream = null);
