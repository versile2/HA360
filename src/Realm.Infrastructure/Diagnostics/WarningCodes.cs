namespace Realm.Infrastructure.Diagnostics;

/// <summary>
/// The nine fixed warning codes of <c>diagnostics.json</c> (03 section 2.11), in the order the specification lists them. A code is a word for the operator, never
/// prose, and carries no value: the file says that something is wrong, the log says what.
/// </summary>
public static class WarningCodes
{
    /// <summary>The <c>HomeAssistant</c> connection entry reads Unavailable. A host whose options were refused reads it at once (02 section 10.8).</summary>
    public const string HaUnavailable = "ha_unavailable";

    /// <summary>Home Assistant answered <c>auth_invalid</c> and the websocket is in its <c>AuthFailed</c> state.</summary>
    public const string HaAuthFailed = "ha_auth_failed";

    /// <summary>The websocket has not been connected for more than 60 seconds.</summary>
    public const string HaWebsocketDownOver60s = "ha_ws_down_over_60s";

    /// <summary>The ingestion count of dropped items and rows is above zero.</summary>
    public const string IngestDrops = "ingest_drops";

    /// <summary>The database writer's queue holds more than 80 % of its bound.</summary>
    public const string WriterQueueOver80Pct = "writer_queue_over_80pct";

    /// <summary>The previous run did not stop cleanly (<c>meta.clean_shutdown</c> was <c>0</c> at start).</summary>
    public const string UncleanShutdown = "unclean_shutdown";

    /// <summary>The start-up self-check, or the zone Home Assistant reports, could not be resolved against the time zone data of this machine.</summary>
    public const string ZoneDataMissing = "zone_data_missing";

    /// <summary>The map script and the server disagree on the payload schema (a stale cached script).</summary>
    public const string PayloadSchemaMismatch = "payload_schema_mismatch";

    /// <summary>A vehicle's last refresh is older than <c>ui_vehicle_stale_after_minutes</c>.</summary>
    public const string VehicleSensorStale = "vehicle_sensor_stale";
}
