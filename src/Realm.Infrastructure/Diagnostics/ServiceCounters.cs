namespace Realm.Infrastructure.Diagnostics;

/// <summary>
/// The plain counters the Live services keep for <c>diagnostics.json</c> (03 sections 2.11 and 9.2): Interlocked fields, no meter, no listener, no exporter.
/// A service calls a <c>Record</c> or <c>Set</c> method at a place it already passes (a connect, a message, a call, a commit); <see cref="DiagnosticsSnapshotBuilder"/>
/// reads the properties from any thread. Every value is a count, a depth, an instant or a flag: there is no place to put a coordinate, an address, an entity id,
/// a token or a piece of log text (02 section 10.4). Every service takes the instance as an optional last argument, so a service built without one (a test, a
/// tool) simply counts nothing.
/// </summary>
public sealed class ServiceCounters
{
    private readonly RateWindow _wsMessagesPerMinute = new();
    private readonly RateWindow _ingestItemsPerMinute = new();

    private long _wsConnects;
    private long _wsReconnects;
    private long _wsMessages;
    private long _lastWsMessageTicks;
    private int _watchedEntities;
    private long _restCalls;
    private long _restFailures;
    private long _ingestItems;
    private long _ingestSkipped;
    private int _ingestQueueDepth;
    private long _dbCommits;
    private long _dbRows;
    private long _lastDbCommitTicks;
    private int _writerQueueDepth;
    private long _writerDropped;
    private long _backfillRuns;
    private long _backfillRows;
    private long _retentionRuns;
    private long _retentionRowsDeleted;
    private long _avatarFetches;
    private long _avatarFailures;
    private int _schemaVersion;
    private volatile bool _uncleanShutdownAtStart;
    private volatile bool _zoneDataMissing;
    private volatile bool _payloadSchemaMismatch;

    /// <param name="time">The clock the process uptime is measured on.</param>
    public ServiceCounters(TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(time);
        StartedUtc = time.GetUtcNow();
    }

    /// <summary>When the counters were created, which is at the start of the host (the schema step takes them first).</summary>
    public DateTimeOffset StartedUtc { get; }

    // ---- the Home Assistant websocket -------------------------------------------------------------------------------

    /// <summary>Connections that were established (authenticated and subscribed), since start.</summary>
    public long WsConnects => Interlocked.Read(ref _wsConnects);

    /// <summary>Connections lost after they had been established, since start.</summary>
    public long WsReconnects => Interlocked.Read(ref _wsReconnects);

    /// <summary>Messages received from Home Assistant after authentication, since start.</summary>
    public long WsMessages => Interlocked.Read(ref _wsMessages);

    /// <summary>When the last message arrived; null before the first.</summary>
    public DateTimeOffset? LastWsMessageUtc => FromTicks(Interlocked.Read(ref _lastWsMessageTicks));

    /// <summary>How many entities the watch list of the last discovery held.</summary>
    public int WatchedEntities => Volatile.Read(ref _watchedEntities);

    /// <summary>The websocket authenticated and subscribed.</summary>
    public void RecordWsConnected() => Interlocked.Increment(ref _wsConnects);

    /// <summary>An established connection was lost.</summary>
    public void RecordWsReconnect() => Interlocked.Increment(ref _wsReconnects);

    /// <summary>One message arrived at <paramref name="at"/>.</summary>
    public void RecordWsMessage(DateTimeOffset at)
    {
        Interlocked.Increment(ref _wsMessages);
        Interlocked.Exchange(ref _lastWsMessageTicks, at.UtcTicks);
        _wsMessagesPerMinute.Add(at);
    }

    /// <summary>Messages received in the 60 seconds up to <paramref name="now"/>.</summary>
    public int WsMessagesPerMinute(DateTimeOffset now) => _wsMessagesPerMinute.PerMinute(now);

    /// <summary>The discovery that has just finished watches <paramref name="count"/> entities.</summary>
    public void SetWatchedEntities(int count) => Volatile.Write(ref _watchedEntities, count);

    // ---- Home Assistant REST ----------------------------------------------------------------------------------------

    /// <summary>REST calls made (a call that was retried counts once), since start.</summary>
    public long RestCalls => Interlocked.Read(ref _restCalls);

    /// <summary>REST calls that ended in an error, retries used up included, since start.</summary>
    public long RestFailures => Interlocked.Read(ref _restFailures);

    /// <summary>A REST call began.</summary>
    public void RecordRestCall() => Interlocked.Increment(ref _restCalls);

    /// <summary>A REST call ended in an error.</summary>
    public void RecordRestFailure() => Interlocked.Increment(ref _restFailures);

    // ---- ingestion --------------------------------------------------------------------------------------------------

    /// <summary>Items the ingestion pipeline processed, since start.</summary>
    public long IngestItems => Interlocked.Read(ref _ingestItems);

    /// <summary>Items the pipeline skipped because processing them failed, since start.</summary>
    public long IngestSkipped => Interlocked.Read(ref _ingestSkipped);

    /// <summary>Items waiting in the pipeline's queue, as the pipeline last saw it.</summary>
    public int IngestQueueDepth => Volatile.Read(ref _ingestQueueDepth);

    /// <summary>The pipeline processed one item at <paramref name="at"/>.</summary>
    public void RecordIngestItem(DateTimeOffset at)
    {
        Interlocked.Increment(ref _ingestItems);
        _ingestItemsPerMinute.Add(at);
    }

    /// <summary>The pipeline skipped an item that failed.</summary>
    public void RecordIngestSkipped() => Interlocked.Increment(ref _ingestSkipped);

    /// <summary>Items processed in the 60 seconds up to <paramref name="now"/>.</summary>
    public int IngestEventsPerMinute(DateTimeOffset now) => _ingestItemsPerMinute.PerMinute(now);

