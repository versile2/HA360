using Realm.Domain;

namespace Realm.Infrastructure.Options;

/// <summary>One entry of the <c>places</c> option (02 sections 3.1 and 3.2). A blank optional text reads as null.</summary>
/// <param name="Zone">A zone entity id such as <c>zone.work_2</c>, or an exact HA zone name.</param>
/// <param name="Kind">The icon family; <see cref="PlaceKind.Other"/> when unset.</param>
/// <param name="Hidden">Defaults to false.</param>
public sealed record PlaceOption(string Zone, string? DisplayName, string? Subtitle, PlaceKind Kind, bool Hidden);
