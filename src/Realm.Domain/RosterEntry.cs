namespace Realm.Domain;

/// <summary>
/// One row of the roster (02 section 7.2, table <c>roster</c>): something Home Assistant reports that can be on the map, the group the owner (or the
/// lifecycle rules) put it in, and what the owner calls it. Times are UTC instants.
/// </summary>
/// <param name="EntityId">The primary key: <c>person.alden</c> or <c>device_tracker.pixel_8</c>.</param>
/// <param name="Group">People, Vehicles or Not tracked.</param>
/// <param name="DisplayName">Shown on the map and in lists. Set when the entry is found; the owner may change it.</param>
/// <param name="LoreTitle">The optional second label under the name.</param>
/// <param name="Color">A <c>#RRGGBB</c> colour, set when the entry is found; the owner may change it.</param>
/// <param name="SortOrder">The position inside the group, lowest first.</param>
/// <param name="Source">The word for where it comes from: <c>Home Assistant</c>, <c>Life360</c> or <c>Home Assistant + Life360</c>.</param>
/// <param name="FirstSeenUtc">When the entry was found.</param>
/// <param name="LastActiveUtc">The last discovery at which it reported a usable state (not unavailable or unknown).</param>
/// <param name="AutoMovedUtc">When the lifecycle rules moved it to Not tracked; null when it was never moved automatically or the owner has moved it since.</param>
public sealed record RosterEntry(
    string EntityId,
    RosterKind Kind,
    RosterGroup Group,
    string DisplayName,
    string? LoreTitle,
    string Color,
    int SortOrder,
    string Source,
    DateTimeOffset FirstSeenUtc,
    DateTimeOffset LastActiveUtc,
    DateTimeOffset? AutoMovedUtc)
{
    /// <summary>The member or vehicle id derived from the entity id: stable and safe in a URL (<c>person.alden</c> is <c>alden</c>, <c>device_tracker.pixel_8</c> is <c>tracker_pixel_8</c>).</summary>
    public string Id => IdOf(EntityId);

    /// <summary>The id of the member or vehicle that an entity becomes.</summary>
    public static string IdOf(string entityId)
    {
        var dot = entityId.IndexOf('.', StringComparison.Ordinal);
        if (dot < 0)
        {
            return entityId;
        }

        var domain = entityId[..dot];
        var objectId = entityId[(dot + 1)..];
        return domain switch
        {
            "person" => objectId,
            "device_tracker" => "tracker_" + objectId,
            _ => domain + "_" + objectId,
        };
    }
}
