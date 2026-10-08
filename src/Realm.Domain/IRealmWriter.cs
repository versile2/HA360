namespace Realm.Domain;

/// <summary>
/// The door to the single database writer (03 section 2.2, 02 section 7.3). Every write of the live add-on goes through one in-process queue; a background
/// consumer commits it in batches. The row methods never block and never throw for a full queue: they return false when the row was not accepted
/// (the queue is full of rows that matter more, the writer has stopped, or a fix has a coordinate that is not a finite number), so a slow disk can never delay what the user sees.
/// </summary>
public interface IRealmWriter
{
    /// <summary>
    /// Queues one stored fix of a person (<c>fixes</c> table, <c>INSERT OR IGNORE</c> on member, time and source). <paramref name="inTrack"/> false stores it
    /// with <c>track = 0</c> and <paramref name="reason"/>: those diagnostic rows are the first to be dropped when the queue is full.
    /// </summary>
    bool EnqueueFix(string memberId, RawFix fix, bool inTrack = true, TrackReason? reason = null);

    /// <summary>Queues one phone sensor transition (<c>signals</c> table, <c>INSERT OR IGNORE</c> on member, time and kind).</summary>
    bool EnqueueSignal(string memberId, PhoneSignal signal);

    /// <summary>
    /// Queues one <c>meta</c> row (02 section 7.2): inserted, or replaced when the key exists (the <c>ha_time_zone</c> of D66 is written this way). The key and the value
    /// must not be empty. Returns false when the row was not accepted, as the other row methods do.
    /// </summary>
    bool EnqueueMeta(string key, string value);

    /// <summary>
    /// Writes a closed trip and its speeding and phone events (<c>trips</c> and <c>trip_events</c>), after everything queued before it has been committed
    /// (02 section 7.3: a close is written after the flush that holds its last fix). Completes when the trip is committed. Returns false when a trip of the
    /// same member and start time already exists (nothing is changed then), true when a new row was written. The writer must be running.
    /// </summary>
    /// <param name="algoVersion">The detector's algorithm version, stamped on the row (02 section 7.7).</param>
    /// <param name="deriveHash">The hash of the options that influence the derived counts, stamped on the row (02 section 7.7).</param>
    Task<bool> WriteTripAsync(string memberId, DetectedTrip trip, int algoVersion, string deriveHash, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes roster rows (<c>roster</c> table, 02 section 7.2): each entry is inserted, or replaces the row of its entity id, in one transaction after everything
    /// queued before it has been committed. Completes when they are committed. Nobody else writes that table. The writer must be running.
    /// </summary>
    Task WriteRosterAsync(IReadOnlyList<RosterEntry> entries, CancellationToken cancellationToken = default);

    /// <summary>Commits everything queued so far and returns when it is on disk. A failure is thrown and the rows stay queued for the next attempt.</summary>
    Task FlushAsync(CancellationToken cancellationToken = default);
}
