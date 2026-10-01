namespace Realm.Domain;

/// <summary>The state of the trip state machine just before the last track fix was applied, so that a spike retraction can undo it.</summary>
internal sealed record MachineSnapshot(
    TripState State,
    TrackEntry? Stop,
    TrackEntry[] Ring,
    TrackEntry[] Run,
    (double Lat, double Lon)? Anchor,
    OpenTrip? Trip,
    int TripTrackCount,
    OpenTrip? Held,
    OpenTrip? Coarse,
    int CoarseTrackCount);
