using System.Globalization;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Realm.Domain;
using Realm.Infrastructure.Data;
using Realm.Infrastructure.Ha;
using Realm.Infrastructure.Hosting;
using Realm.Infrastructure.Options;
using Realm.Infrastructure.Stats;

namespace Realm.Infrastructure.Ingestion;

/// <summary>
/// The one consumer of everything Home Assistant and discovery report (03 section 2.8). For each <see cref="IngestItem"/>: normalise the entity states with
/// <see cref="FixParser"/>, fuse each person's sources (<see cref="Fuse"/>, <see cref="PlaceResolver"/>, <see cref="FreshnessRules"/>), feed the member's
/// <see cref="TripDetector"/> (in memory, so it never delays the user), <b>publish the new snapshot to <see cref="RealmState"/></b>, and only then queue the
/// rows on <see cref="IRealmWriter"/>: a SQLite commit must never delay what the user sees. A 30 s tick (<see cref="ChangeNotifier.Tick"/>) lets rows age into
/// stale and offline without events and closes trips by silence.
/// </summary>
/// <remarks>
/// <para>
/// Not done here: persisting a closed trip. <see cref="TripClosed"/> is the seam, raised after the snapshot is published; <see cref="TripRecorder"/> subscribes
/// and <see cref="StatsService"/> writes the trip (phone-use count, algorithm version, derive hash, <see cref="RealmSnapshot.StatsVersion"/>). The backfill is
/// <see cref="Backfill.BackfillService"/>. A new member is hydrated from the database by <see cref="RealmStateHydrator"/>: the latest stored fixes, the fix
/// times of the last 24 hours and a replay of the last 30 minutes through its detector.
/// </para>
/// <para>
/// All state is guarded by one lock, held for a few microseconds per item and never across I/O; <see cref="ProcessAsync"/> is what the consumer loop calls and
/// what tests call directly.
/// </para>
/// </remarks>
public sealed class IngestionPipeline : BackgroundService
{
    private const int QueueCapacity = 4096;
    private const string ZonePrefix = "zone.";

    private static readonly TimeSpan FixHistory = TimeSpan.FromHours(24);
    private static readonly TimeSpan CensoredRunMaxAge = TimeSpan.FromDays(7);
    private static readonly TimeSpan ErrorLogInterval = TimeSpan.FromMinutes(1);

    // A check on the HomeAssistant entry runs this long after the instant at which the entry changes (the limits of 02 section 1.8 are inclusive).
    private static readonly TimeSpan CheckMargin = TimeSpan.FromSeconds(1);

    private readonly RealmOptions _options;
    private readonly RealmState _state;
    private readonly ChangeNotifier _notifier;
    private readonly IRealmWriter _writer;
    private readonly IRealmQueries _queries;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly ResilientLoop _loop;
    private readonly DetectionSettings _detection;
    private readonly RealmStateHydrator _hydrator;
    private readonly Func<HaConnectionStatus>? _liveStatus;
    private readonly Channel<IngestItem> _channel = Channel.CreateBounded<IngestItem>(new BoundedChannelOptions(QueueCapacity)
    {
        FullMode = BoundedChannelFullMode.Wait,
        SingleReader = true,
    });

    private readonly object _gate = new();
    private readonly Dictionary<string, HaEntitySnapshot> _entities = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (MemberRuntime Member, FixSource Source)> _trackerOwners = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (MemberRuntime Member, SensorRole Role)> _sensorOwners = new(StringComparer.Ordinal);
    private readonly Dictionary<string, VehicleRuntime> _vehicleOwners = new(StringComparer.Ordinal);
    private readonly List<Action<IRealmWriter>> _writes = [];
    private readonly List<(string MemberId, DetectedTrip Trip)> _closed = [];

    private Dictionary<string, MemberRuntime> _members = new(StringComparer.Ordinal);
    private Dictionary<string, VehicleRuntime> _vehicles = new(StringComparer.Ordinal);
    private Dictionary<string, RawPlace> _zones = new(StringComparer.Ordinal);
    private IReadOnlyList<PlaceDef> _places = [];
    private IReadOnlyList<RawPlace> _drawn = [];
    private HashSet<string>? _watched;
    private string _zoneId = "UTC";
    private string? _zoneMetaQueued;
    private HaConnectionStatus _connection;
    private ConnectionState[] _lastConnections = [];
    private ConnectionState? _publishedHomeAssistant;
    private bool _dirty;
    private bool _zonesDirty;
    private bool _disposed;
    private ITimer? _livenessTimer;
    private DateTimeOffset? _livenessDue;
    private long _processed;
    private DateTimeOffset _lastErrorLogged = DateTimeOffset.MinValue;

