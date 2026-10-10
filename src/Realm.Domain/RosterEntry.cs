namespace Realm.Domain;

/// <summary>
/// One row of the roster (02 section 7.2, table <c>roster</c>): something Home Assistant reports that can be on the map, the group the owner (or the
/// lifecycle rules) put it in, and what the owner calls it. Times are UTC instants.
/// </summary>
/// <param name="EntityId">The primary key: <c>person.alden</c> or <c>device_tracker.pixel_8</c>.</param>
/// <param name="Group">People, Trackers (the <see cref="RosterGroup.Vehicles"/> value) or Not tracked.</param>
/// <param name="DisplayName">The name in effect, shown on the map, in lists and in reports: the owner's <see cref="NameOverride"/> when there is one, else <see cref="SourceName"/>.</param>
/// <param name="LoreTitle">The title in effect (the optional second label under the name): <see cref="TitleOverride"/> when there is one, else <see cref="SourceTitle"/>.</param>
/// <param name="Color">The colour in effect (<c>#RRGGBB</c>): <see cref="ColorOverride"/> when there is one, else <see cref="SourceColor"/>.</param>
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
    /// <summary>The name Home Assistant (or Life360) gives it, as of the last discovery. Null on a row stored by 0.2.0 until the first discovery of 0.2.1.</summary>
    public string? SourceName { get; init; }

    /// <summary>The title the source gives it (Home Assistant has none, so null outside the Demo).</summary>
    public string? SourceTitle { get; init; }

    /// <summary>The colour it took when it was found.</summary>
    public string? SourceColor { get; init; }

    /// <summary>The owner's name; wins over <see cref="SourceName"/> and survives later changes in Home Assistant or Life360. Null: none set.</summary>
    public string? NameOverride { get; init; }

    /// <summary>The owner's title; null: none set.</summary>
    public string? TitleOverride { get; init; }

    /// <summary>The owner's colour (<c>#RRGGBB</c>); null: none set.</summary>
    public string? ColorOverride { get; init; }

    /// <summary>The owner's choice of picture (see <see cref="RosterIcons"/>); null: automatic (the photo when the source has one, else the initial or a car).</summary>
    public string? Icon { get; init; }

    /// <summary>True when the owner changed anything that "Reset to Home Assistant / Life360" would undo.</summary>
    public bool IsCustomised => NameOverride is not null || TitleOverride is not null || ColorOverride is not null || Icon is not null;

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
