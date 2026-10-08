namespace Realm.Domain;

/// <summary>The thresholds behind the Driving report's event counts, printed in the footer of the printable report (01 section 6.10).</summary>
/// <param name="SpeedingMph">driving_speeding_mph: speed above which a stretch of driving counts as speeding.</param>
/// <param name="SpeedingMinSeconds">driving_speeding_min_seconds: how long the speed must stay above it.</param>
/// <param name="PhoneMinSeconds">driving_phone_min_seconds: how long the screen must be in use while driving.</param>
public sealed record DrivingThresholds(double SpeedingMph, int SpeedingMinSeconds, int PhoneMinSeconds)
{
    /// <summary>The add-on defaults (80 mph, 30 s, 10 s).</summary>
    public static DrivingThresholds Default { get; } = new(80, 30, 10);
}
