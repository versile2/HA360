namespace Realm.Domain;

/// <summary>A zone as Home Assistant reports it, before options overrides. Id is the zone entity id without the "zone." prefix.</summary>
public record RawPlace(
    string Id,
    string Name,
    double Lat,
    double Lon,
    double RadiusM,
    bool Passive);