    /// <summary>The pipeline's queue holds <paramref name="depth"/> items.</summary>
    public void SetIngestQueueDepth(int depth) => Volatile.Write(ref _ingestQueueDepth, depth);

    // ---- the database writer ----------------------------------------------------------------------------------------

    /// <summary>Batches of rows the writer committed, since start.</summary>
    public long DbCommits => Interlocked.Read(ref _dbCommits);

    /// <summary>Rows the writer committed, since start.</summary>
    public long DbRows => Interlocked.Read(ref _dbRows);

    /// <summary>When the last batch of rows was committed; null before the first.</summary>
    public DateTimeOffset? LastDbCommitUtc => FromTicks(Interlocked.Read(ref _lastDbCommitTicks));

    /// <summary>Rows waiting in the writer's queue, as the writer last saw it.</summary>
    public int WriterQueueDepth => Volatile.Read(ref _writerQueueDepth);

    /// <summary>Rows the writer's queue refused or evicted because it was full or closed, since start.</summary>
    public long WriterDropped => Interlocked.Read(ref _writerDropped);

    /// <summary>The writer committed a batch of <paramref name="rows"/> rows at <paramref name="at"/>.</summary>
    public void RecordDbCommit(int rows, DateTimeOffset at)
    {
        Interlocked.Increment(ref _dbCommits);
        Interlocked.Add(ref _dbRows, rows);
        Interlocked.Exchange(ref _lastDbCommitTicks, at.UtcTicks);
    }

    /// <summary>The writer's queue holds <paramref name="depth"/> rows and has dropped <paramref name="dropped"/> since start (a count that never goes down).</summary>
    public void SetWriterQueue(int depth, long dropped)
    {
        Volatile.Write(ref _writerQueueDepth, depth);
        RaiseTo(ref _writerDropped, dropped);
    }

    // ---- the jobs ---------------------------------------------------------------------------------------------------

    /// <summary>Gap-fills that ran, since start.</summary>
    public long BackfillRuns => Interlocked.Read(ref _backfillRuns);

    /// <summary>Rows the gap-fills queued, since start.</summary>
    public long BackfillRows => Interlocked.Read(ref _backfillRows);

    /// <summary>Prune runs that finished, since start.</summary>
    public long RetentionRuns => Interlocked.Read(ref _retentionRuns);

    /// <summary>Rows the prune runs deleted, since start.</summary>
    public long RetentionRowsDeleted => Interlocked.Read(ref _retentionRowsDeleted);

    /// <summary>A gap-fill finished having queued <paramref name="rows"/> rows.</summary>
    public void RecordBackfillRun(long rows)
    {
        Interlocked.Increment(ref _backfillRuns);
        Interlocked.Add(ref _backfillRows, rows);
    }

    /// <summary>A prune run finished having deleted <paramref name="rowsDeleted"/> rows.</summary>
    public void RecordRetentionRun(long rowsDeleted)
    {
        Interlocked.Increment(ref _retentionRuns);
        Interlocked.Add(ref _retentionRowsDeleted, rowsDeleted);
    }

    // ---- avatars ----------------------------------------------------------------------------------------------------

    /// <summary>Avatars fetched from upstream (a picture served from the cache is not one), since start.</summary>
    public long AvatarFetches => Interlocked.Read(ref _avatarFetches);

    /// <summary>Avatar fetches that were refused or failed, since start.</summary>
    public long AvatarFailures => Interlocked.Read(ref _avatarFailures);

    /// <summary>An avatar fetch began.</summary>
    public void RecordAvatarFetch() => Interlocked.Increment(ref _avatarFetches);

    /// <summary>An avatar fetch was refused or failed.</summary>
    public void RecordAvatarFailure() => Interlocked.Increment(ref _avatarFailures);

    // ---- facts of the start and flags -------------------------------------------------------------------------------

    /// <summary>The schema version the schema step left the database at; 0 before it ran.</summary>
    public int SchemaVersion => Volatile.Read(ref _schemaVersion);

    /// <summary>The previous run did not stop cleanly (<c>meta.clean_shutdown</c> was <c>0</c> at start, 02 section 7.8).</summary>
    public bool UncleanShutdownAtStart => _uncleanShutdownAtStart;

    /// <summary>False when the start-up self-check could not resolve time zone data; true until it says otherwise.</summary>
    public bool ZoneDataOk => !_zoneDataMissing;

    /// <summary>The browser script and the server disagreed on the map payload schema (03 section 4.1); once seen it stays set.</summary>
    public bool PayloadSchemaMismatch => _payloadSchemaMismatch;

    /// <summary>The schema step finished: the database is at <paramref name="schemaVersion"/>, and the last run did or did not stop cleanly.</summary>
    public void RecordStartup(int schemaVersion, bool uncleanShutdown)
    {
        Volatile.Write(ref _schemaVersion, schemaVersion);
        _uncleanShutdownAtStart = uncleanShutdown;
    }

    /// <summary>The start-up self-check resolved (or could not resolve) time zone data.</summary>
    public void RecordZoneData(bool ok) => _zoneDataMissing = !ok;

    /// <summary>The map script reported a payload schema other than the server's.</summary>
    public void MarkPayloadSchemaMismatch() => _payloadSchemaMismatch = true;

    private static DateTimeOffset? FromTicks(long ticks) => ticks == 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero);

    // A counter that another thread may be raising at the same moment never goes down.
    private static void RaiseTo(ref long field, long value)
    {
        var seen = Interlocked.Read(ref field);
        while (value > seen)
        {
            var previous = Interlocked.CompareExchange(ref field, value, seen);
            if (previous == seen)
            {
                return;
            }

            seen = previous;
        }
    }
}
