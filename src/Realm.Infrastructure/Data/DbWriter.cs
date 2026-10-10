using System.Threading.Channels;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Realm.Domain;
using Realm.Infrastructure.Diagnostics;
using Realm.Infrastructure.Hosting;

namespace Realm.Infrastructure.Data;

/// <summary>
/// The single database writer (02 section 7.3, 03 sections 2.4, 2.12 and 2.13). Producers queue rows through <see cref="IRealmWriter"/>; one consumer commits
/// the queue in one transaction whenever 100 rows are waiting or 2 s have passed, so a crash loses at most 2 s of fixes (the HA history gap-fill restores
/// them). Fixes and signals are <c>INSERT OR IGNORE</c>, meta rows replace. The queue holds
/// <see cref="QueueCapacity"/> rows and drops <c>track = 0</c> diagnostic rows first when it is full. A trip close and a roster write are each written on their own, after the flush
/// that holds everything queued before them. On a graceful stop the queue is drained, <c>meta.clean_shutdown</c> is set to <c>'1'</c> and the WAL is
/// checkpointed. This is the only code that writes the database after <see cref="SchemaBootstrap"/>.
/// </summary>
public sealed class DbWriter : BackgroundService, IRealmWriter
{
    /// <summary>The queue bound of 02 section 7.3.</summary>
    public const int QueueCapacity = 10_000;

    /// <summary>A flush starts as soon as this many rows are queued.</summary>
    public const int FlushRows = 100;

    /// <summary>A flush starts at least this often.</summary>
    public static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(2);

    private const string EnsureMemberSql =
        "INSERT OR IGNORE INTO members(id, first_seen_utc, last_configured_utc) VALUES (@id, @now, @now)";

    private const string RecordingStartSql =
        "UPDATE members SET recording_start = @ts WHERE id = @id AND (recording_start IS NULL OR recording_start > @ts)";

    private const string FixSql =
        "INSERT OR IGNORE INTO fixes(member_id, ts, source, lat, lon, acc_m, speed_mps, heading_deg, alt_m, battery_pct, charging, driving, address, track, reason) "
        + "VALUES (@member, @ts, @source, @lat, @lon, @acc, @speed, @heading, @alt, @battery, @charging, @driving, @address, @track, @reason)";

    private const string SignalSql =
        "INSERT OR IGNORE INTO signals(member_id, ts, kind, value) VALUES (@member, @ts, @kind, @value)";

    private const string PruneFixesSql = "DELETE FROM fixes WHERE id IN (SELECT id FROM fixes WHERE ts < @cutoff ORDER BY id LIMIT @limit)";

    private const string PruneSignalsSql = "DELETE FROM signals WHERE id IN (SELECT id FROM signals WHERE ts < @cutoff ORDER BY id LIMIT @limit)";

    private const string RosterSql =
        "INSERT INTO roster(entity_id, kind, grp, display_name, lore_title, color, sort_order, source, first_seen, last_active, auto_moved_at, "
        + "source_name, source_title, source_color, name_override, title_override, color_override, icon, keep_history) "
        + "VALUES (@id, @kind, @grp, @name, @lore, @color, @sort, @source, @first, @active, @moved, @sname, @stitle, @scolor, @oname, @otitle, @ocolor, @icon, @keep) "
        + "ON CONFLICT(entity_id) DO UPDATE SET kind = excluded.kind, grp = excluded.grp, display_name = excluded.display_name, lore_title = excluded.lore_title, "
        + "color = excluded.color, sort_order = excluded.sort_order, source = excluded.source, first_seen = excluded.first_seen, "
        + "last_active = excluded.last_active, auto_moved_at = excluded.auto_moved_at, source_name = excluded.source_name, source_title = excluded.source_title, "
        + "source_color = excluded.source_color, name_override = excluded.name_override, title_override = excluded.title_override, "
        + "color_override = excluded.color_override, icon = excluded.icon, keep_history = excluded.keep_history";

