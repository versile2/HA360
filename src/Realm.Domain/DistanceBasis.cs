namespace Realm.Domain;

/// <summary>How distances were measured. v1 produces only Gps.</summary>
public enum DistanceBasis
{
    Gps,
    Odometer,
    Mixed,
}
