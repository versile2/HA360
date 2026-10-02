using System.Threading.Channels;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Realm.Domain;
using Realm.Infrastructure.Hosting;

namespace Realm.Infrastructure.Data;

/// <summary>
/// The single database writer (02 section 7.3, 03 sections 2.4, 2.12 and 2.13). Producers queue rows through <see cref="IRealmWriter"/>; one consumer commits
/// the queue in one transaction whenever 100 rows are waiting or 2 s have passed, so a crash loses at most 2 s of fixes (the HA history gap-fill restores
/// them). Fixes and signals are <c>INSERT OR IGNORE</c>, vehicle samples merge with <c>ON CONFLICT DO UPDATE ... COALESCE</c>, meta rows replace. The queue holds
/// <see cref="QueueCapacity"/> rows and drops <c>track = 0</c> diagnostic rows first when it is full. A trip close is written on its own, after the flush
/// that holds everything queued before it. On a graceful stop the queue is drained, <c>meta.clean_shutdown</c> is set to <c>'1'</c> and the WAL is
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

    // "COALESCE(excluded.col, col)": a later sample of the same vehicle and time fills what is empty and never erases a value.
    private const string VehicleSampleSql =
        "INSERT INTO vehicle_samples(vehicle_id, ts, odo_m, fuel_pct, ignition, gear, speed_mps, remote_start_s, lat, lon) "
        + "VALUES (@vehicle, @ts, @odo, @fuel, @ignition, @gear, @speed, @remote, @lat, @lon) "
        + "ON CONFLICT(vehicle_id, ts) DO UPDATE SET "
        + "odo_m = COALESCE(excluded.odo_m, odo_m), fuel_pct = COALESCE(excluded.fuel_pct, fuel_pct), "
        + "ignition = COALESCE(excluded.ignition, ignition), gear = COALESCE(excluded.gear, gear), "
        + "speed_mps = COALESCE(excluded.speed_mps, speed_mps), remote_start_s = COALESCE(excluded.remote_start_s, remote_start_s), "
        + "lat = COALESCE(excluded.lat, lat), lon = COALESCE(excluded.lon, lon)";

    private const string SignalSql =
        "INSERT OR IGNORE INTO signals(member_id, ts, kind, value) VALUES (@member, @ts, @kind, @value)";

    private const string PruneFixesSql = "DELETE FROM fixes WHERE id IN (SELECT id FROM fixes WHERE ts < @cutoff ORDER BY id LIMIT @limit)";

    private const string PruneVehicleSamplesSql = "DELETE FROM vehicle_samples WHERE id IN (SELECT id FROM vehicle_samples WHERE ts < @cutoff ORDER BY id LIMIT @limit)";

    private const string PruneSignalsSql = "DELETE FROM signals WHERE id IN (SELECT id FROM signals WHERE ts < @cutoff ORDER BY id LIMIT @limit)";

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
    internal static readonly string[] PrunedTables = ["fixes", "vehicle_samples", "signals"];

    private readonly IDbContextFactory<RealmDb> _factory;
    private readonly TimeProvider _time;
    private readonly ILogger<DbWriter> _logger;
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

    public DbWriter(IDbContextFactory<RealmDb> factory, TimeProvider time, ILogger<DbWriter> logger)
    {
        _factory = factory;
        _time = time;
        _logger = logger;
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
        if (fix.Source is not (FixSource.Life360 or FixSource.Companion))
        {
            throw new ArgumentException("Only life360 and companion fixes are stored as fixes; FordPass readings are vehicle samples", nameof(fix));
        }

        // lat and lon are NOT NULL columns and SQLite stores NaN as NULL: one such row would fail every flush after it, so it is refused here.
        if (!double.IsFinite(fix.Lat) || !double.IsFinite(fix.Lon))
        {
            return false;
        }

        return Accept(new WriteCommand.Fix(memberId, fix, inTrack, inTrack ? null : reason));
    }

    public bool EnqueueVehicleSample(VehicleSample sample)
    {
        return Accept(new WriteCommand.VehicleSampleRow(sample));
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
        if (!_queue.TryAdd(new WriteCommand.Trip(memberId, trip, algoVersion, deriveHash, done)))
        {
            throw new InvalidOperationException("The database writer has stopped");
        }

        _bell.Writer.TryWrite(true);
        return await done.Task.WaitAsync(cancellationToken);
    }

    public Task FlushAsync(CancellationToken cancellationToken = default)
    {
        return FlushQueuedAsync(cancellationToken);
    }

    /// <summary>
    /// Deletes up to <paramref name="limit"/> of the oldest rows of one of the raw tables (<c>fixes</c>, <c>signals</c>, <c>vehicle_samples</c>) with a time before
    /// <paramref name="cutoff"/> and returns how many went (02 section 7.6, the <c>prune</c> job). It runs between flushes, never inside one, so the writer stays the
    /// only code that changes the database. Any other table is refused: trips are kept for ever.
    /// </summary>
    internal async Task<int> PruneAsync(string table, DateTimeOffset cutoff, int limit, CancellationToken cancellationToken)
    {
        var sql = table switch
        {
            "fixes" => PruneFixesSql,
            "vehicle_samples" => PruneVehicleSamplesSql,
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

    private bool Accept(WriteCommand command)
    {
        if (!_queue.TryAdd(command))
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

                    var end = done;
                    while (end < commands.Count && commands[end] is not WriteCommand.Trip)
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
            using var sampleUpsert = new SqlStatement(
                connection, transaction, VehicleSampleSql,
                "@vehicle", "@ts", "@odo", "@fuel", "@ignition", "@gear", "@speed", "@remote", "@lat", "@lon");
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
                    case WriteCommand.VehicleSampleRow row:
                        var s = row.Value;
                        sampleUpsert.Run(
                            s.VehicleId, SqlValues.Millis(s.Ts), SqlValues.Real(s.OdometerM), SqlValues.Integer(s.FuelPct), SqlValues.Text(s.Ignition),
                            SqlValues.Text(s.Gear), SqlValues.Real(s.SpeedMps), SqlValues.Integer(s.RemoteStartSeconds), SqlValues.Real(s.Lat), SqlValues.Real(s.Lon));
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
        foreach (var command in pending)
        {
            if (command is WriteCommand.Trip trip)
            {
                trip.Done.TrySetException(error);
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
