using Realm.Domain;
using Realm.Infrastructure.Ha;
using Realm.Infrastructure.Options;

namespace Realm.Infrastructure.Ingestion;

/// <summary>
/// The current immutable <see cref="RealmSnapshot"/> (03 section 2.9). Only the ingestion pipeline writes it; any thread reads it. The snapshot is swapped
/// whole, so a reader never sees half of an update, and <see cref="Version"/> counts the swaps.
/// </summary>
public sealed class RealmState
{
    private volatile Entry _current;

    /// <param name="initial">What a reader sees before the first update.</param>
    public RealmState(RealmSnapshot initial)
    {
        ArgumentNullException.ThrowIfNull(initial);
        _current = new Entry(initial, 0);
    }

    /// <summary>
    /// The state of a host that has just started: no members, UTC, and Home Assistant being reached for the first time (Reconnecting for 15 s, then
    /// Unavailable), which is what the pipeline's first status says too.
    /// </summary>
    public static RealmState CreateInitial(RealmOptions options, TimeProvider time)
    {
        var now = time.GetUtcNow();
        return new RealmState(SnapshotBuilder.Initial(options, now, new HaConnectionStatus(HaConnectionState.Connecting, now, null, 0, null)));
    }

    public RealmSnapshot Current => _current.Snapshot;

    /// <summary>0 for the initial snapshot, then one more with every <see cref="Publish"/>.</summary>
    public long Version => _current.Version;

    /// <summary>Replaces the snapshot. Called by the pipeline's one consumer (and its tick, under the pipeline's lock), so there is one writer.</summary>
    public void Publish(RealmSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _current = new Entry(snapshot, _current.Version + 1);
    }

    private sealed record Entry(RealmSnapshot Snapshot, long Version);
}
