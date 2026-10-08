using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Realm.Domain;
using Realm.Infrastructure.Diagnostics;
using Realm.Infrastructure.Hosting;
using Realm.Infrastructure.Ingestion;
using Realm.Infrastructure.Roster;

namespace Realm.Infrastructure.Ha;

/// <summary>
/// Finds out who can be on the map and what to watch (03 section 2.4, 02 section 1.2): discovery at start and every 15 minutes, the zones by one template call
/// every 5 minutes and at every reconnect. Each discovery finds the people and GPS trackers (<see cref="HaDiscovery.FindEntities"/>), lets the
/// <see cref="RosterService"/> apply the lifecycle rules to them (a new one joins People, a lost one is retired, D113) and sends the persistent notifications those
/// events ask for, then resolves the roster into members and vehicles. It publishes the result to <see cref="DiscoveryState"/>, hands it to the ingestion
/// pipeline, and gives the websocket its watch list (zones included, R-096; the connection subscribes again only when the set changed). When the owner changes
/// the roster in Settings it resolves again from the last discovery, at once. It reads Home Assistant by REST only, so it does not wait for the websocket; a
/// failure (Home Assistant still starting) is retried by the <see cref="ResilientLoop"/>. Start returns at once.
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
    private readonly RosterService _roster;
    private readonly DiscoveryState _state;
    private readonly Func<IngestItem, CancellationToken, ValueTask> _sink;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly ServiceCounters? _counters;
    private readonly ResilientLoop _loop;
    private readonly HashSet<string> _warned = new(StringComparer.Ordinal);
    private readonly Channel<bool> _reconnected = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
        SingleReader = true,
    });

    private readonly Channel<bool> _rosterChanged = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
        SingleReader = true,
    });

    // Notifications that Home Assistant did not take yet, by notification id: a later discovery sends them again (the id makes that safe).
    private readonly Dictionary<string, RosterNotice> _unsent = new(StringComparer.Ordinal);
    private Found? _found;

    private long _lastRefreshTicks;
    private int _watched;
    private string? _lastSummary;

    /// <param name="sink">Receives <see cref="DiscoveryUpdated"/> and <see cref="ZonesUpdated"/>; the pipeline's queue.</param>
    /// <param name="counters">Where the size of the watch list is kept for <c>diagnostics.json</c>; null counts nothing.</param>
    public HaDiscoveryRefresher(
        IHaGateway gateway,
        RosterService roster,
        DiscoveryState state,
        Func<IngestItem, CancellationToken, ValueTask> sink,
        TimeProvider time,
        ILogger<HaDiscoveryRefresher> logger,
        ServiceCounters? counters = null)
    {
        _gateway = gateway;
        _roster = roster;
        _state = state;
        _sink = sink;
        _time = time;
        _logger = logger;
        _counters = counters;
        _loop = new ResilientLoop(nameof(HaDiscoveryRefresher), logger, time);
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
        _roster.Changed += OnRosterChanged;
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

                if (DrainRosterBell() && _found is not null)
                {
                    await PublishAsync(_found, cancellationToken);
                }

                var next = discoveryDue < zonesDue ? discoveryDue : zonesDue;
                reconnected |= await WaitAsync(next - _time.GetUtcNow(), cancellationToken);
            }
        }
        finally
        {
            _gateway.Connected -= OnConnected;
            _roster.Changed -= OnRosterChanged;
        }
    }

    private void OnConnected() => _reconnected.Writer.TryWrite(true);

    private void OnRosterChanged() => _rosterChanged.Writer.TryWrite(true);

    private bool DrainRosterBell()
    {
        var rang = false;
        while (_rosterChanged.Reader.TryRead(out _))
        {
            rang = true;
        }

        return rang;
    }

    // True when a reconnect rang the bell before the wait ran out. A change of the roster ends the wait too (the loop then resolves again); it stays in its own bell.
    private async Task<bool> WaitAsync(TimeSpan wait, CancellationToken cancellationToken)
    {
        if (wait <= TimeSpan.Zero)
        {
            return DrainBell();
        }

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var sleep = Task.Delay(wait, _time, stop.Token);
        var ring = _reconnected.Reader.WaitToReadAsync(stop.Token).AsTask();
        var roster = _rosterChanged.Reader.WaitToReadAsync(stop.Token).AsTask();
        await Task.WhenAny(sleep, ring, roster);
        await stop.CancelAsync();
        try
        {
            await Task.WhenAll(sleep, ring, roster);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The losers of the race were cancelled on purpose.
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
        await _roster.LoadAsync(cancellationToken);
        var config = await _gateway.GetConfigAsync(cancellationToken);
        var integration = await _gateway.GetIntegrationEntitiesAsync(cancellationToken);
        var states = await _gateway.GetStatesAsync(HaDiscovery.StateFilter(integration), cancellationToken);
        var warnings = new List<string>();
        var entities = HaDiscovery.FindEntities(integration, states, warnings);
        var now = _time.GetUtcNow();

        // The 30 day rule only matters on the first start: a later pass does not look at the history.
        var recent = _roster.Entries.Count == 0 ? await RecentTrackersAsync(entities, states, now, cancellationToken) : EmptySet;
        var reconciliation = await _roster.ReconcileAsync([.. entities.Select(e => e.ToCandidate())], recent, cancellationToken);

        _found = new Found(config, entities, states, warnings);
        await PublishAsync(_found, cancellationToken);
        DrainRosterBell();
        await SendNoticesAsync(reconciliation.Notices, cancellationToken);
    }

    private static readonly IReadOnlySet<string> EmptySet = new HashSet<string>(StringComparer.Ordinal);

    // First start (D113): a GPS tracker that no person owns joins People only if it had a position in the last 30 days. The history of the tracker says it (a
    // state other than unavailable or unknown at some time in the window, without attributes, so the answer is small); a tracker whose history is empty or cannot
    // be read is judged by the update time of its current state, and one that cannot be judged at all is kept rather than lost.
    private async Task<IReadOnlySet<string>> RecentTrackersAsync(
        IReadOnlyList<DiscoveredEntity> entities,
        IReadOnlyList<HaEntitySnapshot> states,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var since = now - RosterRules.FirstStartWindow;
        var byId = states.ToDictionary(s => s.EntityId, StringComparer.Ordinal);
        var recent = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entity in entities.Where(e => e.Kind == RosterKind.Tracker))
        {
            var usable = false;
            try
            {
                var rows = await _gateway.GetHistoryAsync(entity.EntityId, since, now, withAttributes: false, cancellationToken);
                usable = rows.Any(row => !string.Equals(row.State, "unavailable", StringComparison.OrdinalIgnoreCase) && !string.Equals(row.State, "unknown", StringComparison.OrdinalIgnoreCase));
                if (rows.Count == 0)
                {
                    usable = byId.TryGetValue(entity.EntityId, out var current) && (current.LastUpdatedUtc is not { } updated || updated >= since);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning("The history of a tracker could not be read at the first start ({ErrorType}); it is kept", ex.GetType().Name);
                usable = true;
            }

            if (usable)
            {
                recent.Add(entity.EntityId);
            }
        }

        return recent;
    }

    // Resolves the roster against the last discovery and hands the result to everyone who needs it.
    private async Task PublishAsync(Found found, CancellationToken cancellationToken)
    {
        var result = HaDiscovery.Resolve(found.Config, found.Entities, found.States, _roster.Entries, found.Warnings);

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

    // Each notice goes to Home Assistant once its roster change is stored. One that Home Assistant does not take is kept and sent again at the next discovery.
    private async Task SendNoticesAsync(IReadOnlyList<RosterNotice> notices, CancellationToken cancellationToken)
    {
        foreach (var notice in notices)
        {
            _unsent[notice.NotificationId] = notice;
        }

        foreach (var notice in _unsent.Values.ToList())
        {
            try
            {
                await _gateway.NotifyAsync(notice.NotificationId, notice.Title, notice.Message, cancellationToken);
                _unsent.Remove(notice.NotificationId);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning("A notification could not be sent to Home Assistant ({ErrorType}); it is tried again at the next discovery", ex.GetType().Name);
            }
        }
    }

    private sealed record Found(HaConfig Config, IReadOnlyList<DiscoveredEntity> Entities, IReadOnlyList<HaEntitySnapshot> States, IReadOnlyList<string> Warnings);

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
        _counters?.SetWatchedEntities(watched);
        Interlocked.Exchange(ref _lastRefreshTicks, _time.GetUtcNow().UtcTicks);
    }
}
