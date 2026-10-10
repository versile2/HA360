using Realm.Domain;
using Realm.Web.Map;

namespace Realm.Web.Sheet;

/// <summary>How the detail line (L3, L4) of a row is drawn: plain, in the warning colour with a clock icon (stale), or in the stale colour (offline, last heard).</summary>
public enum LineTone
{
    /// <summary>Secondary text.</summary>
    Normal,

    /// <summary>The warning colour, with a clock icon (a stale member, a stale vehicle).</summary>
    Warning,

    /// <summary>The stale colour (an offline member).</summary>
    Stale,
}

/// <summary>
/// The battery pill of 01 section 5.1 (and of the Peek selection header, which reuses it): "19%", a bolt when charging, red-tinted when low. <paramref name="AccessibleName"/>
/// carries "Battery 12 percent, low" and the "(battery as of …)" clause when the reading is old.
/// </summary>
public sealed record BatteryBadgeVm(int Percent, string Text, bool Charging, bool Low, string AccessibleName);

/// <summary>One person's row (01 section 5.1). Every string is final; the component only lays them out.</summary>
/// <param name="Id">The role id (<c>king</c>): the test id is <c>row-member-{Id}</c>.</param>
/// <param name="Name">L1, bold.</param>
/// <param name="Lore">L1, secondary; null when the member has no lore title.</param>
/// <param name="Initial">The avatar's letter, shown on <paramref name="Color"/> under the photo.</param>
/// <param name="Color">A validated <c>#RRGGBB</c> member colour, or null (the surface colour is used).</param>
/// <param name="AvatarUrl">The photo (the app's avatar proxy), or null.</param>
/// <param name="Status">The status class: stale and offline rows are drawn at 72 and 60 percent opacity.</param>
/// <param name="StatusLine">L2.</param>
/// <param name="DetailLine">L3.</param>
/// <param name="DetailTone">How L3 is drawn.</param>
/// <param name="Battery">The pill; null when the battery is unknown and for the static prince.</param>
/// <param name="AccessibleName">The pin's accessible name (01 section 10.3), without "Double tap to show on map." which the component adds.</param>
/// <param name="Glyph">The glyph the owner chose instead of the initial (0.2.1); null otherwise.</param>
public sealed record MemberRowVm(
    string Id,
    string Name,
    string? Lore,
    string Initial,
    string? Color,
    string? AvatarUrl,
    MemberStatus Status,
    string StatusLine,
    string DetailLine,
    LineTone DetailTone,
    BatteryBadgeVm? Battery,
    string AccessibleName,
    VehicleGlyph? Glyph = null)
{
    /// <summary>The test id of 01 Appendix B.</summary>
    public string TestId => "row-member-" + Id;
}

/// <summary>One vehicle's row (01 section 5.2); a location line and an update line.</summary>
/// <param name="Id">The role id (<c>wagon</c>): the test id is <c>row-vehicle-{Id}</c>.</param>
/// <param name="Name">L1, bold.</param>
/// <param name="Lore">L1, secondary.</param>
/// <param name="Glyph">The avatar's glyph: the pickup silhouette or the car icon.</param>
/// <param name="LocationLine">L2.</param>
/// <param name="Updated">L4 ("Updated 20 min ago", "Last heard 1 hr ago"); empty when unknown.</param>
/// <param name="UpdatedTone">Warning when the vehicle is stale.</param>
/// <param name="AccessibleName">The accessible name, without "Double tap to show on map.".</param>
public sealed record VehicleRowVm(
    string Id,
    string Name,
    string? Lore,
    VehicleGlyph Glyph,
    string LocationLine,
    string Updated,
    LineTone UpdatedTone,
    string AccessibleName)
{
    /// <summary>The test id of 01 Appendix B.</summary>
    public string TestId => "row-vehicle-" + Id;
}

/// <summary>A 24 px person on a place row.</summary>
public sealed record MiniAvatarVm(string Name, string Initial, string? Color, string? AvatarUrl);

/// <summary>One place's row (01 section 5.3): the count is people only (R2-009).</summary>
/// <param name="Id">The zone id: the test id is <c>row-place-{Id}</c>.</param>
/// <param name="Name">L1 (the list label, with its " (2)" suffix when the name repeats).</param>
/// <param name="Subtitle">L2; empty when there is none.</param>
/// <param name="Kind">The zone kind, which picks the icon.</param>
/// <param name="People">How many people are inside (<c>MemberIdsInside</c>); a vehicle never counts.</param>
/// <param name="CountText">"1 here", "2 here" or "Empty".</param>
/// <param name="Avatars">The first three people inside, as mini avatars.</param>
/// <param name="More">How many more people than avatars (the "+N").</param>
/// <param name="AccessibleName">"Hearth Haven, Home. 1 here: Alden." without "Double tap to show on map.".</param>
public sealed record PlaceRowVm(
    string Id,
    string Name,
    string Subtitle,
    PlaceKind Kind,
    int People,
    string CountText,
    IReadOnlyList<MiniAvatarVm> Avatars,
    int More,
    string AccessibleName)
{
    /// <summary>The test id of 01 Appendix B.</summary>
    public string TestId => "row-place-" + Id;
}
