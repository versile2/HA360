namespace Realm.Domain;

/// <summary>
/// One vehicle: a GPS device tracker the owner moved to Vehicles in Settings. It has a position and nothing else (no odometer, fuel or engine state).
/// SI units; null means unknown. Build it with named arguments.
/// </summary>
/// <param name="SpeedMps">The speed the tracker reports; null when it reports none or the report is too old.</param>
/// <param name="IsMoving">The fresh reported speed is above <see cref="VehicleRules.MovingMinMps"/>; implies a known SpeedMps.</param>
/// <param name="Freshness">Fresh, Stale or NoFix, decided by the data layer.</param>
public record VehicleVm(
    string Id,
    string Name,
    string? LoreTitle,
    VehicleGlyph Glyph,
    double? Lat,
    double? Lon,
    string? Street,
    string? PlaceId,
    DateTimeOffset? LastUpdateUtc,
    double? SpeedMps,
    bool IsMoving,
    Freshness Freshness);
