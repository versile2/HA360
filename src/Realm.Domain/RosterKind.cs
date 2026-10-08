namespace Realm.Domain;

/// <summary>What a roster entry is in Home Assistant.</summary>
public enum RosterKind
{
    /// <summary>A <c>person</c> entity, merged with its device trackers.</summary>
    Person,

    /// <summary>A <c>device_tracker</c> with a GPS position that no person owns.</summary>
    Tracker,
}
