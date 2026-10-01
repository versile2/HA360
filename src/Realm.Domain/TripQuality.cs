namespace Realm.Domain;

/// <summary>Whether a trip was built from dense fixes or from sparse ones (02 section 5.6).</summary>
public enum TripQuality
{
    /// <summary>Fixes close enough together to follow the drive; speed data exists.</summary>
    Dense,

    /// <summary>Sparse fixes only: counted in drives and distance, never in top speed or events.</summary>
    Coarse,
}
