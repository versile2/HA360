namespace Realm.Domain;

/// <summary>One accepted fix of a trip track, with the speeds the trip logic used.</summary>
/// <param name="SpeedMps">The speed for state decisions: the reported speed, else the implied one; null when neither exists or it exceeds 90 m/s.</param>
/// <param name="ReportedSpeedMps">The reported speed only, null when the fix had none or it exceeds 90 m/s. Corroboration (02 section 5.8) starts from it.</param>
public record TrackPoint(
    DateTimeOffset Ts,
    double Lat,
    double Lon,
    double? SpeedMps,
    double? ReportedSpeedMps);
