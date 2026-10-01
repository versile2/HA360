namespace Realm.Domain;

/// <summary>How a trip ended (the ended_by column of 02 section 7.2).</summary>
public enum TripEndedBy
{
    /// <summary>The member stopped for the merge time.</summary>
    Stop,

    /// <summary>No fix at all arrived for the no-fix time while driving (a tunnel or dead zone).</summary>
    NoFix,

    /// <summary>A coarse trip, which ends at its last qualifying fix.</summary>
    Coarse,
}
