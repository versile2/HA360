namespace Realm.Domain;

/// <summary>One vehicle. SI units; null means unknown. Build it with named arguments.</summary>
/// <param name="SpeedMps">From the vehicle speed sensor; null when unavailable or too old.</param>
/// <param name="IsMoving">Ignition is On and SpeedMps is above 1 m/s; implies a known SpeedMps.</param>
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
    IgnitionState? Ignition,
    int? RemoteStartSecondsLeft,
    int? FuelPct,
    double? OdometerM,
    DateTimeOffset? LastUpdateUtc,
    double? SpeedMps,
    bool IsMoving,
    Freshness Freshness,
    bool IsPlaceholder,
    string? PlaceholderNote);
