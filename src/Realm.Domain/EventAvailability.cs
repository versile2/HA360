namespace Realm.Domain;

/// <summary>Which drivers can have a count for an event type: every driver, only some, or none (no data source exists).</summary>
public enum EventAvailability
{
    All,
    Some,
    None,
}