    /// <param name="options">The add-on options.</param>
    /// <param name="state">Where every snapshot is published.</param>
    /// <param name="notifier">Announces the snapshots and raises the 30 s tick.</param>
    /// <param name="writer">Receives the rows after the snapshot that follows them was published.</param>
    /// <param name="queries">The stored fixes and trips, for hydrating a new member.</param>
    /// <param name="time">The clock of every measurement and timer.</param>
    /// <param name="logger">The pipeline's log.</param>
    /// <param name="hydrator">Hydrates a new member; created from <paramref name="queries"/> when null.</param>
    /// <param name="connection">
    /// The websocket whose status the HomeAssistant entry of the snapshot follows. Its status is read every time a snapshot is built, because the connection
    /// announces only a change of its state, never the state events and ping replies that keep it alive (02 section 1.8 counts those). Null (the pipeline
    /// on its own, in a test) leaves the entry to <see cref="ApplyConnectionStatus"/>. The service provider fills it in with the registered connection.
    /// </param>
    public IngestionPipeline(
        RealmOptions options,
        RealmState state,
        ChangeNotifier notifier,
        IRealmWriter writer,
        IRealmQueries queries,
        TimeProvider time,
        ILogger<IngestionPipeline> logger,
        RealmStateHydrator? hydrator = null,
        HaWebSocketConnection? connection = null)
    {
        _options = options;
        _state = state;
        _notifier = notifier;
        _writer = writer;
        _queries = queries;
        _time = time;
        _logger = logger;
        _loop = new ResilientLoop(nameof(IngestionPipeline), logger, time);
        _hydrator = hydrator ?? new RealmStateHydrator(queries);
        _detection = new DetectionSettings(options);
        _liveStatus = connection is null ? null : () => connection.Status;

        // Until the websocket says otherwise Home Assistant is being reached for the first time: Reconnecting for 15 s, then Unavailable.
        _connection = new HaConnectionStatus(HaConnectionState.Connecting, time.GetUtcNow(), null, 0, null);
        _notifier.Tick += OnTick;
    }

    /// <summary>
    /// A trip closed (a drive that is valid by 02 section 5.5), raised on the thread that processed the item, after the snapshot that follows it was
    /// published. It is raised for the trips a start-up replay finds too. A subscriber must hand the trip off at once (<see cref="TripRecorder"/> queues it
    /// for its own loop): this runs on the pipeline's consumer. A subscriber that throws is logged and skipped.
    /// </summary>
    public event Action<string, DetectedTrip>? TripClosed;

    public ServiceHealth Health => _loop.Health;

    /// <summary>Items processed since start (diagnostics).</summary>
    public long ProcessedCount => Interlocked.Read(ref _processed);

    /// <summary>Items waiting in the queue (diagnostics).</summary>
    public int QueueDepth => _channel.Reader.Count;

    /// <summary>
    /// Queues an item; waits while 4 096 are queued, which slows the socket reader and never drops a diff (diffs are order-dependent). After shutdown has
    /// begun the item is ignored.
    /// </summary>
    public async ValueTask EnqueueAsync(IngestItem item, CancellationToken cancellationToken)
    {
        try
        {
            await _channel.Writer.WriteAsync(item, cancellationToken);
        }
        catch (ChannelClosedException)
        {
            // Shutting down: the connection may still be delivering.
        }
    }

    /// <summary>
    /// The websocket's state changed: the Home Assistant connection entry of the snapshot follows at once (a state change is never throttled). When the
    /// pipeline was given the connection, the entry is built from the connection's status as it is now (with the activity since this change), and
    /// <paramref name="status"/> only says that it is time to look.
    /// </summary>
    public void ApplyConnectionStatus(HaConnectionStatus status)
    {
        Publication publication;
        lock (_gate)
        {
            _connection = status;
            _dirty = true;
            publication = PublishLocked(_time.GetUtcNow());
        }

        Announce(publication);
    }

    /// <summary>The 30 s data tick (03 section 2.9): ages freshness, speed and battery, and closes trips that have been silent too long.</summary>
    public void Tick()
    {
        Publication publication;
        List<(string MemberId, DetectedTrip Trip)> closed;
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            foreach (var member in _members.Values)
            {
                if (member.Detector is { } detector)
                {
                    CollectClosed(member, detector.Tick(now));
                }

                PruneAndMeasureHeartbeat(member, now);
            }

            _dirty = true;
            publication = PublishLocked(now);
            closed = TakeClosed();
        }

