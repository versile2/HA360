using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Realm.Domain;
using Realm.Infrastructure.Hosting;
using Realm.Infrastructure.Ingestion;
using Realm.Infrastructure.Options;

namespace Realm.Infrastructure.Ha;

/// <summary>
/// Finds out who the members are and what to watch (03 section 2.4, 02 section 1.2): discovery at start and every 15 minutes, the zones by one template call
/// every 5 minutes and at every reconnect. It publishes the result to <see cref="DiscoveryState"/>, hands it to the ingestion pipeline, and gives the
/// websocket its watch list (zones included, R-096; the connection subscribes again only when the set changed). It uses REST only, so it does not wait for
/// the websocket; a failure (Home Assistant still starting) is retried by the <see cref="ResilientLoop"/>. Start returns at once.
/// </summary>
public sealed class HaDiscoveryRefresher : BackgroundService
{
    /// <summary>How often the members, vehicles and zones are discovered again.</summary>
    public static readonly TimeSpan DiscoveryInterval = TimeSpan.FromMinutes(15);

    /// <summary>How often the zones are read again (a new or deleted zone is noticed by it).</summary>
    public static readonly TimeSpan ZonesInterval = TimeSpan.FromMinutes(5);

    // A reconnect right after a discovery already has fresh zones.
    private static readonly TimeSpan ReconnectZonesMinAge = TimeSpan.FromSeconds(5);

