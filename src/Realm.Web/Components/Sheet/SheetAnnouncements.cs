using System.Globalization;
using Realm.Domain;
using Realm.Web.Layout;
using Realm.Web.State;

namespace Realm.Web.Components.Sheet;

/// <summary>
/// The strings of the sheet's one polite live region (01 section 10.3): a selection ("Showing Cass"), a section change ("4 drivers") and a size change ("List opened" at 80 % with nothing selected,
/// "Details opened" at 80 % with a selection, "List collapsed" at Peek). They are derived from the transition between two <see cref="SheetState"/>s, so a tap, the Android Back, Esc and the
/// bottom navigation's re-tap announce the same things. Time-based strings (the "Here for" chip, relative times) are never announced: they update silently. Pure.
/// </summary>
public static class SheetAnnouncements
{
    /// <summary>80 % with a selection: the detail.</summary>
    public const string DetailsOpened = "Details opened";

    /// <summary>80 % with nothing selected: the list.</summary>
    public const string ListOpened = "List opened";

    /// <summary>Peek: the sheet came down.</summary>
    public const string ListCollapsed = "List collapsed";

    /// <summary>"Showing Cass": the name is the entity's display name (a place's display name).</summary>
    public static string Showing(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        return "Showing " + name;
    }

    /// <summary>"5 drivers", "1 driver", "2 vehicles", "14 places": what the section that now shows holds.</summary>
    public static string SectionCount(Section section, int drivers, int vehicles, int places) =>
        section switch
        {
            Section.Vehicles => Count(vehicles, "vehicle", "vehicles"),
            Section.Places => Count(places, "place", "places"),
            _ => Count(drivers, "driver", "drivers"),
        };

    /// <summary>
    /// What the live region says after the sheet went from <paramref name="before"/> to <paramref name="after"/>: null when there is nothing to say and the region stays as it is, the empty string
    /// when the selection was cleared (the region is emptied so that the same "Showing Cass" can be said again), otherwise the string. A new selection says only "Showing {name}", however the sheet
    /// moved with it ("a selection that collapses the sheet announces only 'Showing Cass'"); a section change says the count; a size change, in the Compact layout only (the panel has no size),
    /// says what the sheet became.
    /// </summary>
    /// <param name="before">The state the last announcement was made for.</param>
    /// <param name="after">The state now.</param>
    /// <param name="compact">True in the Compact layout, the only one with a sheet size.</param>
    /// <param name="members">The people, to name a selected member.</param>
    /// <param name="vehicles">The vehicles, to name a selected vehicle and to count a section.</param>
    /// <param name="places">The places, to name a selected place and to count a section.</param>
    public static string? Between(
        SheetState before,
        SheetState after,
        bool compact,
        IReadOnlyList<MemberVm> members,
        IReadOnlyList<VehicleVm> vehicles,
        IReadOnlyList<PlaceVm> places)
    {
        ArgumentNullException.ThrowIfNull(members);
        ArgumentNullException.ThrowIfNull(vehicles);
        ArgumentNullException.ThrowIfNull(places);
        if (after.Selection is { } selected && selected != before.Selection)
        {
            return NameOf(selected, members, vehicles, places) is { } name ? Showing(name) : null;
        }

        if (after.Section != before.Section)
        {
            return SectionCount(after.Section, members.Count, vehicles.Count, places.Count);
        }

        if (compact && after.Size != before.Size)
        {
            return after.Size == SheetSize.Peek ? ListCollapsed : after.Selection is null ? ListOpened : DetailsOpened;
        }

        return after.Selection is null && before.Selection is not null ? string.Empty : null;
    }

    private static string Count(int count, string one, string many) => count.ToString(CultureInfo.InvariantCulture) + " " + (count == 1 ? one : many);

    // The display name, as the pin and the header say it first; an entity that has left the lists has no name and so nothing is announced.
    private static string? NameOf(EntityRef selected, IReadOnlyList<MemberVm> members, IReadOnlyList<VehicleVm> vehicles, IReadOnlyList<PlaceVm> places) =>
        selected.Kind switch
        {
            EntityKind.Member => members.FirstOrDefault(member => string.Equals(member.Id, selected.Id, StringComparison.Ordinal))?.DisplayName,
            EntityKind.Vehicle => vehicles.FirstOrDefault(vehicle => string.Equals(vehicle.Id, selected.Id, StringComparison.Ordinal))?.Name,
            EntityKind.Place => places.FirstOrDefault(place => string.Equals(place.Id, selected.Id, StringComparison.Ordinal))?.DisplayName,
            _ => null,
        };
}
