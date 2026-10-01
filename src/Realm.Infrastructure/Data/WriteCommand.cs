using Realm.Domain;

namespace Realm.Infrastructure.Data;

/// <summary>One entry of the writer's queue (02 section 7.3). A row command is one row; a trip close carries a completion the caller awaits.</summary>
internal abstract record WriteCommand
{
    /// <summary>True for the <c>track = 0</c> fixes: diagnostics that are dropped first when the queue is full.</summary>
    public virtual bool IsDiagnostic => false;

    /// <summary>True for a command that is never refused for lack of room (a trip close is rare and the caller waits for it).</summary>
    public virtual bool MustAccept => false;

    internal sealed record Fix(string MemberId, RawFix Value, bool InTrack, TrackReason? Reason) : WriteCommand
    {
        public override bool IsDiagnostic => !InTrack;
    }

    internal sealed record VehicleSampleRow(VehicleSample Value) : WriteCommand;

    internal sealed record Signal(string MemberId, PhoneSignal Value) : WriteCommand;

    internal sealed record Trip(
        string MemberId,
        DetectedTrip Value,
        int AlgoVersion,
        string DeriveHash,
        TaskCompletionSource<bool> Done) : WriteCommand
    {
        public override bool MustAccept => true;
    }
}