    private const string MetaSql = "INSERT OR REPLACE INTO meta(key, value) VALUES (@key, @value)";

    // v1: distance_m = distance_gps_m and distance_source = 'gps'; no vehicle, no odometer drive, accel and braking always NULL (02 section 7.2, D20).
    private const string TripSql =
        "INSERT OR IGNORE INTO trips(member_id, start_ts, end_ts, duration_s, start_lat, start_lon, end_lat, end_lon, "
        + "start_place_id, end_place_id, start_street, end_street, distance_m, distance_gps_m, distance_source, vehicle_id, vehicle_drive_id, "
        + "top_speed_mps, top_speed_ts, top_speed_street, speeding_count, phone_count, accel_count, braking_count, "
        + "quality, has_gap, ended_by, source_mask, algo_version, derive_hash, created_ts) "
        + "VALUES (@member, @start, @end, @duration, @slat, @slon, @elat, @elon, "
        + "@splace, @eplace, @sstreet, @estreet, @dist, @dist, 'gps', NULL, NULL, "
        + "@top, @topts, @topstreet, @speeding, @phone, NULL, NULL, "
        + "@quality, @gap, @endedby, @mask, @algo, @hash, @created)";

    private const string TripEventSql =
        "INSERT INTO trip_events(trip_id, kind, start_ts, end_ts, peak, lat, lon) VALUES (@trip, @kind, @start, @end, @peak, @lat, @lon)";

    /// <summary>The tables of 02 section 7.6 that age out after <c>retention_fix_days</c>, in the order they are pruned.</summary>
    internal static readonly string[] PrunedTables = ["fixes", "signals"];

