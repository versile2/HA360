namespace Realm.Domain;

/// <summary>One Home Assistant entity that feeds a roster entry, as Settings shows it to tell the entry apart: the entity id and its friendly name.</summary>
/// <param name="EntityId">For example <c>person.alden</c> or <c>device_tracker.pixel_8</c>.</param>
/// <param name="FriendlyName">The entity's <c>friendly_name</c> attribute; null when it has none.</param>
/// <param name="Role">What the entity is to the entry: <c>Person</c>, <c>Phone app tracker</c>, <c>Life360 tracker</c> or <c>Tracker</c>.</param>
public sealed record RosterIdentityEntity(string EntityId, string? FriendlyName, string Role);

/// <summary>
/// Who a roster entry is in Home Assistant and Life360 (0.2.1, D117): the entities behind it with their friendly names, the Life360 member name when Life360 feeds it,
/// and whether the source has a picture. Read from the last discovery and kept in memory only; it is empty until the first discovery has run.
/// </summary>
/// <param name="Entities">The person and trackers behind the entry; the entry's own entity first.</param>
/// <param name="Life360Name">The member's name in Life360 (the tracker's friendly name without its "Life360 " prefix); null when Life360 does not feed the entry.</param>
/// <param name="HasPhoto">The source has a picture the avatar proxy can serve.</param>
public sealed record RosterIdentity(IReadOnlyList<RosterIdentityEntity> Entities, string? Life360Name, bool HasPhoto)
{
    /// <summary>No information (the Demo's trackers, or before the first discovery).</summary>
    public static readonly RosterIdentity Unknown = new([], null, false);
}
