using System.Globalization;
using System.Text.Json;

namespace Realm.Domain;

/// <summary>
/// The one parser from Home Assistant entity states to the data layer's records (02 section 1.6): the only
/// place where units are converted and fixes are filtered. Every path (websocket, REST bootstrap, history) maps
/// to <see cref="HaEntitySnapshot"/> first. Pure: the clock arrives as a parameter.
/// </summary>
public static class FixParser
{
    /// <summary>Home Assistant's Life360 and FordPass speeds are mph; this converts them to m/s, the only factor used.</summary>
    public const double MphToMps = 0.44704;

    private const double MilesToMetres = 1609.344;
    private const double KilometresToMetres = 1000;
    private const double KilometresPerHourToMps = 1 / 3.6;

    // Life360 reports a constant 15.2 m (50 ft) accuracy on every fix: a placeholder, not a measurement.
    private const double Life360AccuracyPlaceholderM = 15.2;
    private const double Life360AccuracyPlaceholderToleranceM = 0.05;

    // FordPass sends 0,0 bursts: a position within half a degree of the origin is not a fix.
    private const double NullIslandDegrees = 0.5;
    private const double VehicleMaxPlausibleSpeedMps = 60;
    private const double RestartEchoMinDistanceM = 1;

    private static readonly TimeSpan DuplicateWindow = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Parses one tracker state into a fix, or null when the state is not a new, valid fix.
    /// </summary>
    /// <param name="snapshot">A device_tracker state.</param>
    /// <param name="source">The kind of tracker, decided by the caller from discovery.</param>
    /// <param name="now">Used only to clamp a timestamp in the future to now.</param>
    /// <param name="previous">The last accepted (or, on the first snapshot, last stored) fix of this source for this entity; null if none.</param>
    /// <param name="firstSnapshot">True for the first snapshot after an HA or add-on start: rule F0 then treats a companion state within 1 m of <paramref name="previous"/> as an echo of the restored state.</param>
    public static RawFix? ParseTracker(
        HaEntitySnapshot snapshot,
        FixSource source,
        DateTimeOffset now,
        RawFix? previous = null,
        bool firstSnapshot = false)
    {
        if (IsUnavailable(snapshot.State))
        {
            return null;
        }

        var attributes = snapshot.Attributes;
        if (Number(attributes, "latitude") is not { } lat || Number(attributes, "longitude") is not { } lon)
        {
            return null;
        }

        if (lat is < -90 or > 90 || lon is < -180 or > 180)
        {
            return null;
        }

        if (source == FixSource.FordPass && Math.Abs(lat) < NullIslandDegrees && Math.Abs(lon) < NullIslandDegrees)
        {
            return null;
        }

        // Life360: the tracker's own last_seen, never last_updated, which an HA restart resets and would fake freshness.
        var observed = source == FixSource.Life360 ? Instant(attributes, "last_seen") : snapshot.LastUpdatedUtc?.ToUniversalTime();
        if (observed is not { } ts)
        {
            return null;
        }

        if (ts > now)
        {
            ts = now;
        }

        if (previous is not null && IsRepeat(previous, source, lat, lon, ts, firstSnapshot))
        {
            return null;
        }

        var battery = Percent(Number(attributes, "battery_level"));
        return new RawFix(
            EntityId: snapshot.EntityId,
            Source: source,
            Ts: ts,
            Lat: lat,
            Lon: lon,
            AccuracyM: Accuracy(source, Number(attributes, "gps_accuracy")),
            SpeedMps: Speed(source, Number(attributes, "speed")),
            HeadingDeg: Number(attributes, "course"),
            AltitudeM: Number(attributes, "altitude"),
            BatteryPct: battery,
            Charging: battery is null ? null : Flag(attributes, "battery_charging"),
            BatteryAsOfUtc: battery is null ? null : ts,
            Driving: source == FixSource.Life360 ? Flag(attributes, "driving") : null,
            Address: source == FixSource.Life360 ? Text(attributes, "address") : null);
    }

