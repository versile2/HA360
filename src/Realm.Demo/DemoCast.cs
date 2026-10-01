using Realm.Domain;

namespace Realm.Demo;

/// <summary>
/// The fictional demo cast and every string of the fixture (02 section 9.2, D17, D28). Tests and the exporter read
/// strings from here and never retype them. Positions, ages and driving numbers are not strings and live in
/// <see cref="DemoDataSource"/>.
/// </summary>
public static class DemoCast
{
    /// <summary>The placeholder note of the second vehicle (01 Appendix A.2).</summary>
    public const string ChariotNote = "Awaiting the royal scribes (the maker's app)";

    public static readonly DemoMember King = new(
        Id: "king",
        Name: "Alden",
        Lore: "The King",
        Color: "#E8BC4E",
        Kind: MemberKind.Live,
        SortOrder: 0,
        PersonUserId: "demo-user-1",
        PhoneCapable: true,
        Address: null,
        StaticLabel: null);

    public static readonly DemoMember Queen = new(
        Id: "queen",
        Name: "Briar",
        Lore: "The Queen",
        Color: "#C792EA",
        Kind: MemberKind.Live,
        SortOrder: 1,
        PersonUserId: "demo-user-2",
        PhoneCapable: false,
        Address: "I-35",
        StaticLabel: null);

    public static readonly DemoMember Jester = new(
        Id: "jester",
        Name: "Cass",
        Lore: "The Royal Jester",
        Color: "#5CC8FF",
        Kind: MemberKind.Live,
        SortOrder: 2,
        PersonUserId: null,
        PhoneCapable: false,
        Address: "48 Larkspur Lane, Millbrook, TX",
        StaticLabel: null);

    public static readonly DemoMember Cryptid = new(
        Id: "cryptid",
        Name: "Dara",
        Lore: "The Court Cryptid",
        Color: "#FF8FB1",
        Kind: MemberKind.Live,
        SortOrder: 3,
        PersonUserId: null,
        PhoneCapable: false,
        Address: "Eastgate Avenue, Pinebrook, TX",
        StaticLabel: null);

    public static readonly DemoMember Prince = new(
        Id: "prince",
        Name: "Elio",
        Lore: "Prince of the Peaks",
        Color: "#7EE0A5",
        Kind: MemberKind.Static,
        SortOrder: 4,
        PersonUserId: null,
        PhoneCapable: false,
        Address: null,
        StaticLabel: "Home · Highmeadow");

    public static readonly DemoVehicle Wagon = new(
        Id: "wagon",
        Name: "Ford Pickup",
        Lore: "The King's Wagon",
        Glyph: VehicleGlyph.Pickup,
        SortOrder: 0,
        IsPlaceholder: false,
        PlaceholderNote: null);

    public static readonly DemoVehicle Chariot = new(
        Id: "chariot",
        Name: "Hatchback",
        Lore: "The Queen's Chariot",
        Glyph: VehicleGlyph.Car,
        SortOrder: 1,
        IsPlaceholder: true,
        PlaceholderNote: ChariotNote);

    /// <summary>The five people in sort order.</summary>
    public static readonly IReadOnlyList<DemoMember> Members = [King, Queen, Jester, Cryptid, Prince];

    /// <summary>The two vehicles in sort order.</summary>
    public static readonly IReadOnlyList<DemoVehicle> Vehicles = [Wagon, Chariot];
}
