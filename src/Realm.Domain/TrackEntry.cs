namespace Realm.Domain;

/// <summary>An accepted track fix with the speed the trip logic uses for it.</summary>
/// <param name="SpeedMps">Reported, else implied, speed; null when neither exists or it exceeds the maximum.</param>
/// <param name="ReportedSpeedMps">The reported speed when it is usable.</param>
/// <param name="Reanchored">The fix was accepted as a real jump after three consistent spike rejects.</param>
internal sealed record TrackEntry(RawFix Fix, double? SpeedMps, double? ReportedSpeedMps, bool Reanchored)
{
    public DateTimeOffset Ts => Fix.Ts;

    public double Lat => Fix.Lat;

    public double Lon => Fix.Lon;

    /// <summary>The speed for state decisions: unknown counts as standing still.</summary>
    public double VEff => SpeedMps ?? 0;
}
