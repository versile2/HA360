namespace Realm.Domain;

/// <summary>A selected member, vehicle or place.</summary>
public record EntityRef(
    EntityKind Kind,
    string Id);
