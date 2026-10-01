namespace Realm.Domain;

/// <summary>How current a member's or vehicle's position is. Decided by the data layer, never by the UI.</summary>
/// <remarks>A vehicle only ever uses Fresh, Stale and NoFix.</remarks>
public enum Freshness
{
    Fresh,
    Stale,
    Offline,
    NoFix,
    Static,
}
