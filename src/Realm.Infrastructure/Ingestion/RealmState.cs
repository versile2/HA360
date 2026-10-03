using Realm.Domain;
using Realm.Infrastructure.Ha;
using Realm.Infrastructure.Options;

namespace Realm.Infrastructure.Ingestion;

/// <summary>
/// The current immutable <see cref="RealmSnapshot"/> (03 section 2.9). The ingestion pipeline publishes it and the statistics service counts
/// <see cref="RealmSnapshot.StatsVersion"/> up; any thread reads it. The snapshot is swapped whole, so a reader never sees half of an update, and
/// <see cref="Version"/> counts the swaps. The statistics version never goes down: a snapshot the pipeline built before a bump, and publishes after it,
/// is lifted to the newer count.
/// </summary>
public sealed class RealmState
{
    private readonly object _gate = new();
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
    /// <param name="options">The add-on options.</param>
    /// <param name="time">The clock of the first connection status.</param>
    /// <param name="refused">
    /// True when the options were refused (02 section 3.3): no service that talks to Home Assistant starts, so nothing will ever replace this snapshot, and a
    /// Connecting status would read Reconnecting for 15 s and then Unavailable with no first data behind it. The Home Assistant entry is built from
    /// <see cref="HaConnectionState.NotConfigured"/> instead, which reads Unavailable at once (02 section 1.8, 03 section 9.3), so the UI shows the first-data
    /// error of 01 section 8.6 and <c>diagnostics.json</c> raises <c>ha_unavailable</c>.
    /// </param>
    public static RealmState CreateInitial(RealmOptions options, TimeProvider time, bool refused = false)
    {
        var now = time.GetUtcNow();
        var status = refused
            ? new HaConnectionStatus(HaConnectionState.NotConfigured, null, null, 0, null)
            : new HaConnectionStatus(HaConnectionState.Connecting, now, null, 0, null);
        return new RealmState(SnapshotBuilder.Initial(options, now, status));
    }

    public RealmSnapshot Current => _current.Snapshot;

    /// <summary>0 for the initial snapshot, then one more with every <see cref="Publish"/>.</summary>
    public long Version => _current.Version;

    /// <summary>Replaces the snapshot, keeping the statistics version at the highest value seen. Called by the pipeline's one consumer (and its tick, under the pipeline's lock).</summary>
    public void Publish(RealmSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        lock (_gate)
        {
            var known = _current.Snapshot.StatsVersion;
            _current = new Entry(snapshot.StatsVersion >= known ? snapshot : snapshot with { StatsVersion = known }, _current.Version + 1);
        }
    }

    /// <summary>
    /// A trip was closed and written (02 section 1.10): counts <see cref="RealmSnapshot.StatsVersion"/> up in a new snapshot and returns the new value. The
    /// caller announces the change (<see cref="ChangeNotifier.NotifyChanged"/>).
    /// </summary>
    public int BumpStatsVersion()
    {
        lock (_gate)
        {
            var bumped = _current.Snapshot with { StatsVersion = _current.Snapshot.StatsVersion + 1 };
            _current = new Entry(bumped, _current.Version + 1);
            return bumped.StatsVersion;
        }
    }

    private sealed record Entry(RealmSnapshot Snapshot, long Version);
}
