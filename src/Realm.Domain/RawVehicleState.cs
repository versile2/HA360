namespace Realm.Domain;

/// <summary>The inputs of a vehicle row, parsed from the FordPass sensors of one vehicle. SI units; null means unknown.</summary>
/// <param name="LastUpdateUtc">The vehicle's sample clock: the lastrefresh sensor, else the newest update time among its sensors.</param>
/// <param name="Ignition">RemoteStart when a remote-start countdown is running and the ignition is On.</param>
/// <param name="RemoteStartSecondsLeft">Null when no countdown is running.</param>
/// <param name="SpeedMps">As reported by the speed sensor, whatever its age; the caller applies the maximum age against <paramref name="LastUpdateUtc"/>.</param>
public record RawVehicleState(
    DateTimeOffset? LastUpdateUtc,
    double? OdometerM,
    int? FuelPct,
    IgnitionState? Ignition,
    int? RemoteStartSecondsLeft,
    double? SpeedMps);
