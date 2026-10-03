using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Realm.Infrastructure.Data;
using Realm.Infrastructure.Diagnostics;
using Realm.Infrastructure.Hosting;
using Realm.Infrastructure.Ingestion;
using Realm.Infrastructure.Options;

namespace Realm.Infrastructure.Retention;

/// <summary>
/// The <c>prune</c> job (02 section 7.6, 03 section 2.4): five minutes after start and then every day at 03:30 in HA's time zone, the fixes, vehicle samples
/// and signals older than <c>retention_fix_days</c> are deleted in batches of 5 000 rows with 200 ms between them, then <c>PRAGMA incremental_vacuum(2000)</c>
/// gives the pages back, and once a week <c>PRAGMA wal_checkpoint(TRUNCATE)</c> shrinks the log. Trips, trip events, members, meta and job state are kept for
/// ever, and <c>members.recording_start</c> is not moved: coverage refers to when the Realm first recorded, not to what it kept. A retention of 0 days or fewer
/// keeps everything. The deletes go through <see cref="DbWriter"/> and take the same gate as its flushes, so the writer remains the only code that changes the
/// database.
/// </summary>
public sealed class RetentionService : BackgroundService
{
    /// <summary>Rows deleted per batch.</summary>
    public const int BatchRows = 5_000;

    /// <summary>The first run comes this long after start.</summary>
    public static readonly TimeSpan FirstRunDelay = TimeSpan.FromMinutes(5);

    /// <summary>The time between batches, so a flush is never starved.</summary>
    public static readonly TimeSpan BatchGap = TimeSpan.FromMilliseconds(200);

    private static readonly TimeSpan CheckpointEvery = TimeSpan.FromDays(7);
    private static readonly TimeSpan DailyAt = new(3, 30, 0);

    private readonly DbWriter _writer;
    private readonly RealmState _state;
    private readonly RealmOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly ServiceCounters? _counters;
    private readonly ResilientLoop _loop;
    private DateTimeOffset? _lastCheckpoint;
    private long _lastRunMs;
    private long _lastDeleted;

    /// <param name="counters">Where finished prune runs and the rows they deleted are counted for <c>diagnostics.json</c>; null counts nothing.</param>
    public RetentionService(
        DbWriter writer,
        RealmState state,
        RealmOptions options,
        TimeProvider time,
        ILogger<RetentionService> logger,
        ServiceCounters? counters = null)
    {
        _writer = writer;
        _state = state;
        _options = options;
        _time = time;
        _logger = logger;
        _counters = counters;
        _loop = new ResilientLoop(nameof(RetentionService), logger, time);
    }

    public ServiceHealth Health => _loop.Health;

    /// <summary>When the last run ended; null before the first one (diagnostics).</summary>
    public DateTimeOffset? LastRunUtc
    {
        get
        {
            var ms = Interlocked.Read(ref _lastRunMs);
            return ms == 0 ? null : DateTimeOffset.FromUnixTimeMilliseconds(ms);
        }
    }

    /// <summary>Rows the last run deleted (diagnostics).</summary>
    public long LastDeleted => Interlocked.Read(ref _lastDeleted);

    /// <summary>The next 03:30 in <paramref name="zone"/> that is later than <paramref name="now"/>, as a UTC instant.</summary>
    public static DateTimeOffset NextDailyRun(DateTimeOffset now, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        var today = TimeZoneInfo.ConvertTime(now, zone).Date + DailyAt;
        for (var day = 0; day <= 2; day++)
        {
            var local = DateTime.SpecifyKind(today.AddDays(day), DateTimeKind.Unspecified);
            while (zone.IsInvalidTime(local))
            {
                local = local.AddMinutes(1);
            }

            var utc = new DateTimeOffset(local, zone.GetUtcOffset(local)).ToUniversalTime();
            if (utc > now)
            {
                return utc;
            }
        }

        return now.AddDays(1);
    }

    /// <summary>
    /// One run of the job now: deletes what is older than <c>retention_fix_days</c>, batch by batch, gives the freed pages back and, if a week has passed
    /// since the last one, checkpoints the log. Returns the number of rows deleted.
    /// </summary>
    public async Task<int> PruneAsync(CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow();
        var deleted = 0;
        if (_options.RetentionFixDays > 0)
        {
            var cutoff = now - TimeSpan.FromDays(_options.RetentionFixDays);
            foreach (var table in DbWriter.PrunedTables)
            {
                int batch;
                do
                {
                    batch = await _writer.PruneAsync(table, cutoff, BatchRows, cancellationToken);
                    deleted += batch;
                    if (batch == BatchRows)
                    {
                        await Task.Delay(BatchGap, _time, cancellationToken);
                    }
                }
                while (batch == BatchRows);
            }

            await _writer.VacuumAsync(cancellationToken);
        }

        if (_lastCheckpoint is not { } last || now - last >= CheckpointEvery)
        {
            await _writer.CheckpointAsync(cancellationToken);
            _lastCheckpoint = now;
        }

        Interlocked.Exchange(ref _lastDeleted, deleted);
        Interlocked.Exchange(ref _lastRunMs, _time.GetUtcNow().ToUnixTimeMilliseconds());
        _counters?.RecordRetentionRun(deleted);
        if (deleted > 0)
        {
            _logger.LogInformation("Retention deleted {Rows} rows older than {Days} days", deleted, _options.RetentionFixDays);
        }

        return deleted;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) => _loop.RunAsync(RunAsync, stoppingToken);

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(FirstRunDelay, _time, cancellationToken);
        while (true)
        {
            await PruneAsync(cancellationToken);
            var wait = NextDailyRun(_time.GetUtcNow(), Zone()) - _time.GetUtcNow();
            await Task.Delay(wait > TimeSpan.Zero ? wait : TimeSpan.FromMinutes(1), _time, cancellationToken);
        }
    }

    // HA's zone as the snapshot reports it; UTC while it is unknown or if this machine does not know it.
    private TimeZoneInfo Zone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(_state.Current.Zone);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }
}
