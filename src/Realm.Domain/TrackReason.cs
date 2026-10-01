namespace Realm.Domain;

/// <summary>Why a stored fix is not in the trip track (the reason column of 02 section 7.2, plus Dropped).</summary>
public enum TrackReason
{
    /// <summary>The accuracy is worse than 100 m.</summary>
    Accuracy,

    /// <summary>The fix was an implied-speed outlier, or was retracted as one by the fix after it.</summary>
    Spike,

    /// <summary>A better source fed the track within the failover time, or the source never feeds a member track.</summary>
    Priority,

    /// <summary>A duplicate of, or older than, the last track fix: not worth storing as a track fix.</summary>
    Dropped,
}
