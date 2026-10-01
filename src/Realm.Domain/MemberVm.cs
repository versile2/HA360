namespace Realm.Domain;

/// <summary>
/// One person on the map. Units are SI (metres, metres per second, UTC instants); null means unknown, never zero.
/// Build it with named arguments.
/// </summary>
/// <param name="Id">Lowercase slug, stable for the life of the database.</param>
/// <param name="LoreTitle">Null: no lore is shown.</param>
/// <param name="Charging">Null when the battery reading is unknown.</param>
/// <param name="BatteryAsOfUtc">Timestamp of the reading that BatteryPct came from.</param>
/// <param name="SpeedMps">The reported speed only, never an implied one.</param>
/// <param name="Freshness">Decided by the data layer with the heartbeat-aware threshold.</param>
/// <param name="StaticLabel">The text shown for a static pin (for example "Home · {town}"); null for live members.</param>
public record MemberVm(
    string Id,
    string DisplayName,
    string? LoreTitle,
    string? AvatarUrl,
    string Color,
    MemberKind Kind,
    double? Lat,
    double? Lon,
    double? AccuracyM,
    int? BatteryPct,
    bool? Charging,
    DateTimeOffset? BatteryAsOfUtc,
    bool IsDriving,
    double? SpeedMps,
    string? Street,
    string? City,
    string? Region,
    string? FullAddress,
    string? PlaceId,
    DateTimeOffset? SinceUtc,
    DateTimeOffset? LastUpdateUtc,
    int SortOrder,
    Freshness Freshness,
    string? StaticLabel = null);
