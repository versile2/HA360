using Realm.Domain;

namespace Realm.Demo;

/// <summary>One zone of the demo fixture (02 section 9.2, places table).</summary>
/// <param name="Id">The zone entity id without the "zone." prefix.</param>
/// <param name="ZoneName">The name Home Assistant gives the zone; two zones share one (the duplicate rule of 02 section 1.9).</param>
/// <param name="Name">The display name after the duplicate rule: the second "Work" is "Work (2)".</param>
/// <param name="Subtitle">The subtitle of the table; "n/a" for the arrival zone, which is never drawn.</param>
public record DemoPlace(
    string Id,
    string ZoneName,
    string Name,
    string Subtitle,
    PlaceKind Kind,
    double Lat,
    double Lon,
    double RadiusM)
{
    /// <summary>The zone as Home Assistant reports it, the input of the real place resolution.</summary>
    public RawPlace ToRaw() => new(Id, ZoneName, Lat, Lon, RadiusM, Passive: false);
}
