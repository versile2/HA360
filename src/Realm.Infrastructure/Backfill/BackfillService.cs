using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Realm.Domain;
using Realm.Infrastructure.Diagnostics;
using Realm.Infrastructure.Ha;
using Realm.Infrastructure.Hosting;
using Realm.Infrastructure.Ingestion;
using Realm.Infrastructure.Options;
using Realm.Infrastructure.Stats;

namespace Realm.Infrastructure.Backfill;

/// <summary>
/// The gap-fill from Home Assistant's history (02 section 8, 03 section 2.4), once on every start. It never reads Life360: v1 has no Life360 history. For each
/// live member it fetches the history of the trackers in 12 hour chunks with attributes and of the phone's screen, lock and Android Auto sensors in 48 hour
/// chunks without, from <c>max(now - backfill_days, lastStoredTs - 5 min)</c> (the last stored fix being the one of the previous run, see
/// <see cref="HydrationMark"/>) up to the moment the member was hydrated, which is where the live detector takes over. <c>backfill_days = 0</c> turns it off.
/// </summary>
/// <remarks>
/// <para>
/// The rows go through the one parser (<see cref="FixParser"/>) and the one detector (<see cref="TripDetector"/>, through its replay entry point), exactly as
/// live data does: the stored fixes from half an hour before the filled range (and from before any stored trip that the range would cut, see
/// <see cref="RealmStateHydrator.ReplayStart"/>) plus the fetched ones are replayed, each fetched fix is queued with the decision the replay made for it, the
/// signals are queued, and every trip the replay closes goes to <see cref="StatsService.RecordTripAsync"/> (phone use, write, statistics version). Every
/// insert is <c>OR IGNORE</c> and a stored trip is never rewritten, so a second run, or an overlap with live data, changes nothing.
/// </para>
/// <para>
/// Not done: the battery sensors (the Life360 fixes carry the battery), <c>detected_activity</c> (no consumer), the replay of a tracker (0.3.1: a tracker's fixes are only
/// stored, <see cref="BackfillVehiclesAsync"/>) and the <c>job_state</c> cursor (the stored fixes are the cursor). An entity whose
/// history cannot be fetched is logged and skipped; the rest carry on.
/// </para>
/// </remarks>
public sealed class BackfillService : BackgroundService
{
    /// <summary>The chunk of a tracker request: they come with attributes, about 600 KB per 48 hours (02 section 8.2).</summary>
    public static readonly TimeSpan TrackerChunk = TimeSpan.FromHours(12);

    /// <summary>The chunk of a sensor request: state and change time only.</summary>
    public static readonly TimeSpan SensorChunk = TimeSpan.FromHours(48);

    private const int FlushEvery = 1000;
    private static readonly TimeSpan Overlap = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan HydrationPoll = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan TripReach = TimeSpan.FromHours(24);

    private readonly IHaGateway _gateway;
    private readonly IRealmQueries _queries;
    private readonly IRealmWriter _writer;
    private readonly DiscoveryState _discovery;
    private readonly RealmStateHydrator _hydrator;
    private readonly RealmState _state;
    private readonly StatsService _stats;
    private readonly RealmOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly ServiceCounters? _counters;
    private readonly ResilientLoop _loop;
    private readonly DetectionSettings _detection;
    private readonly HashSet<string> _vehiclesDone = new(StringComparer.Ordinal);
    private long _rows;
    private long _trips;

    /// <param name="counters">Where finished gap-fills and the rows they queued are counted for <c>diagnostics.json</c>; null counts nothing.</param>
    public BackfillService(
        IHaGateway gateway,
        IRealmQueries queries,
        IRealmWriter writer,
        DiscoveryState discovery,
        RealmStateHydrator hydrator,
        RealmState state,
        StatsService stats,
        RealmOptions options,
        TimeProvider time,
        ILogger<BackfillService> logger,
        ServiceCounters? counters = null)
    {
        _gateway = gateway;
        _queries = queries;
        _writer = writer;
        _discovery = discovery;
        _hydrator = hydrator;
        _state = state;
        _stats = stats;
        _options = options;
        _time = time;
        _logger = logger;
        _counters = counters;
        _detection = new DetectionSettings(options);
        _loop = new ResilientLoop(nameof(BackfillService), logger, time);
    }

