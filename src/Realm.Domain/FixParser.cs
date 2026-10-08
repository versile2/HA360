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
    /// <summary>Home Assistant's Life360 speeds are mph; this converts them to m/s, the only factor used.</summary>
    public const double MphToMps = 0.44704;

    // Life360 reports a constant 15.2 m (50 ft) accuracy on every fix: a placeholder, not a measurement.
    private const double Life360AccuracyPlaceholderM = 15.2;
    private const double Life360AccuracyPlaceholderToleranceM = 0.05;

    private const double RestartEchoMinDistanceM = 1;

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
        if (source == FixSource.Life360)
        {
            // Attributes with the same last_seen are a battery or wifi change, not a new fix.
            return ts == previous.Ts;
        }

        // A companion fix is the state's update time "when coordinates changed" (02 section 1.6): the same coordinates, however long after, are an
        // attribute-only update (accuracy, altitude, battery) and not a new fix. Counting them would make a stationary phone look fresher than its
        // last position report and bias the heartbeat estimate (02 section 4.7) low.
        // Rule F0: on the first snapshot after a restart, a state within 1 m of the last stored fix is an echo.
        return samePosition
            || (firstSnapshot && Geo.DistanceM(previous.Lat, previous.Lon, lat, lon) < RestartEchoMinDistanceM);
    }

    private static double? Accuracy(FixSource source, double? metres) => source switch
    {
        FixSource.Life360 => metres is { } m && Math.Abs(m - Life360AccuracyPlaceholderM) < Life360AccuracyPlaceholderToleranceM ? null : metres,
        _ => metres,
    };

    private static double? Speed(FixSource source, double? raw) => source switch
    {
        FixSource.Life360 => raw * MphToMps,
        _ => raw,
    };

    private static bool IsUnavailable(string state) =>
        string.Equals(state, "unavailable", StringComparison.OrdinalIgnoreCase)
        || string.Equals(state, "unknown", StringComparison.OrdinalIgnoreCase);

    // A whole percent from 0 to 100; anything else is unknown.
    private static int? Percent(double? value) =>
        value is >= 0 and <= 100 ? (int)Math.Round(value.Value, MidpointRounding.AwayFromZero) : null;

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
