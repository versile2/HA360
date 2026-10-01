namespace Realm.Domain;

/// <summary>Collects the output of one or more detector calls.</summary>
internal sealed class StepAccumulator
{
    public List<FixDecision> Decisions { get; } = [];

    public List<RawFix> Retracted { get; } = [];

    public List<DetectedTrip> Closed { get; } = [];

    public List<DetectedTrip> Discarded { get; } = [];

    public TripStep ToStep() => new(Decisions, Retracted, Closed, Discarded);
}
