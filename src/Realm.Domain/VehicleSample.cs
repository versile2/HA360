namespace Realm.Domain;

/// <summary>
/// One row of the <c>vehicle_samples</c> table (02 section 7.2): the FordPass readings of one vehicle at one instant. A sample is keyed by
/// (vehicle, time); a later sample with the same key fills in the columns that are still empty and never erases a value (02 section 7.3).
/// SI units; null means "not part of this sample", never zero.
/// </summary>
/// <param name="Ts">The sample clock: the lastrefresh sensor when there is one, else the sensor's change time (02 section 5.7).</param>
/// <param name="OdometerM">The odometer in metres (the sensor's miles times 1609.344).</param>
/// <param name="Ignition">The raw ignition text, lower-cased.</param>
/// <param name="RemoteStartSeconds">Seconds left of a running remote start.</param>
public record VehicleSample(
    string VehicleId,
    DateTimeOffset Ts,
    double? OdometerM = null,
    int? FuelPct = null,
    string? Ignition = null,
    string? Gear = null,
    double? SpeedMps = null,
    int? RemoteStartSeconds = null,
    double? Lat = null,
    double? Lon = null);