    /// <summary>
    /// Parses the sensors of one FordPass vehicle (entity ids sensor.{prefix}_odometer, _fuel, _ignitionstatus,
    /// _speed, _remotestartcountdown and _lastrefresh). A missing or unavailable sensor gives null for its field.
    /// </summary>
    /// <param name="entityPrefix">The vehicle's entity_prefix option, for example fordpass_xxxx.</param>
    /// <param name="sensors">Any entity states; those that are not this vehicle's sensors are ignored.</param>
    public static RawVehicleState ParseVehicleState(string entityPrefix, IEnumerable<HaEntitySnapshot> sensors)
    {
        var byId = new Dictionary<string, HaEntitySnapshot>(StringComparer.Ordinal);
        foreach (var sensor in sensors)
        {
            byId[sensor.EntityId] = sensor;
        }

        HaEntitySnapshot? Find(string name) => byId.GetValueOrDefault($"sensor.{entityPrefix}_{name}");

        var odometer = Find("odometer");
        var speed = Find("speed");
        var countdownMinutes = NumericState(Find("remotestartcountdown"));
        int? remoteStartSeconds = countdownMinutes is { } minutes
            ? (int)Math.Round(minutes * 60, MidpointRounding.AwayFromZero)
            : null;

        // The vehicle's sample clock is its lastrefresh sensor; without it, the newest update among its sensors.
        var ownSensors = new[] { "odometer", "fuel", "ignitionstatus", "speed", "remotestartcountdown", "lastrefresh" }
            .Select(Find)
            .OfType<HaEntitySnapshot>()
            .ToList();
        var lastRefresh = Find("lastrefresh") is { } refresh ? IsoInstant(refresh.State) : null;

        return new RawVehicleState(
            LastUpdateUtc: lastRefresh ?? ownSensors.Max(s => s.LastUpdatedUtc)?.ToUniversalTime(),
            OdometerM: NumericState(odometer) is { } odometerReading ? odometerReading * (UnitIs(odometer, "km") ? KilometresToMetres : MilesToMetres) : null,
            FuelPct: Percent(NumericState(Find("fuel"))),
            Ignition: Ignition(Find("ignitionstatus")?.State, countdownRunning: countdownMinutes > 0),
            RemoteStartSecondsLeft: remoteStartSeconds > 0 ? remoteStartSeconds : null,
            SpeedMps: NumericState(speed) is { } speedReading ? speedReading * (UnitIs(speed, "km/h") ? KilometresPerHourToMps : MphToMps) : null);
    }

