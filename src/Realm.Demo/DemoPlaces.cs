using Realm.Domain;

namespace Realm.Demo;

/// <summary>
/// The 15 zones of the demo fixture, 14 of them drawn (02 section 9.2, 01 Appendix A.3). Names are the demo mapping
/// only; production names come from the owner's options. Coordinates are fixture values used exactly as listed (D41).
/// </summary>
public static class DemoPlaces
{
    public static readonly DemoPlace Home = new("home", "Hearth Haven", "Hearth Haven", "Home", PlaceKind.Home, 31.0990, -85.3410, 100);
    public static readonly DemoPlace JesterHall = new("jester_hall", "The Jester's Hall", "The Jester's Hall", "Cass's house", PlaceKind.Family, 31.1040, -85.3560, 100);
    public static readonly DemoPlace Work = new("work", "Work", "Work", "The Counting House", PlaceKind.Work, 31.1530, -85.4080, 150);
    public static readonly DemoPlace Work2 = new("work_2", "Work", "Work (2)", "The Counting House", PlaceKind.Work, 31.1534, -85.4084, 150);
    public static readonly DemoPlace Park = new("park", "Park", "Park", "The Commons", PlaceKind.Park, 31.0720, -85.3290, 200);
    public static readonly DemoPlace Orrin = new("orrin", "Orrin's", "Orrin's", "Orrin's Stronghold", PlaceKind.Family, 31.0420, -85.3880, 120);
    public static readonly DemoPlace Mara = new("mara", "Mara's", "Mara's", "Mara's Manor", PlaceKind.Family, 31.1450, -85.2960, 120);
    public static readonly DemoPlace SkateOne = new("skate_one", "Rollerdome", "Rollerdome", "The Tourney Grounds", PlaceKind.Fun, 31.1760, -85.4700, 200);
    public static readonly DemoPlace SkateTwo = new("skate_two", "Rollerdome", "Rollerdome (2)", "The Tourney Grounds", PlaceKind.Fun, 31.2210, -85.5140, 200);
    public static readonly DemoPlace QueenOffice = new("queen_office", "Briar's Office", "Briar's Office", "The Queen's Counting House", PlaceKind.Work, 31.0200, -85.5200, 150);
    public static readonly DemoPlace Derby = new("derby", "Derby Hall", "Derby Hall", "The Joust", PlaceKind.Fun, 31.2550, -85.3400, 250);
    public static readonly DemoPlace Cemetery = new("cemetery", "Hollow Cemetery", "Hollow Cemetery", "The Quiet Fields", PlaceKind.Cemetery, 31.0300, -85.2900, 150);
    public static readonly DemoPlace Vet = new("vet", "Vet Clinic", "Vet Clinic", "The Beast Healer", PlaceKind.Vet, 31.1180, -85.4350, 100);
    public static readonly DemoPlace Wheels = new("wheels", "Wheel Hall", "Wheel Hall", "The Wheeled Hall", PlaceKind.Fun, 31.1900, -85.3300, 200);

    /// <summary>The 20 mile arrival circle centred on home: a fixture row the Demo itself does not use (the Demo shows the 14 others).</summary>
    public static readonly DemoPlace Approach = new("approach", "(arrival zone, never shown)", "(arrival zone, never shown)", "n/a", PlaceKind.Other, 31.0990, -85.3410, 32_187);

    /// <summary>All 15 zones in the order of the table.</summary>
    public static readonly IReadOnlyList<DemoPlace> All =
        [Home, JesterHall, Work, Work2, Park, Orrin, Mara, SkateOne, SkateTwo, QueenOffice, Derby, Cemetery, Vet, Wheels, Approach];

    /// <summary>The 14 zones the Demo draws and lists (every one but the fixture's arrival circle).</summary>
    public static readonly IReadOnlyList<DemoPlace> Drawn = All.Where(place => place != Approach).ToArray();
}
