namespace Realm.Domain;

/// <summary>A Life360 fix that carried an address, kept to name the streets of a trip's endpoints and top speed.</summary>
internal sealed record AddressFix(DateTimeOffset Ts, double Lat, double Lon, string Street);
