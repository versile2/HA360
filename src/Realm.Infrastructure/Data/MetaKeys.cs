namespace Realm.Infrastructure.Data;

/// <summary>The keys of the <c>meta</c> table that are written after the schema step (02 section 7.2, D58); the schema step writes the others.</summary>
public static class MetaKeys
{
    /// <summary>HA's IANA time zone id, written by the ingestion pipeline whenever discovery reports it (D66).</summary>
    public const string HaTimeZone = "ha_time_zone";
}
