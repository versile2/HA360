using Realm.Domain;

namespace Realm.Demo;

/// <summary>One vehicle of the fictional demo cast (02 section 9.2).</summary>
/// <param name="Id">The role id: wagon or chariot.</param>
/// <param name="Lore">The lore title shown beside the name.</param>
/// <param name="PlaceholderNote">The note of a vehicle with no integration; null for a connected one.</param>
public record DemoVehicle(
    string Id,
    string Name,
    string Lore,
    VehicleGlyph Glyph,
    int SortOrder,
    bool IsPlaceholder,
    string? PlaceholderNote);
