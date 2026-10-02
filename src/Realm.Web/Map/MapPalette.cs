using Realm.Web.Theme;

namespace Realm.Web.Map;

/// <summary>
/// The colours that travel in the map payloads: the ring colours of 01 sections 4.3 and 4.7 and the zone appearances of 01 section 4.6.
/// Values that equal a <see cref="RealmPalette"/> constant reuse it; the others are fixed by 01 section 4.3 (AC-16 asserts them) and
/// sit here until <c>RealmPalette</c> takes the status and member colours (its own summary says they join it in the slice that first
/// draws them). JavaScript holds no colour table (03 section 4.2), so these reach the browser only through the payloads.
/// </summary>
public static class MapPalette
{
    /// <summary>The static pin's dashed ring.</summary>
    public const string RingStatic = "#8FA3FF";

    /// <summary>A stale or offline member's dashed ring, and a stale vehicle's.</summary>
    public const string RingStale = RealmPalette.Stale;

    /// <summary>A driving member's solid ring and a moving vehicle's.</summary>
    public const string RingDriving = "#4DB8FF";

    /// <summary>A member's solid ring at a place.</summary>
    public const string RingAtPlace = "#3DDC84";

    /// <summary>A member's solid ring when out and not driving.</summary>
    public const string RingOut = RealmPalette.Text;

    /// <summary>A parked vehicle's ring.</summary>
    public const string RingParked = RealmPalette.Text2;

    /// <summary>The zone outline on light map styles (Day, Streets).</summary>
    public const string ZoneLineLight = "#8A5F00";

    /// <summary>Zone fill and outline on dark and imagery styles, and the dark values of 01 section 4.6.</summary>
    public static ZoneAppearance ZoneDark { get; } = new(RealmPalette.Primary, 0.10, 0.22, Casing: false);

    /// <summary>Light map styles: 14 % fill, the darker outline.</summary>
    public static ZoneAppearance ZoneLight { get; } = new(ZoneLineLight, 0.14, 0.22, Casing: false);

    /// <summary>Imagery: the dark values plus a 1 px dark casing.</summary>
    public static ZoneAppearance ZoneImagery { get; } = new(RealmPalette.Primary, 0.10, 0.22, Casing: true);
}
