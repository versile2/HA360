namespace Realm.Domain;

/// <summary>
/// The result of fusing one person's sources: the winning fix's own point, plus battery, speed and address
/// chosen by their own rules (02 section 4.3).
/// </summary>
/// <param name="AccuracyM">The winner's accuracy; null when the winner has none (Life360).</param>
/// <param name="Ts">The winning fix's time.</param>
/// <param name="SpeedMps">A reported speed no older than 90 seconds; never an implied one.</param>
/// <param name="BatteryAsOfUtc">When the chosen battery reading was taken; null when BatteryPct is null.</param>
/// <param name="Address">The raw Life360 address; parse it with <see cref="AddressParser"/>.</param>
/// <param name="Alts">The other candidates that survived the discard rules, newest first.</param>
public record FusedPosition(
    double Lat,
    double Lon,
    double? AccuracyM,
    DateTimeOffset Ts,
    FixSource WinnerSource,
    double? SpeedMps,
    int? BatteryPct,
    bool? Charging,
    DateTimeOffset? BatteryAsOfUtc,
    string? Address,
    IReadOnlyList<RawFix> Alts);
