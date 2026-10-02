using Realm.Domain;
using Realm.Web.Formatting;

namespace Realm.Web.Sheet;

/// <summary>
/// The Peek selection header of 01 section 3.4.2 (D45): the summary of the selected person, vehicle or place, every string final. <see cref="SelectionHeaderFormatter"/> builds it from the
/// same row view models and formatters as the list rows, so the header and the row never disagree; the component only lays it out. The same view model is the floating pill of the
/// hidden Expanded panel (01 section 3.6).
/// </summary>
/// <param name="Entity">What is selected. Its <see cref="EntityRef.Kind"/> picks the avatar: a photo or initial in a circle for a member, a glyph or kind icon in a rounded square otherwise.</param>
/// <param name="Line1">Line 1, bold: the person's name, the vehicle's name, the place's display name.</param>
/// <param name="Line1Lore">Line 1, secondary: the lore title ("The Royal Jester"); null for a place and for a member or vehicle without one. It ellipsises before the name.</param>
/// <param name="Line2Lead">Line 2, first part: the status line of a person, "{location} · {engine}" of a vehicle, "Here now (2)" or "Empty" of a place. It ellipsises before the tail.</param>
/// <param name="Line2Tail">Line 2, last part: "Since 9:06 pm", "Updated 3 min ago", "Last seen 42 min ago" or "Updated 20 min ago" / "Last heard 1 hr ago"; null when there is none.</param>
/// <param name="Initial">A member's avatar letter, shown on <paramref name="Color"/> under the photo; empty for a vehicle and a place.</param>
/// <param name="Color">A member's validated <c>#RRGGBB</c> colour, or null.</param>
/// <param name="AvatarUrl">A member's photo (the app's avatar proxy), or null.</param>
/// <param name="Glyph">A vehicle's glyph (the pickup silhouette or the car icon); null for a member and a place.</param>
/// <param name="ZoneKind">A place's kind, which picks its icon; null for a member and a vehicle.</param>
/// <param name="Battery">A member's battery pill (the row's, reused); null when the battery is unknown, for the static prince, and for a vehicle and a place.</param>
/// <param name="AccessibleName">The name of the group (01 section 10.3): the pin's accessible name for a member or vehicle, "{name}. Here now (2)." for a place. It carries the full facts the lines ellipsise.</param>
public sealed record SelectionHeaderVm(
    EntityRef Entity,
    string Line1,
    string? Line1Lore,
    string Line2Lead,
    string? Line2Tail,
    string Initial,
    string? Color,
    string? AvatarUrl,
    VehicleGlyph? Glyph,
    PlaceKind? ZoneKind,
    BatteryBadgeVm? Battery,
    string AccessibleName)
{
    /// <summary>Line 2 as one string: the lead, then " · " and the tail when there is one (01 section 8.4, "Peek selection header, line 2").</summary>
    public string Line2 => Line2Tail is null ? Line2Lead : Line2Lead + SelectionHeaderFormatter.Separator + Line2Tail;
}
