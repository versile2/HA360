namespace Realm.Demo;

/// <summary>
/// A drive the fixture states outright (02 section 9.4 (d)); the generator builds the rest around it. Times are minutes from
/// Monday 00:00 local of the week. Anything left null is generated.
/// </summary>
/// <param name="EndMin">Null: derived from the distance.</param>
/// <param name="Tenths">Null: taken from the driver's share of the week's miles.</param>
/// <param name="TopMph">Null: a slow top speed (below the speeding threshold) is drawn.</param>
/// <param name="Phone">Null: the phone count of the drive is drawn like the others.</param>
internal sealed record DemoFixedDrive(
    int StartMin,
    int? EndMin,
    string? From,
    string? To,
    int? Tenths,
    int? TopMph,
    int? Phone = null);