    /// <summary>Parses a zone state, or returns null when it is not a zone with a position and a radius.</summary>
    public static RawPlace? ParseZone(HaEntitySnapshot snapshot)
    {
        const string prefix = "zone.";
        if (!snapshot.EntityId.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var attributes = snapshot.Attributes;
        if (Number(attributes, "latitude") is not { } lat
            || Number(attributes, "longitude") is not { } lon
            || Number(attributes, "radius") is not { } radius)
        {
            return null;
        }

        if (lat is < -90 or > 90 || lon is < -180 or > 180 || radius < 0)
        {
            return null;
        }

        var id = snapshot.EntityId[prefix.Length..];
        return new RawPlace(
            Id: id,
            Name: Text(attributes, "friendly_name") ?? id,
            Lat: lat,
            Lon: lon,
            RadiusM: radius,
            Passive: Flag(attributes, "passive") ?? false);
    }

    private static bool IsRepeat(RawFix previous, FixSource source, double lat, double lon, DateTimeOffset ts, bool firstSnapshot)
    {
        var samePosition = previous.Lat == lat && previous.Lon == lon;
        var apart = (ts - previous.Ts).Duration();
        switch (source)
        {
            case FixSource.Life360:
                // Attributes with the same last_seen are a battery or wifi change, not a new fix.
                return ts == previous.Ts;
            case FixSource.Companion:
                // Rule F0: on the first snapshot after a restart, a state within 1 m of the last stored fix is an echo.
                return (samePosition && apart <= DuplicateWindow)
                    || (firstSnapshot && Geo.DistanceM(previous.Lat, previous.Lon, lat, lon) < RestartEchoMinDistanceM);
            default:
                if (samePosition && apart <= DuplicateWindow)
                {
                    return true;
                }

                // Implied speed above 60 m/s from the last accepted fix: a spike, not movement.
                var distance = Geo.DistanceM(previous.Lat, previous.Lon, lat, lon);
                return apart == TimeSpan.Zero
                    ? distance > 0
                    : distance / apart.TotalSeconds > VehicleMaxPlausibleSpeedMps;
        }
    }

    private static double? Accuracy(FixSource source, double? metres) => source switch
    {
        FixSource.Life360 => metres is { } m && Math.Abs(m - Life360AccuracyPlaceholderM) < Life360AccuracyPlaceholderToleranceM ? null : metres,
        FixSource.Companion => metres,
        _ => null,
    };

    private static double? Speed(FixSource source, double? raw) => source switch
    {
        FixSource.Life360 => raw * MphToMps,
        FixSource.Companion => raw,
        _ => null,
    };

    private static IgnitionState? Ignition(string? text, bool countdownRunning) => text?.Trim().ToUpperInvariant() switch
    {
        "OFF" => IgnitionState.Off,
        "ACCESSORY" => IgnitionState.Accessory,
        "ON" or "RUN" or "START" => countdownRunning ? IgnitionState.RemoteStart : IgnitionState.On,
        _ => null,
    };

    private static bool IsUnavailable(string state) =>
        string.Equals(state, "unavailable", StringComparison.OrdinalIgnoreCase)
        || string.Equals(state, "unknown", StringComparison.OrdinalIgnoreCase);

    private static bool UnitIs(HaEntitySnapshot? snapshot, string unit) =>
        snapshot is not null && string.Equals(Text(snapshot.Attributes, "unit_of_measurement"), unit, StringComparison.OrdinalIgnoreCase);

    // A whole percent from 0 to 100; anything else is unknown.
    private static int? Percent(double? value) =>
        value is >= 0 and <= 100 ? (int)Math.Round(value.Value, MidpointRounding.AwayFromZero) : null;

    private static double? NumericState(HaEntitySnapshot? snapshot) =>
        snapshot is not null
        && double.TryParse(snapshot.State, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
        && double.IsFinite(value)
            ? value
            : null;

    private static double? Number(IReadOnlyDictionary<string, JsonElement> attributes, string key) =>
        attributes.TryGetValue(key, out var element)
        && element.ValueKind == JsonValueKind.Number
        && element.TryGetDouble(out var value)
        && double.IsFinite(value)
            ? value
            : null;

    private static bool? Flag(IReadOnlyDictionary<string, JsonElement> attributes, string key) =>
        attributes.TryGetValue(key, out var element) && element.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? element.GetBoolean()
            : null;

    private static string? Text(IReadOnlyDictionary<string, JsonElement> attributes, string key)
    {
        if (!attributes.TryGetValue(key, out var element) || element.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var text = element.GetString();
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    // An instant given as epoch seconds or as an ISO 8601 string (02 documents both for last_seen).
    private static DateTimeOffset? Instant(IReadOnlyDictionary<string, JsonElement> attributes, string key)
    {
        if (!attributes.TryGetValue(key, out var element))
        {
            return null;
        }

        if (element.ValueKind == JsonValueKind.String)
        {
            return IsoInstant(element.GetString());
        }

        if (element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out var seconds) && double.IsFinite(seconds))
        {
            var milliseconds = Math.Round(seconds * 1000);
            return milliseconds is >= -62_135_596_800_000 and <= 253_402_300_799_999
                ? DateTimeOffset.FromUnixTimeMilliseconds((long)milliseconds)
                : null;
        }

        return null;
    }

    private static DateTimeOffset? IsoInstant(string? text) =>
        DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var value)
            ? value.ToUniversalTime()
            : null;
}