    private readonly IHaGateway _gateway;
    private readonly RealmOptions _options;
    private readonly DiscoveryState _state;
    private readonly Func<IngestItem, CancellationToken, ValueTask> _sink;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly ResilientLoop _loop;
    private readonly HashSet<string> _warned = new(StringComparer.Ordinal);
    private readonly Channel<bool> _reconnected = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
        SingleReader = true,
    });

    private long _lastRefreshTicks;
    private int _watched;
    private string? _lastSummary;

    /// <summary>
    /// Sets the watch list the options name explicitly (<see cref="HaDiscovery.SeedWatchList"/>) so that a connection that starts before the first discovery
    /// has finished does not subscribe to all of Home Assistant. Hosted services are all constructed before the first one starts.
    /// </summary>
    /// <param name="sink">Receives <see cref="DiscoveryUpdated"/> and <see cref="ZonesUpdated"/>; the pipeline's queue.</param>
    public HaDiscoveryRefresher(
        IHaGateway gateway,
        RealmOptions options,
        DiscoveryState state,
        Func<IngestItem, CancellationToken, ValueTask> sink,
        TimeProvider time,
        ILogger<HaDiscoveryRefresher> logger)
    {
        _gateway = gateway;
        _options = options;
        _state = state;
        _sink = sink;
        _time = time;
        _logger = logger;
        _loop = new ResilientLoop(nameof(HaDiscoveryRefresher), logger, time);
        var seed = HaDiscovery.SeedWatchList(options);
        if (seed.Count > 0)
        {
            gateway.SetWatchList(seed);
        }
    }

    public ServiceHealth Health => _loop.Health;

    /// <summary>When the last discovery or zone refresh finished; null before the first.</summary>
    public DateTimeOffset? LastRefreshUtc => _lastRefreshTicks == 0 ? null : new DateTimeOffset(Interlocked.Read(ref _lastRefreshTicks), TimeSpan.Zero);

    /// <summary>How many entities the last watch list held.</summary>
    public int WatchedEntityCount => Volatile.Read(ref _watched);

    protected override Task ExecuteAsync(CancellationToken stoppingToken) => _loop.RunAsync(RunAsync, stoppingToken);

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        // StartAsync must return at once (03 section 2.4).
        await Task.Yield();
        _gateway.Connected += OnConnected;
        try
        {
            var discoveryDue = _time.GetUtcNow();
            var zonesDue = DateTimeOffset.MaxValue;
            var zonesAt = DateTimeOffset.MinValue;
            var reconnected = false;
            while (!cancellationToken.IsCancellationRequested)
            {
                var now = _time.GetUtcNow();
                if (now >= discoveryDue)
                {
                    await DiscoverAsync(cancellationToken);
                    now = _time.GetUtcNow();
                    discoveryDue = now + DiscoveryInterval;
                    zonesDue = now + ZonesInterval;
                    zonesAt = now;
                    reconnected = false;
                }
                else if (now >= zonesDue || (reconnected && now - zonesAt >= ReconnectZonesMinAge))
                {
                    await RefreshZonesAsync(cancellationToken);
                    now = _time.GetUtcNow();
                    zonesDue = now + ZonesInterval;
                    zonesAt = now;
                    reconnected = false;
                }

                var next = discoveryDue < zonesDue ? discoveryDue : zonesDue;
                reconnected |= await WaitAsync(next - _time.GetUtcNow(), cancellationToken);
            }
        }
        finally
        {
            _gateway.Connected -= OnConnected;
        }
    }

    private void OnConnected() => _reconnected.Writer.TryWrite(true);

    // True when a reconnect rang the bell before the wait ran out.
    private async Task<bool> WaitAsync(TimeSpan wait, CancellationToken cancellationToken)
    {
        if (wait <= TimeSpan.Zero)
        {
            return DrainBell();
        }

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var sleep = Task.Delay(wait, _time, stop.Token);
        var ring = _reconnected.Reader.WaitToReadAsync(stop.Token).AsTask();
        await Task.WhenAny(sleep, ring);
        await stop.CancelAsync();
        try
        {
            await Task.WhenAll(sleep, ring);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The loser of the race was cancelled on purpose.
        }

        return DrainBell();
    }

    private bool DrainBell()
    {
        var rang = false;
        while (_reconnected.Reader.TryRead(out _))
        {
            rang = true;
        }

        return rang;
    }

    private async Task DiscoverAsync(CancellationToken cancellationToken)
    {
        var config = await _gateway.GetConfigAsync(cancellationToken);
        var integration = await _gateway.GetIntegrationEntitiesAsync(cancellationToken);
        var states = await _gateway.GetStatesAsync(HaDiscovery.StateFilter(_options, integration), cancellationToken);
        var result = HaDiscovery.Resolve(_options, config, integration, states, _time.GetUtcNow());

        foreach (var warning in result.Warnings)
        {
            if (_warned.Add(warning))
            {
                _logger.LogWarning("{Warning}", warning);
            }
        }

        _state.Publish(result);
        _gateway.SetWatchList(result.WatchList);
        await _sink(new DiscoveryUpdated(result), cancellationToken);
        Done(result.WatchList.Count);

        var summary = $"{result.Members.Count} members, {result.Vehicles.Count} vehicles, {result.Zones.Count} zones, {result.WatchList.Count} entities";
        if (summary != _lastSummary)
        {
            _lastSummary = summary;
            _logger.LogInformation("Discovery: {Members} members, {Vehicles} vehicles, {Zones} zones, watching {Entities} entities", result.Members.Count, result.Vehicles.Count, result.Zones.Count, result.WatchList.Count);
        }
    }

    private async Task RefreshZonesAsync(CancellationToken cancellationToken)
    {
        var zones = await _gateway.GetZonesAsync(cancellationToken);
        var current = _state.Current;

        // New zones must be watched or they never update live (R-096).
        var watch = current.WatchList.Where(id => !id.StartsWith("zone.", StringComparison.Ordinal))
            .Concat(zones.Select(zone => "zone." + zone.Id))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        _state.Publish(current with { Zones = zones, WatchList = watch });
        _gateway.SetWatchList(watch);
        await _sink(new ZonesUpdated(zones), cancellationToken);
        Done(watch.Length);
    }

    private void Done(int watched)
    {
        Volatile.Write(ref _watched, watched);
        Interlocked.Exchange(ref _lastRefreshTicks, _time.GetUtcNow().UtcTicks);
    }
}
