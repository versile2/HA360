namespace Realm.Domain;

/// <summary>The state of the trip state machine of one member (02 section 5.3).</summary>
public enum TripState
{
    /// <summary>No drive in progress.</summary>
    Idle,

    /// <summary>A drive is in progress.</summary>
    Driving,

    /// <summary>The member stopped; the drive closes if the stop lasts the merge time, and resumes if they move on.</summary>
    Settling,
}