    private readonly IDbContextFactory<RealmDb> _factory;
    private readonly TimeProvider _time;
    private readonly ILogger<DbWriter> _logger;
    private readonly ServiceCounters? _counters;
    private readonly WriteQueue _queue = new(QueueCapacity);
    private readonly Channel<bool> _bell = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });
    private readonly SemaphoreSlim _flushGate = new(1, 1);
    private readonly ResilientLoop _loop;
    private ITimer? _timer;
    private long _committedRows;
    private long _lastCommitMs;
    private long _lastDropLogMs;
    private int _subscriberFailureLogged;

    /// <param name="counters">Where commits, the queue depth and the drops are counted for <c>diagnostics.json</c>; null counts nothing.</param>
    public DbWriter(IDbContextFactory<RealmDb> factory, TimeProvider time, ILogger<DbWriter> logger, ServiceCounters? counters = null)
    {
        _factory = factory;
        _time = time;
        _logger = logger;
        _counters = counters;
        _loop = new ResilientLoop(nameof(DbWriter), logger, time);
    }

    /// <summary>
    /// Raised after each committed batch of rows with the number of rows in it, on the writer's thread. A trip close does not raise it. A subscriber that
    /// throws is logged once and does not affect the writer or the other subscribers: the rows are committed by then.
    /// </summary>
    public event Action<int>? RowsCommitted;

    /// <summary>Whether the writer loop has failed and is waiting to start again (03 section 2.14).</summary>
    public ServiceHealth Health => _loop.Health;

    /// <summary>Rows queued and not yet committed (the <c>writerQueueDepth</c> of the diagnostics).</summary>
    public int QueueDepth => _queue.Count;

    /// <summary>Rows refused or evicted because the queue was full or the writer had stopped, since the process started.</summary>
    public long DroppedRows => _queue.Dropped;

    /// <summary>Rows committed since the process started.</summary>
    public long CommittedRows => Interlocked.Read(ref _committedRows);

    /// <summary>When the last batch of rows was committed; null before the first one.</summary>
    public DateTimeOffset? LastCommitUtc
    {
        get
        {
            var ms = Interlocked.Read(ref _lastCommitMs);
            return ms == 0 ? null : SqlValues.FromMillis(ms);
        }
    }

    public bool EnqueueFix(string memberId, RawFix fix, bool inTrack = true, TrackReason? reason = null)
    {
        // lat and lon are NOT NULL columns and SQLite stores NaN as NULL: one such row would fail every flush after it, so it is refused here.
        if (!double.IsFinite(fix.Lat) || !double.IsFinite(fix.Lon))
        {
            return false;
        }

        return Accept(new WriteCommand.Fix(memberId, fix, inTrack, inTrack ? null : reason));
    }

    public bool EnqueueSignal(string memberId, PhoneSignal signal)
    {
        return Accept(new WriteCommand.Signal(memberId, signal));
    }

    public bool EnqueueMeta(string key, string value)
    {
        if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(value))
        {
            return false;
        }

        return Accept(new WriteCommand.Meta(key, value));
    }

    public async Task<bool> WriteTripAsync(string memberId, DetectedTrip trip, int algoVersion, string deriveHash, CancellationToken cancellationToken = default)
    {
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var accepted = _queue.TryAdd(new WriteCommand.Trip(memberId, trip, algoVersion, deriveHash, done));
        PublishQueue();
        if (!accepted)
        {
            throw new InvalidOperationException("The database writer has stopped");
        }

        _bell.Writer.TryWrite(true);
        return await done.Task.WaitAsync(cancellationToken);
    }

    public async Task WriteRosterAsync(IReadOnlyList<RosterEntry> entries, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (entries.Count == 0)
        {
            return;
        }

        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var accepted = _queue.TryAdd(new WriteCommand.Roster(entries, done));
        PublishQueue();
        if (!accepted)
        {
            throw new InvalidOperationException("The database writer has stopped");
        }

        _bell.Writer.TryWrite(true);
        await done.Task.WaitAsync(cancellationToken);
    }

    public Task FlushAsync(CancellationToken cancellationToken = default)
    {
        return FlushQueuedAsync(cancellationToken);
    }

    /// <summary>
    /// Deletes up to <paramref name="limit"/> of the oldest rows of one of the raw tables (<c>fixes</c>, <c>signals</c>) with a time before
    /// <paramref name="cutoff"/> and returns how many went (02 section 7.6, the <c>prune</c> job). It runs between flushes, never inside one, so the writer stays the
    /// only code that changes the database. Any other table is refused: trips are kept for ever.
    /// </summary>
    internal async Task<int> PruneAsync(string table, DateTimeOffset cutoff, int limit, CancellationToken cancellationToken)
    {
        var sql = table switch
        {
            "fixes" => PruneFixesSql,
            "signals" => PruneSignalsSql,
            _ => throw new ArgumentException("Only the raw tables are pruned", nameof(table)),
        };

        await _flushGate.WaitAsync(cancellationToken);
        try
        {
            await using var session = await DbSession.OpenAsync(_factory, cancellationToken);
            using var command = session.Connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.AddWithValue("@cutoff", SqlValues.Millis(cutoff));
            command.Parameters.AddWithValue("@limit", (long)limit);
            return command.ExecuteNonQuery();
        }
        finally
        {
            _flushGate.Release();
        }
    }

    /// <summary>Gives back up to 2 000 free pages to the file (<c>PRAGMA incremental_vacuum(2000)</c>, 02 section 7.6); like <see cref="PruneAsync"/>, between flushes.</summary>
    internal async Task VacuumAsync(CancellationToken cancellationToken)
    {
        await _flushGate.WaitAsync(cancellationToken);
        try
        {
            await using var session = await DbSession.OpenAsync(_factory, cancellationToken);
            Execute(session.Connection, "PRAGMA incremental_vacuum(2000)");
        }
        finally
        {
            _flushGate.Release();
        }
    }

    /// <summary>Folds the write-ahead log into the file and truncates it (<c>PRAGMA wal_checkpoint(TRUNCATE)</c>); like <see cref="PruneAsync"/>, between flushes.</summary>
    internal async Task CheckpointAsync(CancellationToken cancellationToken)
    {
        await _flushGate.WaitAsync(cancellationToken);
        try
        {
            await using var session = await DbSession.OpenAsync(_factory, cancellationToken);
            Execute(session.Connection, "PRAGMA wal_checkpoint(TRUNCATE)");
        }
        finally
        {
            _flushGate.Release();
        }
    }

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        // The 2 s timer exists before StartAsync returns, so a flush is never later than 2 s after the first row.
        _timer = _time.CreateTimer(_ => _bell.Writer.TryWrite(true), null, FlushInterval, FlushInterval);
        return base.StartAsync(cancellationToken);
    }

    /// <summary>Stops the loop, writes everything still queued, marks the database as cleanly stopped and checkpoints the WAL (03 section 2.13).</summary>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await base.StopAsync(cancellationToken);
        }
        finally
        {
            _timer?.Dispose();
            _timer = null;
            await CloseAsync(cancellationToken);
        }
    }

    public override void Dispose()
    {
        _timer?.Dispose();
        _flushGate.Dispose();
        base.Dispose();
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return _loop.RunAsync(ConsumeAsync, stoppingToken);
    }

    private async Task ConsumeAsync(CancellationToken cancellationToken)
    {
        // The bell rings when 100 rows are queued, when a trip close is waiting and every 2 s. A flush with nothing queued does nothing.
        while (await _bell.Reader.WaitToReadAsync(cancellationToken))
        {
            _bell.Reader.TryRead(out _);
            await FlushQueuedAsync(CancellationToken.None);
        }
    }

    // The depth and the drop count (an eviction of a diagnostic row counts too) as the counters of diagnostics.json show them; read again at every change.
    private void PublishQueue()
    {
        _counters?.SetWriterQueue(_queue.Count, _queue.Dropped);
    }

    private bool Accept(WriteCommand command)
    {
        var accepted = _queue.TryAdd(command);
        PublishQueue();
        if (!accepted)
        {
            NoteDrop();
            return false;
        }

        if (_queue.Count >= FlushRows)
        {
            _bell.Writer.TryWrite(true);
        }

        return true;
    }

    // One warning per minute at most; the counter has the exact number.
    private void NoteDrop()
    {
        var now = _time.GetUtcNow().ToUnixTimeMilliseconds();
        var last = Interlocked.Read(ref _lastDropLogMs);
        if (now - last >= 60_000 && Interlocked.CompareExchange(ref _lastDropLogMs, now, last) == last)
        {
            _logger.LogWarning("The write queue is full or stopped; {Dropped} rows dropped so far", _queue.Dropped);
        }
    }

    // Takes everything queued and commits it in order: a run of rows in one transaction, a trip close in its own. Whatever is not committed
    // goes back to the front of the queue, so a failure loses nothing and the next flush retries it.
    private async Task FlushQueuedAsync(CancellationToken cancellationToken)
    {
        await _flushGate.WaitAsync(cancellationToken);
        try
        {
            var commands = _queue.TakeAll();
            PublishQueue();
            var done = 0;
            try
            {
                while (done < commands.Count)
                {
                    if (commands[done] is WriteCommand.Trip trip)
                    {
                        try
                        {
                            await CommitTripAsync(trip);
                        }
                        catch (Exception ex)
                        {
                            trip.Done.TrySetException(ex);
                            done++;
                            throw;
                        }

                        done++;
                        continue;
                    }

                    if (commands[done] is WriteCommand.Roster roster)
                    {
                        try
                        {
                            await CommitRosterAsync(roster);
                        }
                        catch (Exception ex)
                        {
                            roster.Done.TrySetException(ex);
                            done++;
                            throw;
                        }

                        done++;
                        continue;
                    }

                    var end = done;
                    while (end < commands.Count && commands[end] is not (WriteCommand.Trip or WriteCommand.Roster))
                    {
                        end++;
                    }

                    await CommitRowsAsync(commands, done, end);
                    done = end;
                }
            }
            catch
            {
                _queue.PutBack(commands, done);
                PublishQueue();
                throw;
            }
        }
        finally
        {
            _flushGate.Release();
        }
    }

    private async Task CommitRowsAsync(IReadOnlyList<WriteCommand> commands, int start, int end)
    {
        var rows = end - start;
        await using (var session = await DbSession.OpenAsync(_factory, CancellationToken.None))
        {
            var connection = session.Connection;
            using var transaction = connection.BeginTransaction();
            var now = _time.GetUtcNow().ToUnixTimeMilliseconds();

            // A fix or signal needs its member row (foreign key), which OR IGNORE does not excuse; the earliest fix time per member keeps recording_start.
            var earliest = new Dictionary<string, long>(StringComparer.Ordinal);
            var members = new HashSet<string>(StringComparer.Ordinal);
            for (var i = start; i < end; i++)
            {
                switch (commands[i])
                {
                    case WriteCommand.Fix fix:
                        members.Add(fix.MemberId);
                        var ts = fix.Value.Ts.ToUnixTimeMilliseconds();
                        if (!earliest.TryGetValue(fix.MemberId, out var known) || ts < known)
                        {
                            earliest[fix.MemberId] = ts;
                        }

                        break;
                    case WriteCommand.Signal signal:
                        members.Add(signal.MemberId);
                        break;
                }
            }

            using (var ensure = new SqlStatement(connection, transaction, EnsureMemberSql, "@id", "@now"))
            {
                foreach (var member in members)
                {
                    ensure.Run(member, now);
                }
            }

            using var fixInsert = new SqlStatement(
                connection, transaction, FixSql,
                "@member", "@ts", "@source", "@lat", "@lon", "@acc", "@speed", "@heading", "@alt", "@battery", "@charging", "@driving", "@address", "@track", "@reason");
            using var signalInsert = new SqlStatement(connection, transaction, SignalSql, "@member", "@ts", "@kind", "@value");
            using var metaUpsert = new SqlStatement(connection, transaction, MetaSql, "@key", "@value");
            for (var i = start; i < end; i++)
            {
                switch (commands[i])
                {
                    case WriteCommand.Fix fix:
                        var f = fix.Value;
                        fixInsert.Run(
                            fix.MemberId, SqlValues.Millis(f.Ts), FixSourceText.ToText(f.Source), f.Lat, f.Lon,
                            SqlValues.Real(f.AccuracyM), SqlValues.Real(f.SpeedMps), SqlValues.Real(f.HeadingDeg), SqlValues.Real(f.AltitudeM),
                            SqlValues.Integer(f.BatteryPct), SqlValues.Flag(f.Charging), SqlValues.Flag(f.Driving), SqlValues.Text(f.Address),
                            fix.InTrack ? 1L : 0L, SqlValues.Text(ReasonText(fix.Reason)));
                        break;
                    case WriteCommand.Signal signal:
                        signalInsert.Run(signal.MemberId, SqlValues.Millis(signal.Value.Ts), SignalKindText(signal.Value.Kind), SignalValueText(signal.Value.IsOn));
                        break;
                    case WriteCommand.Meta meta:
                        metaUpsert.Run(meta.Key, meta.Value);
                        break;
                }
            }

            using (var recordingStart = new SqlStatement(connection, transaction, RecordingStartSql, "@id", "@ts"))
            {
                foreach (var (member, ts) in earliest)
                {
                    recordingStart.Run(member, ts);
                }
            }

            transaction.Commit();
        }

        Interlocked.Add(ref _committedRows, rows);
        Interlocked.Exchange(ref _lastCommitMs, _time.GetUtcNow().ToUnixTimeMilliseconds());
        _counters?.RecordDbCommit(rows, _time.GetUtcNow());
        RaiseRowsCommitted(rows);
    }

    // The rows are committed when this runs, so a subscriber's failure must not turn the run into a failed flush: that puts the rows back, commits them again
    // (idempotent) and raises the event again, and the writer would fault for ever with CommittedRows growing (CR2-011). Each subscriber is called on its own,
    // so one that throws does not starve the ones after it. The failure is logged once, with its type and never its message.
    private void RaiseRowsCommitted(int rows)
    {
        var handlers = RowsCommitted;
        if (handlers is null)
        {
            return;
        }

        foreach (var handler in handlers.GetInvocationList().Cast<Action<int>>())
        {
            try
            {
                handler(rows);
            }
            catch (Exception ex)
            {
                if (Interlocked.Exchange(ref _subscriberFailureLogged, 1) == 0)
                {
                    _logger.LogError("A RowsCommitted subscriber failed ({ErrorType}); the rows were committed and the writer carries on", ex.GetType().Name);
                }
            }
        }
    }

    // The roster rows replace the row of their entity id; times are Unix milliseconds like every other table, the group and kind are lower-case words.
    private async Task CommitRosterAsync(WriteCommand.Roster command)
    {
        await using (var session = await DbSession.OpenAsync(_factory, CancellationToken.None))
        {
            var connection = session.Connection;
            using var transaction = connection.BeginTransaction();
            using (var upsert = new SqlStatement(
                connection, transaction, RosterSql,
                "@id", "@kind", "@grp", "@name", "@lore", "@color", "@sort", "@source", "@first", "@active", "@moved",
                "@sname", "@stitle", "@scolor", "@oname", "@otitle", "@ocolor", "@icon", "@keep"))
            {
                foreach (var entry in command.Entries)
                {
                    upsert.Run(
                        entry.EntityId, RosterText.Kind(entry.Kind), RosterText.Group(entry.Group), entry.DisplayName, SqlValues.Text(entry.LoreTitle),
                        entry.Color, (long)entry.SortOrder, entry.Source, SqlValues.Millis(entry.FirstSeenUtc), SqlValues.Millis(entry.LastActiveUtc),
                        SqlValues.Millis(entry.AutoMovedUtc), SqlValues.Text(entry.SourceName), SqlValues.Text(entry.SourceTitle), SqlValues.Text(entry.SourceColor),
                        SqlValues.Text(entry.NameOverride), SqlValues.Text(entry.TitleOverride), SqlValues.Text(entry.ColorOverride), SqlValues.Text(entry.Icon), entry.KeepHistory ? 1L : 0L);
                }
            }

            transaction.Commit();
        }

        command.Done.TrySetResult();
    }

    private async Task CommitTripAsync(WriteCommand.Trip command)
    {
        bool inserted;
        await using (var session = await DbSession.OpenAsync(_factory, CancellationToken.None))
        {
            var connection = session.Connection;
            using var transaction = connection.BeginTransaction();
            var now = _time.GetUtcNow().ToUnixTimeMilliseconds();
            var trip = command.Value;

            using (var ensure = new SqlStatement(connection, transaction, EnsureMemberSql, "@id", "@now"))
            {
                ensure.Run(command.MemberId, now);
            }

            using (var insert = new SqlStatement(
                connection, transaction, TripSql,
                "@member", "@start", "@end", "@duration", "@slat", "@slon", "@elat", "@elon", "@splace", "@eplace", "@sstreet", "@estreet", "@dist",
                "@top", "@topts", "@topstreet", "@speeding", "@phone", "@quality", "@gap", "@endedby", "@mask", "@algo", "@hash", "@created"))
            {
                inserted = insert.Run(
                    command.MemberId, SqlValues.Millis(trip.StartUtc), SqlValues.Millis(trip.EndUtc), (long)trip.DurationS,
                    trip.StartLat, trip.StartLon, trip.EndLat, trip.EndLon,
                    SqlValues.Text(trip.StartPlaceId), SqlValues.Text(trip.EndPlaceId), SqlValues.Text(trip.StartStreet), SqlValues.Text(trip.EndStreet),
                    trip.DistanceGpsM,
                    SqlValues.Real(trip.TopSpeedMps), SqlValues.Millis(trip.TopSpeedAtUtc), SqlValues.Text(trip.TopSpeedStreet),
                    SqlValues.Integer(trip.SpeedingCount), SqlValues.Integer(trip.PhoneCount),
                    QualityText(trip.Quality), trip.HasGap ? 1L : 0L, EndedByText(trip.EndedBy), trip.SourceMask,
                    (long)command.AlgoVersion, command.DeriveHash, now) == 1;
            }

            if (inserted)
            {
                long tripId;
                using (var lastId = connection.CreateCommand())
                {
                    lastId.Transaction = transaction;
                    lastId.CommandText = "SELECT last_insert_rowid()";
                    tripId = Convert.ToInt64(lastId.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
                }

                using var eventInsert = new SqlStatement(connection, transaction, TripEventSql, "@trip", "@kind", "@start", "@end", "@peak", "@lat", "@lon");
                foreach (var episode in trip.SpeedingEpisodes)
                {
                    eventInsert.Run(
                        tripId, "speeding", SqlValues.Millis(episode.StartUtc), SqlValues.Millis(episode.EndUtc),
                        episode.PeakMps, episode.Lat, episode.Lon);
                }

                foreach (var phone in trip.PhoneEvents)
                {
                    // peak is the seconds of screen use for a phone event (02 section 7.2); a phone event has no position.
                    eventInsert.Run(
                        tripId, "phone", SqlValues.Millis(phone.StartUtc), SqlValues.Millis(phone.EndUtc),
                        phone.Seconds, DBNull.Value, DBNull.Value);
                }
            }

            transaction.Commit();
        }

        command.Done.TrySetResult(inserted);
    }

    // The queue is closed first, so nothing arrives behind the final flush. The database is marked clean only when everything queued was written.
    private async Task CloseAsync(CancellationToken cancellationToken)
    {
        _queue.Close();
        try
        {
            await FlushQueuedAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            var lost = FailPending(ex);
            _logger.LogError(ex, "Shutdown could not write {Rows} queued rows; clean_shutdown stays 0", lost);
            return;
        }

        try
        {
            await using var session = await DbSession.OpenAsync(_factory, CancellationToken.None);
            Execute(session.Connection, "INSERT OR REPLACE INTO meta(key, value) VALUES ('clean_shutdown', '1')");
            Execute(session.Connection, "PRAGMA wal_checkpoint(TRUNCATE)");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Shutdown could not mark the database as cleanly stopped");
        }
    }

    // Whatever is still queued after a failed final flush is lost: the callers waiting for a trip are told, the rows are counted.
    private int FailPending(Exception error)
    {
        var pending = _queue.TakeAll();
        PublishQueue();
        foreach (var command in pending)
        {
            if (command is WriteCommand.Trip trip)
            {
                trip.Done.TrySetException(error);
            }
            else if (command is WriteCommand.Roster roster)
            {
                roster.Done.TrySetException(error);
            }
        }

        return pending.Count;
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    // 02 section 7.2: acc | spike | priority. A fix the detector dropped as a duplicate or an older one has no stored reason.
    private static string? ReasonText(TrackReason? reason)
    {
        return reason switch
        {
            TrackReason.Accuracy => "acc",
            TrackReason.Spike => "spike",
            TrackReason.Priority => "priority",
            _ => null,
        };
    }

    private static string SignalKindText(PhoneSignalKind kind)
    {
        return kind switch
        {
            PhoneSignalKind.Screen => "screen",
            PhoneSignalKind.Locked => "locked",
            _ => "android_auto",
        };
    }

    private static string SignalValueText(bool? isOn)
    {
        return isOn switch
        {
            true => "on",
            false => "off",
            null => "unavailable",
        };
    }

    private static string QualityText(TripQuality quality)
    {
        return quality == TripQuality.Dense ? "dense" : "coarse";
    }

    private static string EndedByText(TripEndedBy endedBy)
    {
        return endedBy switch
        {
            TripEndedBy.Stop => "stop",
            TripEndedBy.NoFix => "nofix",
            _ => "coarse",
        };
    }
}
