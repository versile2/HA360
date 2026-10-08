namespace Realm.Domain;

/// <summary>One trip row. Exists only inside <see cref="DriverWeek.Trips"/>.</summary>
/// <param name="FromLabel">Always named by <see cref="PlaceLabeler"/>; null only from a caller that has no name, and then the UI shows "Somewhere in the Realm".</param>
/// <param name="TopSpeedMps">Null for a coarse trip.</param>
/// <param name="Events">Keyed speeding, phone, accel, braking; each count is nullable.</param>
public record DriveVm(
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    string? FromLabel,
    string? ToLabel,
    double Meters,
    double? TopSpeedMps,
    IReadOnlyDictionary<string, int?> Events);
