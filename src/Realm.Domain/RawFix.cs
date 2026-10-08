namespace Realm.Domain;

/// <summary>
/// One position observation from one source, in SI units (metres, metres per second, UTC instants).
/// Produced by <see cref="FixParser"/>, the only place where units are converted. Null means unknown, never zero.
/// </summary>
/// <param name="EntityId">The tracker entity the fix came from; the caller maps it to a member.</param>
/// <param name="Ts">The time the position was taken (see 02 section 1.6 for what each source uses).</param>
/// <param name="AccuracyM">Null when the source gives no usable accuracy (the Life360 placeholder).</param>
/// <param name="SpeedMps">The reported speed only, never an implied one.</param>
/// <param name="BatteryPct">Whole percent 0 to 100.</param>
/// <param name="Charging">Null when the battery reading is unknown or does not say.</param>
/// <param name="BatteryAsOfUtc">When the battery reading was taken: the fix time for a tracker attribute, the sensor time for an Android battery sensor. Null means the fix time.</param>
/// <param name="Driving">The Life360 driving flag, a hint only.</param>
/// <param name="Address">The Life360 free-text address.</param>
public record RawFix(
    string EntityId,
    FixSource Source,
    DateTimeOffset Ts,
    double Lat,
    double Lon,
    double? AccuracyM = null,
    double? SpeedMps = null,
    double? HeadingDeg = null,
    double? AltitudeM = null,
    int? BatteryPct = null,
    bool? Charging = null,
    DateTimeOffset? BatteryAsOfUtc = null,
    bool? Driving = null,
    string? Address = null);
