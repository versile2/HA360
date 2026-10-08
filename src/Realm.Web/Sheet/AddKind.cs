using Realm.Domain;

namespace Realm.Web.Sheet;

/// <summary>What the last row of a sheet list adds (0.2.2, D119): a driver, a tracker or a place.</summary>
public enum AddKind
{
    /// <summary>Drivers tab: moves a person from Not tracked into People.</summary>
    Driver,

    /// <summary>Trackers tab: moves a tracker from Not tracked into Trackers.</summary>
    Tracker,

    /// <summary>Places tab: places a new Home Assistant zone on the map.</summary>
    Place,
}

/// <summary>The words and test ids of the three Add rows. The label is one text node starting with a plus sign (D120).</summary>
public static class AddKinds
{
    /// <summary>The Add row of the section.</summary>
    public static AddKind For(Section section) =>
        section switch
        {
            Section.Vehicles => AddKind.Tracker,
            Section.Places => AddKind.Place,
            _ => AddKind.Driver,
        };

    /// <summary>The row's text: "+ Add driver", "+ Add tracker" or "+ Add place".</summary>
    public static string Label(AddKind kind) =>
        kind switch
        {
            AddKind.Tracker => "+ Add tracker",
            AddKind.Place => "+ Add place",
            _ => "+ Add driver",
        };

    /// <summary>The test id of the row (<c>add-driver</c>, <c>add-tracker</c>, <c>add-place</c>); never <c>row-</c>, which is the id of list rows.</summary>
    public static string TestId(AddKind kind) =>
        kind switch
        {
            AddKind.Tracker => "add-tracker",
            AddKind.Place => "add-place",
            _ => "add-driver",
        };

    /// <summary>The dialog title for the two pickers.</summary>
    public static string Title(AddKind kind) => kind == AddKind.Tracker ? "Add tracker" : "Add driver";

    /// <summary>The group the pick moves the entry into.</summary>
    public static RosterGroup Target(AddKind kind) => kind == AddKind.Tracker ? RosterGroup.Vehicles : RosterGroup.People;
}

/// <summary>What the placement panel collected: the name (trimmed, not empty) and the kind whose icon the zone gets.</summary>
/// <param name="Name">The name.</param>
/// <param name="Kind">The kind of place.</param>
public sealed record PlacementDraft(string Name, PlaceKind Kind);
