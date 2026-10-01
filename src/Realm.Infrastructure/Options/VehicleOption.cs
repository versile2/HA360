using Realm.Domain;

namespace Realm.Infrastructure.Options;

/// <summary>One entry of the <c>vehicles</c> option (02 sections 3.1 and 2.4). A blank optional text reads as null.</summary>
/// <param name="Integration">The lower-case integration: <c>fordpass</c> or <c>none</c> (a placeholder).</param>
/// <param name="EntityPrefix">The FordPass entity prefix, such as <c>fordpass_demo</c>; required when <paramref name="Integration"/> is <c>fordpass</c>.</param>
/// <param name="SortOrder">Null when unset, which means the order of the list.</param>
public sealed record VehicleOption(
    string Id,
    string Name,
    string? LoreTitle,
    VehicleGlyph Glyph,
    string Integration,
    string? EntityPrefix,
    string? PlaceholderNote,
    int? SortOrder);
