namespace Realm.Domain;

/// <summary>Where a fix came from. A static pin is never a fix, so it has no value here. A vehicle's tracker is read as a <see cref="Companion"/> source (it is never stored).</summary>
public enum FixSource
{
    Life360,
    Companion,
}
