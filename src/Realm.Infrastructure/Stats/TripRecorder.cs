using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Realm.Domain;
using Realm.Infrastructure.Hosting;
using Realm.Infrastructure.Ingestion;

namespace Realm.Infrastructure.Stats;

/// <summary>
/// Persists the trips the live detector closes (D59, D66). It subscribes to <see cref="IngestionPipeline.TripClosed"/>, which is raised on the pipeline's
/// consumer and so only queues the trip here, and its own loop hands each one to <see cref="StatsService.RecordTripAsync"/> in the order they closed: a
/// SQLite commit never delays what the user sees. A trip that cannot be written is logged and dropped (the start-up replay and the backfill find it again
/// while its fixes are stored); the loop goes on with the next. On a graceful stop the queued trips are written first.
/// </summary>
public sealed class TripRecorder : BackgroundService
{
    private static readonly TimeSpan ErrorLogInterval = TimeSpan.FromMinutes(1);

    private readonly IngestionPipeline _pipeline;
    private readonly StatsService _stats;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly ResilientLoop _loop;
    private readonly Channel<(string MemberId, DetectedTrip Trip)> _queue = Channel.CreateUnbounded<(string MemberId, DetectedTrip Trip)>(
        new UnboundedChannelOptions { SingleReader = true });
    private DateTimeOffset _lastErrorLogged = DateTimeOffset.MinValue;
    private long _recorded;
    private int _depth;

    public TripRecorder(IngestionPipeline pipeline, StatsService stats, TimeProvider time, ILogger<TripRecorder> logger)
    {
        _pipeline = pipeline;
        _stats = stats;
        _time = time;
        _logger = logger;
        _loop = new ResilientLoop(nameof(TripRecorder), logger, time);
        _pipeline.TripClosed += OnClosed;
    }

    public ServiceHealth Health => _loop.Health;

    /// <summary>Trips written as new rows since start (diagnostics).</summary>
    public long RecordedCount => Interlocked.Read(ref _recorded);

    /// <summary>Trips waiting to be written (diagnostics).</summary>
    public int QueueDepth => Volatile.Read(ref _depth);

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _queue.Writer.TryComplete();
        await base.StopAsync(cancellationToken);
    }

    public override void Dispose()
    {
        _pipeline.TripClosed -= OnClosed;
        base.Dispose();
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) => _loop.RunAsync(ConsumeAsync, stoppingToken);

    private void OnClosed(string memberId, DetectedTrip trip)
    {
        Interlocked.Increment(ref _depth);
        if (!_queue.Writer.TryWrite((memberId, trip)))
        {
            Interlocked.Decrement(ref _depth);   // the queue is completed: the service is stopping, and the next start finds the trip again
        }
    }

    // Reads until the queue is completed (by StopAsync), so what is queued at shutdown is still written.
    private async Task ConsumeAsync(CancellationToken stoppingToken)
    {
        await foreach (var (memberId, trip) in _queue.Reader.ReadAllAsync(CancellationToken.None))
        {
            Interlocked.Decrement(ref _depth);
            try
            {
                if (await _stats.RecordTripAsync(memberId, trip, CancellationToken.None))
                {
                    Interlocked.Increment(ref _recorded);
                }
            }
            catch (Exception ex)
            {
                var now = _time.GetUtcNow();
                if (now - _lastErrorLogged >= ErrorLogInterval)
                {
                    _lastErrorLogged = now;
                    _logger.LogError(ex, "A closed trip of member {MemberId} could not be written", memberId);
                }
            }
        }
    }
}
