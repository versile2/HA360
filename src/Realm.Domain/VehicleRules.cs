namespace Realm.Domain;

/// <summary>
/// The vehicle row's derived fields: the speed that may be shown and whether the vehicle is moving. Pure: the position time and the clock are parameters,
/// no clock is read. A vehicle is a device tracker, so it is moving when the speed it reports is above <see cref="MovingMinMps"/> and recent.
/// </summary>
public static class VehicleRules
{
    /// <summary><c>Vehicle.MovingMinMps</c>: a vehicle is moving only above this speed, in metres per second.</summary>
    public const double MovingMinMps = 1.0;

    /// <summary><c>Vehicle.SpeedMaxAgeS</c>: a reported speed older than this is not shown.</summary>
    public const int SpeedMaxAgeS = 600;

    /// <summary>
    /// <c>VehicleVm.SpeedMps</c>: the reported speed when the position that carries it is no older than <paramref name="maxAgeS"/> seconds (inclusive), else
    /// null. A vehicle without a position time has no age, so no speed.
    /// </summary>
    /// <param name="speedMps">The tracker's speed in m/s, whatever its age; null when it reports none.</param>
    /// <param name="sampleUtc">The time of the position the speed came with.</param>
    /// <param name="now">The clock of the snapshot being built.</param>
    public static double? FreshSpeedMps(double? speedMps, DateTimeOffset? sampleUtc, DateTimeOffset now, int maxAgeS = SpeedMaxAgeS) =>
        speedMps is { } speed && sampleUtc is { } sample && now - sample <= TimeSpan.FromSeconds(maxAgeS) ? speed : null;

    /// <summary><c>VehicleVm.IsMoving</c>: the fresh speed (<see cref="FreshSpeedMps"/>) is above <see cref="MovingMinMps"/>. It implies a known speed.</summary>
    public static bool IsMoving(double? speedMps, DateTimeOffset? sampleUtc, DateTimeOffset now, int maxAgeS = SpeedMaxAgeS) =>
        FreshSpeedMps(speedMps, sampleUtc, now, maxAgeS) > MovingMinMps;
}
