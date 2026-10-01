namespace Realm.Domain;

/// <summary>The zones an entity is in, and the one it is listed under.</summary>
/// <param name="ZoneIds">Every zone the entity is in, smallest zone first.</param>
/// <param name="PlaceId">The smallest zone; null when the entity is in none.</param>
public record PlaceMembership(
    IReadOnlyList<string> ZoneIds,
    string? PlaceId);
