namespace Realm.Demo;

/// <summary>One drive of a generated list, still in the fixture's units: minutes from Monday 00:00 local, tenths of a mile, mph.</summary>
/// <param name="Events">All four counts, never null here; the variants decide which of them a view model shows.</param>
internal sealed record DemoGeneratedDrive(
    int StartMin,
    int EndMin,
    string From,
    string To,
    int Tenths,
    int TopMph,
    DemoEventCounts Events);
