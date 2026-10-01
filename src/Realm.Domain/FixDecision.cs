namespace Realm.Domain;

/// <summary>What the trip track did with one fix: it is in the track, or it is stored with track = 0 and a reason.</summary>
public record FixDecision(
    RawFix Fix,
    bool InTrack,
    TrackReason? Reason);
