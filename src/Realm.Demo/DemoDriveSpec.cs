namespace Realm.Demo;

/// <summary>What <see cref="DemoDriveGenerator"/> needs to build one driver's drive list for one week (02 section 9.4).</summary>
/// <param name="WeekStartIso">The week's first day as yyyy-MM-dd; with the member id it seeds the generator.</param>
/// <param name="TotalTenths">The driver's miles for the week, in tenths of a mile.</param>
/// <param name="TopMph">The driver's top speed for the week; exactly one drive reaches it.</param>
/// <param name="Counts">The week's counts of all four kinds (the base data, before any variant hides one).</param>
/// <param name="WindowStartMin">No drive starts before this many minutes after Monday 00:00 local.</param>
/// <param name="WindowEndMin">No drive ends after this many minutes after Monday 00:00 local.</param>
/// <param name="HomeLabel">Where the first drive of a day starts.</param>
internal sealed record DemoDriveSpec(
    string MemberId,
    string WeekStartIso,
    int Count,
    int TotalTenths,
    int TopMph,
    DemoEventCounts Counts,
    int WindowStartMin,
    int WindowEndMin,
    string HomeLabel,
    IReadOnlyList<DemoFixedDrive> Fixed);
