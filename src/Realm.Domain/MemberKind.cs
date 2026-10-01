namespace Realm.Domain;

/// <summary>Live members share a position; a static member is a fixed pin.</summary>
public enum MemberKind
{
    Live,
    Static,
}
