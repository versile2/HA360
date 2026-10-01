namespace Realm.Domain;

/// <summary>A trip being built, or closed and waiting to be finalized.</summary>
internal sealed class OpenTrip
{
    public OpenTrip(IEnumerable<TrackEntry> track, IEnumerable<AddressFix> addresses, TripQuality quality, TripEndedBy endedBy)
    {
        Track = [.. track];
        Addresses = [.. addresses];
        Quality = quality;
        EndedBy = endedBy;
    }

    public List<TrackEntry> Track { get; }

    public List<AddressFix> Addresses { get; }

    public TripQuality Quality { get; }

    public TripEndedBy EndedBy { get; }

    /// <summary>Set when the trip has been emitted as closed (or discarded); it can no longer be merged or restored.</summary>
    public bool Finalized { get; set; }

    public DateTimeOffset StartUtc => Track[0].Ts;

    public DateTimeOffset EndUtc => Track[^1].Ts;
}