    public ServiceHealth Health => _loop.Health;

    /// <summary>Fixes and signals queued for the database by the backfill since start (diagnostics).</summary>
    public long RowsBackfilled => Interlocked.Read(ref _rows);

    /// <summary>Trips the backfill's replay wrote as new rows since start (diagnostics).</summary>
    public long TripsRecovered => Interlocked.Read(ref _trips);

    /// <summary>
    /// One gap-fill of every hydrated live member, now. <c>backfill_days = 0</c> does nothing. The hosted service calls it once the first discovery has
    /// been applied and the members are hydrated; a test calls it directly.
    /// </summary>
    public async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        if (_options.BackfillDays <= 0)
        {
            return;
        }

        var now = _time.GetUtcNow();
        var before = RowsBackfilled;
        foreach (var member in _discovery.Current.Members.Where(m => m.Kind == MemberKind.Live))
        {
            if (_hydrator.MarkOf(member.Id) is { } mark)
            {
                await BackfillMemberAsync(member, mark, now, cancellationToken);
            }
        }

        await BackfillVehiclesAsync(now, cancellationToken);

        // A run that finished: a run that was switched off (above) or that threw is not one.
        _counters?.RecordBackfillRun(RowsBackfilled - before);
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) => _loop.RunAsync(RunAsync, stoppingToken);

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        if (_options.BackfillDays <= 0)
        {
            _logger.LogInformation("Backfill is off (backfill_days is 0)");
            return;
        }

        // The first discovery says who the members are and the pipeline hydrates each one when it applies it: the marks are what the backfill starts from.
        while (_discovery.Version == 0 || _discovery.Current.Members.Any(m => m.Kind == MemberKind.Live && _hydrator.MarkOf(m.Id) is null))
        {
            await Task.Delay(HydrationPoll, _time, cancellationToken);
        }

        var before = RowsBackfilled;
        await RunOnceAsync(cancellationToken);
        _logger.LogInformation("Backfill finished: {Rows} rows queued, {Trips} trips recovered", RowsBackfilled - before, TripsRecovered);
    }

    /// <summary>
    /// The gap-fill of the trackers that keep their history (0.3.1, D125), once per tracker and process (so a tracker whose switch is turned on later is filled at the next start): the positions Home Assistant's recorder has for the tracker's entity from
    /// <c>now - backfill_days</c> up to the earliest position stored for it (or up to now when none is stored), 12 hour chunks like a person's. They are stored as they are (no trips, no
    /// detector: a tracker's moves are derived when History is read). Every insert is <c>OR IGNORE</c>. A tracker whose history cannot be fetched is logged and skipped. <c>backfill_days = 0</c> does nothing.
    /// </summary>
    public async Task BackfillVehiclesAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (_options.BackfillDays <= 0)
        {
            return;
        }

        var horizon = now - TimeSpan.FromDays(_options.BackfillDays);
        foreach (var vehicle in _discovery.Current.Vehicles.Where(v => v.KeepHistory && v.TrackerId is not null))
        {
            if (!_vehiclesDone.Add(vehicle.Id))
            {
                continue;
            }

            var starts = await _queries.GetRecordingStartsAsync(cancellationToken);
            var to = starts.TryGetValue(vehicle.Id, out var first) && first + Overlap < now ? first + Overlap : now;
            if (to <= horizon)
            {
                continue;
            }

            var fixes = new List<RawFix>();
            await FetchVehicleAsync(vehicle, horizon, to, now, fixes, cancellationToken);
            var queued = 0;
            foreach (var fix in fixes)
            {
                _writer.EnqueueFix(vehicle.Id, fix, true, null);
                queued++;
                await FlushEveryAsync(queued, cancellationToken);
            }

            await _writer.FlushAsync(cancellationToken);
            Interlocked.Add(ref _rows, queued);
        }
    }

    private async Task FetchVehicleAsync(ResolvedVehicle vehicle, DateTimeOffset from, DateTimeOffset to, DateTimeOffset now, List<RawFix> fixes, CancellationToken cancellationToken)
    {
        for (var start = from; start < to; start += TrackerChunk)
        {
            var end = start + TrackerChunk < to ? start + TrackerChunk : to;
            try
            {
                ParseTracker(await _gateway.GetHistoryAsync(vehicle.TrackerId!, start, end, true, cancellationToken), start, from, vehicle.Source, now, to, fixes);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning("The history of a tracker ({TrackerId}) could not be fetched ({ErrorType}); the rest of it is skipped", vehicle.Id, ex.GetType().Name);
                return;
            }
        }
    }

    private async Task BackfillMemberAsync(ResolvedMember member, HydrationMark mark, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var horizon = now - TimeSpan.FromDays(_options.BackfillDays);
        var to = mark.HydratedAtUtc;
        var fixes = new List<RawFix>();
        var signals = new List<PhoneSignal>();
        var sensorFrom = to;

        var trackers = new[]
        {
            (Source: FixSource.Life360, EntityId: member.Life360TrackerId, Latest: mark.Life360LatestUtc),
            (Source: FixSource.Companion, EntityId: member.CompanionTrackerId, Latest: mark.CompanionLatestUtc),
        };
        foreach (var (source, entityId, latest) in trackers.Where(t => t.EntityId is not null))
        {
            var from = latest is { } stored && stored - Overlap > horizon ? stored - Overlap : horizon;
            sensorFrom = from < sensorFrom ? from : sensorFrom;
            await FetchAsync(
                member,
                entityId!,
                from,
                to,
                TrackerChunk,
                withAttributes: true,
                (rows, chunkStart) => ParseTracker(rows, chunkStart, from, source, now, to, fixes),
                cancellationToken);
        }

        if (member.Sensors is { } sensors)
        {
            // There is no stored-signal cursor: the sensors are fetched for the same stretch as the trackers (or the whole horizon without a tracker).
            sensorFrom = trackers.Any(t => t.EntityId is not null) ? sensorFrom : horizon;
            foreach (var (kind, entityId) in new[]
            {
                (PhoneSignalKind.Screen, sensors.Interactive),
                (PhoneSignalKind.Locked, sensors.DeviceLocked),
                (PhoneSignalKind.AndroidAuto, sensors.AndroidAuto),
            })
            {
                if (entityId is null)
                {
                    continue;
                }

                var last = (Has: false, IsOn: (bool?)null);
                await FetchAsync(
                    member,
                    entityId,
                    sensorFrom,
                    to,
                    SensorChunk,
                    withAttributes: false,
                    (rows, _) => last = ParseSensor(rows, kind, to, last, signals),
                    cancellationToken);
            }
        }

        var decisions = new Dictionary<(FixSource, DateTimeOffset), FixDecision>();
        IReadOnlyList<DetectedTrip> closed = [];
        if (fixes.Count > 0)
        {
            var filledFrom = fixes.Min(f => f.Ts);
            var recent = await _queries.GetTripsAsync(filledFrom - RealmStateHydrator.ReplayWindow - TripReach, to, member.Id, cancellationToken);
            var start = RealmStateHydrator.ReplayStart(filledFrom - RealmStateHydrator.ReplayWindow, recent);
            var stored = await _queries.GetFixesAsync(member.Id, start, to.AddMilliseconds(1), cancellationToken);

            // The stored copy of a fix wins over a fetched one: it is the one the live feed saw.
            var replayed = stored.Concat(fixes).GroupBy(f => (f.Source, f.Ts)).Select(g => g.First()).ToList();
            var zones = _state.Current.Places.Select(p => new RawPlace(p.Id, p.DisplayName, p.Lat, p.Lon, p.RadiusM, false)).ToList();
            var step = new TripDetector(_detection.Trips, _detection.Driving) { Zones = zones }.Replay(replayed, to);
            foreach (var decision in step.Decisions)
            {
                decisions[(decision.Fix.Source, decision.Fix.Ts)] = decision;
            }

            closed = step.Closed;
        }

        var queued = 0;
        foreach (var fix in fixes)
        {
            if (decisions.TryGetValue((fix.Source, fix.Ts), out var decision))
            {
                _writer.EnqueueFix(member.Id, fix, decision.InTrack, decision.Reason);
                queued++;
                await FlushEveryAsync(queued, cancellationToken);
            }
        }

        foreach (var signal in signals)
        {
            _writer.EnqueueSignal(member.Id, signal);
            queued++;
            await FlushEveryAsync(queued, cancellationToken);
        }

        await _writer.FlushAsync(cancellationToken);
        Interlocked.Add(ref _rows, queued);

        // The rows the trips are built from, and the signals their phone use is counted from, are committed now.
        foreach (var trip in closed)
        {
            if (await _stats.RecordTripAsync(member.Id, trip, cancellationToken))
            {
                Interlocked.Increment(ref _trips);
            }
        }
    }

    // Fetches [from, to) in chunks, oldest first, and hands each chunk's rows to the parser. A failure ends this entity only.
    private async Task FetchAsync(
        ResolvedMember member,
        string entityId,
        DateTimeOffset from,
        DateTimeOffset to,
        TimeSpan chunk,
        bool withAttributes,
        Action<IReadOnlyList<HaEntitySnapshot>, DateTimeOffset> parse,
        CancellationToken cancellationToken)
    {
        for (var start = from; start < to; start += chunk)
        {
            var end = start + chunk < to ? start + chunk : to;
            try
            {
                parse(await _gateway.GetHistoryAsync(entityId, start, end, withAttributes, cancellationToken), start);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning("The history of an entity of member {MemberId} could not be fetched ({ErrorType}); the rest of it is skipped", member.Id, ex.GetType().Name);
                return;
            }
        }
    }

    // 02 section 8.2: a Life360 row with the previous row's last_seen is attribute-only, and a companion row with the same coordinates is too (the parser's
    // repeat rules). The first row of a request is the state at its start, stamped with the start: for a companion that is not a fix of its own, and for Life360
    // it carries the last_seen from before the start, which is not part of the range filled and must not move members.recording_start back.
    private static void ParseTracker(IReadOnlyList<HaEntitySnapshot> rows, DateTimeOffset chunkStart, DateTimeOffset from, FixSource source, DateTimeOffset now, DateTimeOffset to, List<RawFix> fixes)
    {
        var previous = fixes.LastOrDefault(f => f.Source == source);
        foreach (var row in rows)
        {
            if (source == FixSource.Companion && row.LastUpdatedUtc == chunkStart)
            {
                continue;
            }

            if (FixParser.ParseTracker(row, source, now, previous) is { } fix && fix.Ts >= from && fix.Ts <= to)
            {
                fixes.Add(fix);
                if (previous is null || fix.Ts >= previous.Ts)
                {
                    previous = fix;
                }
            }
        }
    }

    // A signal is stored at the time Home Assistant changed the sensor; a row that repeats the previous state is not a transition (the first row of every
    // request is the state at its start).
    private static (bool Has, bool? IsOn) ParseSensor(IReadOnlyList<HaEntitySnapshot> rows, PhoneSignalKind kind, DateTimeOffset to, (bool Has, bool? IsOn) last, List<PhoneSignal> signals)
    {
        foreach (var row in rows)
        {
            if (row.LastChangedUtc is not { } ts || ts > to)
            {
                continue;
            }

            bool? isOn = row.State.Trim().ToLowerInvariant() switch
            {
                "on" => true,
                "off" => false,
                _ => null,
            };
            if (last.Has && last.IsOn == isOn)
            {
                continue;
            }

            signals.Add(new PhoneSignal(ts, kind, isOn));
            last = (true, isOn);
        }

        return last;
    }

    // The writer's queue holds 10 000 rows and refuses more, so a long backfill commits as it goes.
    private async Task FlushEveryAsync(int queued, CancellationToken cancellationToken)
    {
        if (queued % FlushEvery == 0)
        {
            await _writer.FlushAsync(cancellationToken);
        }
    }
}
