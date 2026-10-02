namespace Realm.Domain;

/// <summary>
/// The vehicle row's derived fields (02 section 1.9): the speed that may be shown and whether the vehicle is moving. Pure: the sample time and the clock
/// are parameters, no clock is read. <see cref="FixParser.ParseVehicleState"/> reports the speed sensor whatever its age (<see cref="RawVehicleState.SpeedMps"/>),
/// so the age rule lives here and nowhere else.
/// </summary>
public static class VehicleRules
{
    /// <summary><c>Vehicle.MovingMinMps</c>: a vehicle is moving only above this speed, in metres per second (02 section 1.9).</summary>
    public const double MovingMinMps = 1.0;

    /// <summary><c>Vehicle.SpeedMaxAgeS</c>: twice the 5 minute driving poll of FordPass (02 section 1.9).</summary>
    public const int SpeedMaxAgeS = 600;

    /// <summary>
    /// <c>VehicleVm.SpeedMps</c>: the reported speed when the sample that carries it is no older than <paramref name="maxAgeS"/> seconds (inclusive), else
    /// null. A vehicle without a sample time has no age, so no speed.
    /// </summary>
    /// <param name="speedMps">The speed sensor in m/s, whatever its age; null when it is unavailable.</param>
    /// <param name="sampleUtc">The vehicle's sample clock (<see cref="RawVehicleState.LastUpdateUtc"/>).</param>
    /// <param name="now">The clock of the snapshot being built.</param>
    public static double? FreshSpeedMps(double? speedMps, DateTimeOffset? sampleUtc, DateTimeOffset now, int maxAgeS = SpeedMaxAgeS) =>
        speedMps is { } speed && sampleUtc is { } sample && now - sample <= TimeSpan.FromSeconds(maxAgeS) ? speed : null;

    /// <summary>
    /// <c>VehicleVm.IsMoving</c>: the ignition is <see cref="IgnitionState.On"/> and the fresh speed (<see cref="FreshSpeedMps"/>) is above
    /// <see cref="MovingMinMps"/>. The ignition alone never means moving (it flaps and reads On in a parked car, 02 L9 and L16), and neither does a speed
    /// without the ignition; a remote start or accessory power is not driving. It implies a known speed.
    /// </summary>
    public static bool IsMoving(IgnitionState? ignition, double? speedMps, DateTimeOffset? sampleUtc, DateTimeOffset now, int maxAgeS = SpeedMaxAgeS) =>
        ignition == IgnitionState.On && FreshSpeedMps(speedMps, sampleUtc, now, maxAgeS) > MovingMinMps;
}
