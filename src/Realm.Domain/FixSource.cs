namespace Realm.Domain;

/// <summary>Where a fix came from. A static pin is never a fix, so it has no value here.</summary>
public enum FixSource
{
    Life360,
    Companion,
    FordPass,
}
