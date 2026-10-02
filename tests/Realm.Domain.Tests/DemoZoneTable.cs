namespace Realm.Domain.Tests;

// The 14 drawn zones of the demo fixture, typed out of 02 section 9.2 (fictional names and coordinates). The
// 15th, the arrival zone "approach" (radius 32 187 m, centred on home), is never drawn and is not in the list.
internal static class DemoZoneTable
{
    public static readonly IReadOnlyList<RawPlace> Drawn =
    [
        new("home", "Hearth Haven", 31.0990, -85.3410, 100, false),
        new("jester_hall", "The Jester's Hall", 31.1040, -85.3560, 100, false),
        new("work", "Work", 31.1530, -85.4080, 150, false),
        new("work_2", "Work", 31.1534, -85.4084, 150, false),
        new("park", "Park", 31.0720, -85.3290, 200, false),
        new("orrin", "Orrin's", 31.0420, -85.3880, 120, false),
        new("mara", "Mara's", 31.1450, -85.2960, 120, false),
        new("skate_one", "Rollerdome", 31.1760, -85.4700, 200, false),
        new("skate_two", "Rollerdome", 31.2210, -85.5140, 200, false),
        new("queen_office", "Briar's Office", 31.0200, -85.5200, 150, false),
        new("derby", "Derby Hall", 31.2550, -85.3400, 250, false),
        new("cemetery", "Hollow Cemetery", 31.0300, -85.2900, 150, false),
        new("vet", "Vet Clinic", 31.1180, -85.4350, 100, false),
        new("wheels", "Wheel Hall", 31.1900, -85.3300, 200, false),
    ];
}
