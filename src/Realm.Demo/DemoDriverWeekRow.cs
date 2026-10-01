namespace Realm.Demo;

/// <summary>
/// One report driver's week as the fixture holds it: whole drives, tenths of a mile and mph, before they become view models.
/// A driver who is not covered has null Drives, Tenths and TopMph and no counts.
/// </summary>
/// <param name="Tenths">Distance in tenths of a mile (the fixture's unit of exactness, 02 section 9.4).</param>
/// <param name="TopMph">The week's top speed; null when there is no speed record.</param>
/// <param name="Counts">This week's event counts.</param>
/// <param name="Comparators">The same counts in the comparison window; null where there is no comparator.</param>
internal sealed record DemoDriverWeekRow(
    DemoMember Driver,
    bool Covered,
    DateTimeOffset? CoverageStartUtc,
    int? Drives,
    int? Tenths,
    int? TopMph,
    DemoEventCounts Counts,
    DemoEventCounts Comparators);
