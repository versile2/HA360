namespace Realm.Domain;

/// <summary>What one call of the trip detector produced.</summary>
/// <param name="Decisions">One per fix processed: in the track, or stored with track = 0 and a reason.</param>
/// <param name="Retracted">Fixes accepted earlier that a later fix proved to be outliers (store them with track = 0, reason spike).</param>
/// <param name="Closed">Valid trips that closed: persist these.</param>
/// <param name="Discarded">Trips that closed but were invalid (under 0.3 mi, under 2 min or too slow): count them, do not store them.</param>
public record TripStep(
    IReadOnlyList<FixDecision> Decisions,
    IReadOnlyList<RawFix> Retracted,
    IReadOnlyList<DetectedTrip> Closed,
    IReadOnlyList<DetectedTrip> Discarded);