        Announce(publication);
        RaiseClosed(closed);
    }

    /// <summary>
    /// Processes one item: update, publish, announce, then persist. The consumer loop calls it for every queued item; tests call it directly, so a test needs no
    /// socket, no queue and no clock but its own.
    /// </summary>
    public async Task ProcessAsync(IngestItem item, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        IReadOnlyList<MemberRuntime> added = [];
        if (item is DiscoveryUpdated discovery)
        {
            lock (_gate)
            {
                added = ApplyDiscovery(discovery.Discovery);
            }

            await SeedAsync(added, cancellationToken);
        }

        Publication publication;
        Action<IRealmWriter>[] writes;
        List<(string MemberId, DetectedTrip Trip)> closed;
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            switch (item)
            {
                case DiscoveryUpdated:
                    ApplyAll(now, first: true);
                    _dirty = true;
                    break;
                case ZonesUpdated zones:
                    _zones = zones.Zones.ToDictionary(z => z.Id, StringComparer.Ordinal);
                    _zonesDirty = true;
                    _dirty = true;
                    break;
                case FeedItem { Item: HaSnapshot snapshot }:
                    ApplySnapshot(snapshot, now);
                    break;
                case FeedItem { Item: HaStateChanged change }:
                    ApplyChange(change, now);
                    break;
                default:
                    break;
            }

            MarkConnectionDirtyIfItReadsDifferentlyLocked(now);
            publication = PublishLocked(now);
            writes = [.. _writes];
            _writes.Clear();
            closed = TakeClosed();
        }

        Interlocked.Increment(ref _processed);

        // Published and announced above; only now do the rows go to the writer (03 section 2.8 step 3 before 4).
        Announce(publication);
        RaiseClosed(closed);
        Persist(writes);
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) => _loop.RunAsync(ConsumeAsync, stoppingToken);

    // The loop reads until the queue is completed (by StopAsync), so what is queued at shutdown is still processed.
    private async Task ConsumeAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        _notifier.Start();

        // The first snapshot with a clock of its own; publishing arms the check that turns the first 15 s of Reconnecting into Unavailable when HA never answers.
        Publication first;
        lock (_gate)
        {
            _dirty = true;
            first = PublishLocked(_time.GetUtcNow());
        }

        Announce(first);
        await foreach (var item in _channel.Reader.ReadAllAsync(CancellationToken.None))
        {
            try
            {
                await ProcessAsync(item, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // One bad item must not stall the stream; the log has one line a minute, whatever the rate of failures.
                var now = _time.GetUtcNow();
                if (now - _lastErrorLogged >= ErrorLogInterval)
                {
                    _lastErrorLogged = now;
                    _logger.LogError(ex, "An ingestion item failed and was skipped");
                }
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _channel.Writer.TryComplete();
        await base.StopAsync(cancellationToken);
    }

    public override void Dispose()
    {
        _notifier.Tick -= OnTick;
        lock (_gate)
        {
            _disposed = true;
            _livenessTimer?.Dispose();
            _livenessTimer = null;
            _livenessDue = null;
        }

        base.Dispose();
    }

    private void OnTick(DateTimeOffset now) => Tick();

    // ---- discovery ----------------------------------------------------------------------------------------------

    // Replaces who the members are. A member that stays keeps its state; a new one gets a detector. Returns the runtimes that are new.
    private List<MemberRuntime> ApplyDiscovery(HaDiscoveryResult result)
    {
        _zoneId = result.TimeZone;
        QueueZoneMeta(result.TimeZone);
        _watched = new HashSet<string>(result.WatchList, StringComparer.Ordinal);
        _zones = result.Zones.ToDictionary(z => z.Id, StringComparer.Ordinal);
        RebuildPlaces();

        var added = new List<MemberRuntime>();
        var members = new Dictionary<string, MemberRuntime>(StringComparer.Ordinal);
        foreach (var plan in result.Members)
        {
            if (!_members.TryGetValue(plan.Id, out var runtime))
            {
                runtime = new MemberRuntime(plan, plan.Kind == MemberKind.Live ? new TripDetector(_detection.Trips, _detection.Driving) { Zones = _drawn } : null);
                added.Add(runtime);
            }

            runtime.Plan = plan;
            members[plan.Id] = runtime;
        }

        _members = members;
        var vehicles = new Dictionary<string, VehicleRuntime>(StringComparer.Ordinal);
        foreach (var plan in result.Vehicles)
        {
            if (!_vehicles.TryGetValue(plan.Id, out var runtime))
            {
                runtime = new VehicleRuntime(plan);
            }

            runtime.Plan = plan;
            vehicles[plan.Id] = runtime;
        }

        _vehicles = vehicles;

        _trackerOwners.Clear();
        _sensorOwners.Clear();
        _vehicleOwners.Clear();
        foreach (var member in _members.Values)
        {
            Own(member.Plan.Life360TrackerId, member, FixSource.Life360);
            Own(member.Plan.CompanionTrackerId, member, FixSource.Companion);
            if (member.Plan.Sensors is { } sensors)
            {
                OwnSensor(sensors.BatteryLevel, member, SensorRole.BatteryLevel);
                OwnSensor(sensors.BatteryState, member, SensorRole.BatteryState);
                OwnSensor(sensors.Interactive, member, SensorRole.Screen);
                OwnSensor(sensors.DeviceLocked, member, SensorRole.Locked);
                OwnSensor(sensors.AndroidAuto, member, SensorRole.AndroidAuto);
            }
        }

        foreach (var vehicle in _vehicles.Values.Where(v => !v.Plan.IsPlaceholder))
        {
            if (vehicle.Plan.TrackerId is { } tracker)
            {
                _vehicleOwners[tracker] = vehicle;
            }

            foreach (var sensor in vehicle.Plan.SensorIds)
            {
                _vehicleOwners[sensor] = vehicle;
            }
        }

        foreach (var id in _entities.Keys.Where(id => !_watched.Contains(id)).ToList())
        {
            _entities.Remove(id);
        }

        return added;
    }

    private void Own(string? entityId, MemberRuntime member, FixSource source)
    {
        if (entityId is not null)
        {
            _trackerOwners[entityId] = (member, source);
        }
    }

    private void OwnSensor(string? entityId, MemberRuntime member, SensorRole role)
    {
        if (entityId is not null)
        {
            _sensorOwners[entityId] = (member, role);
        }
    }

    // D58, D66: HA's time zone is kept in meta.ha_time_zone. It is queued when it is new to this run, and again if the queue refused it.
    private void QueueZoneMeta(string zone)
    {
        if (string.Equals(_zoneMetaQueued, zone, StringComparison.Ordinal))
        {
            return;
        }

        _writes.Add(writer =>
        {
            if (writer.EnqueueMeta(MetaKeys.HaTimeZone, zone))
            {
                _zoneMetaQueued = zone;
            }
        });
    }

    // 02 section 1.6 rule F0, 5.4 and 7.5: a new member is hydrated from the database (RealmStateHydrator): the latest stored fix of each source is the pointer
    // a companion echo is compared with, the stored fix times of the last 24 hours give the heartbeat at once, and the stored fixes of the last 30 minutes are
    // replayed through the detector, so a drive in progress is picked up and a trip that closed without being written is written. The database is read outside
    // the lock; the replay is the detector's own and runs inside it. A database that cannot answer only costs the hydration, never the live stream.
    private async Task SeedAsync(IReadOnlyList<MemberRuntime> added, CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow();
        foreach (var runtime in added.Where(m => m.Detector is not null))
        {
            try
            {
                var data = await _hydrator.ReadAsync(runtime.Plan.Id, now, cancellationToken);
                lock (_gate)
                {
                    runtime.Life360 ??= data.Life360;
                    runtime.Companion ??= data.Companion;
                    runtime.FixTimes[FixSource.Life360] = [.. data.Life360Times];
                    runtime.FixTimes[FixSource.Companion] = [.. data.CompanionTimes];
                    PruneAndMeasureHeartbeat(runtime, now);
                    CollectClosed(runtime, runtime.Detector!.Replay(data.ReplayFixes, now));
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _hydrator.Abandon(runtime.Plan.Id, now);
                _logger.LogWarning("The stored fixes of member {MemberId} could not be read at start ({ErrorType}); starting without them", runtime.Plan.Id, ex.GetType().Name);
            }
        }
    }

    // ---- applying states ----------------------------------------------------------------------------------------

    // A snapshot has replace semantics: what the previous one held and this one lacks is gone. Every reconnect counts as a first snapshot (rule F0): the
    // pipeline cannot tell an HA restart, whose restored states are echoes, from a dropped socket.
    private void ApplySnapshot(HaSnapshot snapshot, DateTimeOffset now)
    {
        _entities.Clear();
        foreach (var entity in snapshot.Entities)
        {
            if (Keep(entity.EntityId))
            {
                _entities[entity.EntityId] = entity;
            }
        }

        ApplyAll(now, first: true);
        _dirty = true;
    }

    private void ApplyChange(HaStateChanged change, DateTimeOffset now)
    {
        if (change.New is not { } entity)
        {
            if (_entities.Remove(change.EntityId) && change.EntityId.StartsWith(ZonePrefix, StringComparison.Ordinal))
            {
                _zones.Remove(change.EntityId[ZonePrefix.Length..]);
                _zonesDirty = true;
                _dirty = true;
            }

            return;
        }

        if (!Keep(entity.EntityId))
        {
            return;
        }

        _entities[entity.EntityId] = entity;
        ApplyEntity(entity, first: false, now);
    }

    // Before the first discovery nothing is known to be unwanted, so everything is kept (the first discovery prunes it).
    private bool Keep(string entityId) => _watched is null || _watched.Contains(entityId);

    // Oldest first, so the trip detector sees the fixes of a snapshot in the order they happened.
    private void ApplyAll(DateTimeOffset now, bool first)
    {
        foreach (var entity in _entities.Values.OrderBy(e => e.LastUpdatedUtc ?? DateTimeOffset.MinValue).ThenBy(e => e.EntityId, StringComparer.Ordinal).ToList())
        {
            ApplyEntity(entity, first, now);
        }
    }

    private void ApplyEntity(HaEntitySnapshot entity, bool first, DateTimeOffset now)
    {
        if (entity.EntityId.StartsWith(ZonePrefix, StringComparison.Ordinal))
        {
            if (FixParser.ParseZone(entity) is { } zone && !(_zones.TryGetValue(zone.Id, out var known) && known == zone))
            {
                _zones[zone.Id] = zone;
                _zonesDirty = true;
                _dirty = true;
            }

            return;
        }

        if (_trackerOwners.TryGetValue(entity.EntityId, out var tracker))
        {
            ApplyTracker(tracker.Member, tracker.Source, entity, first, now);
        }
        else if (_sensorOwners.TryGetValue(entity.EntityId, out var sensor))
        {
            ApplySensor(sensor.Member, sensor.Role, entity, now);
        }
        else if (_vehicleOwners.TryGetValue(entity.EntityId, out var vehicle))
        {
            ApplyVehicle(vehicle, entity, first, now);
        }
    }

    private void ApplyTracker(MemberRuntime member, FixSource source, HaEntitySnapshot entity, bool first, DateTimeOffset now)
    {
        if (source == FixSource.Life360)
        {
            TrackLife360Availability(member, entity, now);
        }

        var previous = source == FixSource.Life360 ? member.Life360 : member.Companion;
        var fix = FixParser.ParseTracker(entity, source, now, previous, first);
        if (fix is null)
        {
            return;
        }

        // A fix older than the newest one of its source is stored but does not move the source's pointer (02 section 1.6).
        if (previous is null || fix.Ts >= previous.Ts)
        {
            if (source == FixSource.Life360)
            {
                member.Life360 = fix;
            }
            else
            {
                member.Companion = fix;
            }
        }

        if (!member.FixTimes.TryGetValue(source, out var times))
        {
            member.FixTimes[source] = times = [];
        }

        times.Add(fix.Ts);

        if (member.Detector is { } detector)
        {
            var step = detector.Process(fix);
            foreach (var decision in step.Decisions)
            {
                var memberId = member.Plan.Id;
                _writes.Add(writer => writer.EnqueueFix(memberId, decision.Fix, decision.InTrack, decision.Reason));
            }

            CollectClosed(member, step);
        }

        _dirty = true;
    }

    private static void TrackLife360Availability(MemberRuntime member, HaEntitySnapshot entity, DateTimeOffset now)
    {
        member.Life360Seen = true;
        var available = !(string.Equals(entity.State, "unavailable", StringComparison.OrdinalIgnoreCase) || string.Equals(entity.State, "unknown", StringComparison.OrdinalIgnoreCase));
        if (available)
        {
            member.Life360UnavailableSinceUtc = null;
            member.AtLocSinceUtc = InstantAttribute(entity, "at_loc_since");
        }
        else
        {
            member.Life360UnavailableSinceUtc ??= entity.LastChangedUtc is { } changed && changed <= now ? changed : now;
        }
    }

    private void ApplySensor(MemberRuntime member, SensorRole role, HaEntitySnapshot entity, DateTimeOffset now)
    {
        var asOf = entity.LastUpdatedUtc ?? entity.LastChangedUtc ?? now;
        switch (role)
        {
            case SensorRole.BatteryLevel:
                if (double.TryParse(entity.State, NumberStyles.Float, CultureInfo.InvariantCulture, out var level) && level is >= 0 and <= 100)
                {
                    member.SensorBatteryPct = (int)Math.Round(level, MidpointRounding.AwayFromZero);
                    member.SensorBatteryAsOfUtc = asOf;
                    _dirty = true;
                }

                break;
            case SensorRole.BatteryState:
                bool? charging = entity.State.Trim().ToLowerInvariant() switch
                {
                    "charging" or "full" => true,
                    "discharging" or "not_charging" or "not charging" => false,
                    _ => null,
                };
                if (charging is not null)
                {
                    member.SensorCharging = charging;
                    _dirty = true;
                }

                break;
            default:
                QueueSignal(member, role switch
                {
                    SensorRole.Screen => PhoneSignalKind.Screen,
                    SensorRole.Locked => PhoneSignalKind.Locked,
                    _ => PhoneSignalKind.AndroidAuto,
                }, entity, now);
                break;
        }
    }

    // A phone sensor transition is stored at the time HA changed it; the same transition arriving again (every snapshot) is not queued twice.
    private void QueueSignal(MemberRuntime member, PhoneSignalKind kind, HaEntitySnapshot entity, DateTimeOffset now)
    {
        bool? isOn = entity.State.Trim().ToLowerInvariant() switch
        {
            "on" => true,
            "off" => false,
            _ => null,
        };
        var ts = entity.LastChangedUtc ?? now;
        if (member.LastSignals.TryGetValue(kind, out var last) && last == (isOn, ts))
        {
            return;
        }

        member.LastSignals[kind] = (isOn, ts);
        var memberId = member.Plan.Id;
        var signal = new PhoneSignal(ts, kind, isOn);
        _writes.Add(writer => writer.EnqueueSignal(memberId, signal));
    }

    private void ApplyVehicle(VehicleRuntime vehicle, HaEntitySnapshot entity, bool first, DateTimeOffset now)
    {
        if (entity.EntityId == vehicle.Plan.TrackerId)
        {
            var fix = FixParser.ParseTracker(entity, FixSource.FordPass, now, vehicle.Fix, first);
            if (fix is not null && (vehicle.Fix is null || fix.Ts >= vehicle.Fix.Ts))
            {
                vehicle.Fix = fix;
            }
        }

        // Vehicles keep samples, not fixes (02 section 5.7): one row per (vehicle, sample clock), merged by the writer without erasing a value.
        var state = SnapshotBuilder.StateOf(vehicle, _entities);
        if (state.LastUpdateUtc is { } ts)
        {
            var prefix = vehicle.Plan.Prefix;
            var sample = new VehicleSample(
                VehicleId: vehicle.Plan.Id,
                Ts: ts,
                OdometerM: state.OdometerM,
                FuelPct: state.FuelPct,
                Ignition: RawText($"sensor.{prefix}_ignitionstatus")?.ToLowerInvariant(),
                Gear: RawText($"sensor.{prefix}_gearleverposition"),
                SpeedMps: state.SpeedMps,
                RemoteStartSeconds: state.RemoteStartSecondsLeft,
                Lat: vehicle.Fix is { } fixAt && (ts - fixAt.Ts).Duration() <= TimeSpan.FromMinutes(10) ? fixAt.Lat : null,
                Lon: vehicle.Fix is { } fixAtLon && (ts - fixAtLon.Ts).Duration() <= TimeSpan.FromMinutes(10) ? fixAtLon.Lon : null);
            if (sample != vehicle.LastSample)
            {
                vehicle.LastSample = sample;
                _writes.Add(writer => writer.EnqueueVehicleSample(sample));
            }
        }

        _dirty = true;
    }

    private string? RawText(string entityId) =>
        _entities.TryGetValue(entityId, out var entity)
        && !string.IsNullOrWhiteSpace(entity.State)
        && !string.Equals(entity.State, "unavailable", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(entity.State, "unknown", StringComparison.OrdinalIgnoreCase)
            ? entity.State.Trim()
            : null;

    private static DateTimeOffset? InstantAttribute(HaEntitySnapshot entity, string key)
    {
        if (!entity.Attributes.TryGetValue(key, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
        {
            return parsed.ToUniversalTime();
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var seconds) && double.IsFinite(seconds) && Math.Abs(seconds) < 253_402_300_799)
        {
            return DateTimeOffset.FromUnixTimeMilliseconds((long)Math.Round(seconds * 1000));
        }

        return null;
    }

    // ---- derived state and publishing ---------------------------------------------------------------------------

    private void CollectClosed(MemberRuntime member, TripStep step)
    {
        foreach (var trip in step.Closed)
        {
            _closed.Add((member.Plan.Id, trip));
        }
    }

    private List<(string MemberId, DetectedTrip Trip)> TakeClosed()
    {
        var closed = _closed.ToList();
        _closed.Clear();
        return closed;
    }

    private void PruneAndMeasureHeartbeat(MemberRuntime member, DateTimeOffset now)
    {
        foreach (var times in member.FixTimes.Values)
        {
            times.RemoveAll(t => now - t > FixHistory);
        }

        member.Heartbeat = FreshnessRules.Heartbeat(member.FixTimes.Values.Select(times => (IReadOnlyList<DateTimeOffset>)times), _options.UiStaleAfterMinutes);
    }

    // The zones as drawn (02 section 1.9): the options' hidden and display overrides applied, the oversized and hidden ones left out, duplicate names told apart.
    private void RebuildPlaces()
    {
        _zonesDirty = false;
        var zones = _zones.Values.OrderBy(z => z.Id, StringComparer.Ordinal).ToList();
        var optionOf = new Dictionary<string, PlaceOption>(StringComparer.Ordinal);
        foreach (var option in _options.Places)
        {
            if (HaDiscovery.MatchZone(option.Zone, zones) is { } matched)
            {
                optionOf.TryAdd(matched.Id, option);
            }
        }

        var maxRadiusM = _options.UiMaxZoneRadiusKm * 1000;
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var places = new List<PlaceDef>();
        foreach (var zone in zones)
        {
            optionOf.TryGetValue(zone.Id, out var option);
            if (option?.Hidden == true || zone.RadiusM > maxRadiusM)
            {
                continue;
            }

            var name = string.IsNullOrWhiteSpace(option?.DisplayName) ? zone.Name.Trim() : option.DisplayName.Trim();
            seen[name] = seen.GetValueOrDefault(name) + 1;
            var count = seen[name];
            places.Add(new PlaceDef(
                zone,
                count == 1 ? name : $"{name} ({count.ToString(CultureInfo.InvariantCulture)})",
                option?.Subtitle ?? string.Empty,
                zone.Id == "home" ? PlaceKind.Home : option?.Kind ?? PlaceKind.Other));
        }

        _places = places
            .OrderBy(p => p.Zone.Id == "home" ? 0 : 1)
            .ThenBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.Zone.Id, StringComparer.Ordinal)
            .ToList();
        _drawn = _places.Select(p => p.Zone).ToList();
        foreach (var member in _members.Values)
        {
            if (member.Detector is { } detector)
            {
                detector.Zones = _drawn;
            }
        }
    }

    // The derived part of a step: zone membership from the fused positions, the "since" run, then the snapshot. Says whether a snapshot was published and
    // whether a connection state changed with it.
    private Publication PublishLocked(DateTimeOffset now)
    {
        if (!_dirty && !_zonesDirty)
        {
            return Publication.None;
        }

        if (_zonesDirty)
        {
            RebuildPlaces();
        }

        foreach (var member in _members.Values)
        {
            ResolvePlace(member, now);
        }

        foreach (var vehicle in _vehicles.Values)
        {
            var membership = vehicle.Fix is { } fix ? PlaceResolver.Resolve(fix.Lat, fix.Lon, null, _drawn, vehicle.ZoneIds) : null;
            vehicle.ZoneIds = membership?.ZoneIds ?? [];
            vehicle.PlaceId = membership?.PlaceId;
        }

        var connection = CurrentConnectionLocked();
        var snapshot = SnapshotBuilder.Build(new BuildInput
        {
            Options = _options,
            Now = now,
            ZoneId = _zoneId,
            Members = [.. _members.Values],
            Vehicles = [.. _vehicles.Values],
            Places = _places,
            Connection = connection,
            Entities = _entities,
            StatsVersion = _state.Current.StatsVersion,
        });
        _state.Publish(snapshot);
        _dirty = false;
        _publishedHomeAssistant = connection.ToConnectionState(now);
        ArmLivenessTimer(connection, now);

        var states = snapshot.Connections.Select(c => c.State).ToArray();
        var changed = !states.SequenceEqual(_lastConnections);
        _lastConnections = states;
        return changed ? Publication.ConnectionChanged : Publication.Changed;
    }

    private void ResolvePlace(MemberRuntime member, DateTimeOffset now)
    {
        var plan = member.Plan;
        if (plan.Kind == MemberKind.Static)
        {
            var membership = plan.StaticLat is { } lat && plan.StaticLon is { } lon ? PlaceResolver.Resolve(lat, lon, null, _drawn, member.ZoneIds) : null;
            member.ZoneIds = membership?.ZoneIds ?? [];
            member.PlaceId = membership?.PlaceId;
            return;
        }

        var fused = SnapshotBuilder.FuseOf(member, _options, now);
        if (fused is null)
        {
            member.ZoneIds = [];
            member.PlaceId = null;
            member.RunKey = null;
            member.RunStartUtc = null;
            return;
        }

        var result = PlaceResolver.Resolve(fused.Lat, fused.Lon, fused.AccuracyM, _drawn, member.ZoneIds);
        member.ZoneIds = result.ZoneIds;
        member.PlaceId = result.PlaceId;

        // 02 section 4.6, simplified: a drive since its (back-dated) start; a visit since the first fix seen inside the zone; and for a run that was already
        // going when the add-on started, Life360's at_loc_since when it is earlier and not older than 7 days.
        var driving = member.Detector?.IsDriving(now) == true;
        var key = driving ? "drive" : result.PlaceId is { } place ? "place:" + place : "out";
        if (key != member.RunKey)
        {
            member.RunKey = key;
            var start = driving ? member.Detector!.OpenSinceUtc ?? fused.Ts : fused.Ts;
            if (!driving && member.RunCensored && member.AtLocSinceUtc is { } since && since < start && now - since <= CensoredRunMaxAge)
            {
                start = since;
            }

            member.RunStartUtc = start;
            member.RunCensored = false;
        }
        else if (driving && member.Detector!.OpenSinceUtc is { } openSince)
        {
            member.RunStartUtc = openSince;
        }
    }

    // ---- announcing and persisting ------------------------------------------------------------------------------

    // A step that changed nothing announces nothing; a connection change is never throttled.
    private void Announce(Publication publication)
    {
        switch (publication)
        {
            case Publication.ConnectionChanged:
                _notifier.NotifyNow();
                break;
            case Publication.Changed:
                _notifier.NotifyChanged();
                break;
            default:
                break;
        }
    }

    private void RaiseClosed(List<(string MemberId, DetectedTrip Trip)> closed)
    {
        if (closed.Count == 0 || TripClosed is not { } handler)
        {
            return;
        }

        foreach (var (memberId, trip) in closed)
        {
            foreach (var subscriber in handler.GetInvocationList().Cast<Action<string, DetectedTrip>>())
            {
                try
                {
                    subscriber(memberId, trip);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "A trip-closed subscriber failed for member {MemberId}", memberId);
                }
            }
        }
    }

    private void Persist(Action<IRealmWriter>[] writes)
    {
        foreach (var write in writes)
        {
            try
            {
                write(_writer);
            }
            catch (Exception ex)
            {
                var now = _time.GetUtcNow();
                if (now - _lastErrorLogged >= ErrorLogInterval)
                {
                    _lastErrorLogged = now;
                    _logger.LogError(ex, "A row could not be queued for the database");
                }
            }
        }
    }

    // ---- the Home Assistant connection entry -------------------------------------------------------------------

    // The websocket's own record when the pipeline has the connection. It moves with every state event and ping reply, which the pipeline is never told about
    // (StatusChanged fires on a change of state only), so a copy taken at the last change would read as silent for 90 s after it however healthy the socket is.
    // Without a connection (the pipeline alone in a test) it is the last status that was pushed.
    private HaConnectionStatus CurrentConnectionLocked()
    {
        if (_liveStatus is { } live)
        {
            _connection = live();
        }

        return _connection;
    }

    // An item shows that Home Assistant is talking, and the websocket has counted it before handing it over. When that changes what the HomeAssistant entry
    // reads (Connected again after a silence), the snapshot is published even if the item changed nothing that a member or a vehicle shows.
    private void MarkConnectionDirtyIfItReadsDifferentlyLocked(DateTimeOffset now)
    {
        if (_liveStatus is { } live && live().ToConnectionState(now) != _publishedHomeAssistant)
        {
            _dirty = true;
        }
    }

    // The HomeAssistant entry changes by itself at two instants that no event announces: when an open socket has been silent for 90 s (Reconnecting) and 15 s
    // after that, or 15 s into an outage (Unavailable). One check, set just after the next of them, republishes the snapshot then, without waiting for the 30 s
    // tick (the banner of 01 section 8.6 follows the outage closely). Activity moves the first of the two later, so a check that is due earlier than the next
    // change is kept: it looks again when it fires and arms the later one. The check is re-armed by every publication, which every tick is.
    private void ArmLivenessTimer(HaConnectionStatus status, DateTimeOffset now)
    {
        if (_disposed)
        {
            return;
        }

        if (NextConnectionChange(status, now) is not { } next)
        {
            _livenessTimer?.Dispose();
            _livenessTimer = null;
            _livenessDue = null;
            return;
        }

        if (_livenessTimer is not null && _livenessDue is { } armed && armed > now && armed <= next)
        {
            return;
        }

        var due = next - now;
        due = due > TimeSpan.Zero ? due : TimeSpan.Zero;
        _livenessDue = now + due;
        if (_livenessTimer is null)
        {
            _livenessTimer = _time.CreateTimer(_ => RecheckConnection(), null, due, Timeout.InfiniteTimeSpan);
        }
        else
        {
            _livenessTimer.Change(due, Timeout.InfiniteTimeSpan);
        }
    }

    // The instant just after which the HomeAssistant entry reads differently if nothing more is heard (the same limits as HaConnectionStatus.ToConnectionState,
    // 02 section 1.8, both inclusive); null when it stays as it is (Unavailable for good, or at once for a refused or missing token).
    private static DateTimeOffset? NextConnectionChange(HaConnectionStatus status, DateTimeOffset now)
    {
        DateTimeOffset outageStart;
        switch (status.State)
        {
            case HaConnectionState.NotConfigured:
            case HaConnectionState.AuthFailed:
                return null;
            case HaConnectionState.Connected:
                var silentAfter = (status.LastActivityUtc ?? now) + HaConnectionStatus.SilenceLimit;
                if (now <= silentAfter)
                {
                    return silentAfter + CheckMargin;
                }

                outageStart = silentAfter;
                break;
            default:
                if (status.OutageSinceUtc is not { } since)
                {
                    return null;
                }

                outageStart = since;
                break;
        }

        var unavailableAfter = outageStart + HaConnectionStatus.ReconnectingWindow;
        return now <= unavailableAfter ? unavailableAfter + CheckMargin : null;
    }

    private void RecheckConnection()
    {
        Publication publication;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _livenessDue = null;
            _dirty = true;
            publication = PublishLocked(_time.GetUtcNow());
        }

        Announce(publication);
    }
}

/// <summary>What one step of the pipeline did to the snapshot.</summary>
internal enum Publication
{
    /// <summary>Nothing changed, so nothing was published.</summary>
    None,

    /// <summary>A snapshot was published.</summary>
    Changed,

    /// <summary>A snapshot was published and one of its connection states differs from the previous one.</summary>
    ConnectionChanged,
}

/// <summary>Which sensor of a member's phone an entity is.</summary>
internal enum SensorRole
{
    BatteryLevel,
    BatteryState,
    Screen,
    Locked,
    AndroidAuto,
}
