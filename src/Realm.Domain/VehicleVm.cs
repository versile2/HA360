namespace Realm.Domain;

/// <summary>
/// One tracker pin (the type keeps its 0.1 name): a GPS device tracker the owner put under Trackers in Settings. It has a position and nothing else (no odometer, fuel or engine state).
/// SI units; null means unknown. Build it with named arguments.
/// </summary>
/// <param name="SpeedMps">The speed the tracker reports; null when it reports none or the report is too old.</param>
/// <param name="IsMoving">The fresh reported speed is above <see cref="VehicleRules.MovingMinMps"/>; implies a known SpeedMps.</param>
/// <param name="Freshness">Fresh, Stale or NoFix, decided by the data layer.</param>
/// <param name="Color">The roster colour in effect; the pin's face is drawn on it (0.2.1).</param>
/// <param name="AvatarUrl">The photo (the app's avatar proxy), only when the face is the photo; null otherwise.</param>
/// <param name="ShowInitial">The face is the first letter of the name (the owner chose it); <see cref="Glyph"/> is then not drawn.</param>
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
    Freshness Freshness,
    string Color = "#E8BC4E",
    string? AvatarUrl = null,
    bool ShowInitial = false);
