using System.Collections.Concurrent;
using System.Reflection;
using Realm.Domain;
using Realm.Infrastructure.Data;
using Realm.Infrastructure.Ha;
using Realm.Infrastructure.Ingestion;
using Realm.Infrastructure.Options;

namespace Realm.Infrastructure.Diagnostics;

/// <summary>
/// The Live <see cref="IDiagnostics"/> (03 section 2.11, 02 section 10.4): it copies what the services already keep into a <see cref="DiagnosticsSnapshot"/>
/// with <c>mode</c> "live", and raises the eight fixed warning codes of <see cref="WarningCodes"/> from it. It reads the current <see cref="RealmSnapshot"/> (the
/// zone, the counts, the four connection entries, each member's freshness and each vehicle's last refresh), <see cref="ServiceCounters"/> and the websocket's
/// status, and it has no other input, so there is no coordinate, address, name, battery value, entity id, user id, token or log text for it to copy. Members
/// appear by their option slug only. Reading is cheap and never blocks a service; the web host fills in the circuit counts when it serves the file.
/// </summary>
public sealed class DiagnosticsSnapshotBuilder : IDiagnostics
{
    /// <summary>"Not connected for over 60 seconds" of <see cref="WarningCodes.HaWebsocketDownOver60s"/> (03 section 9.3).</summary>
    public static readonly TimeSpan WebsocketDownLimit = TimeSpan.FromSeconds(60);

    private const string UtcZone = "UTC";

    private readonly RealmState _state;
    private readonly RealmOptions _options;
    private readonly ServiceCounters _counters;
    private readonly TimeProvider _time;
    private readonly string _databasePath;
    private readonly Func<HaConnectionStatus?> _websocket;
    private readonly Func<IReadOnlyDictionary<string, int>> _staleThresholds;
    private readonly string _version;
    private readonly ConcurrentDictionary<string, bool> _zones = new(StringComparer.Ordinal);

    /// <param name="state">Where the current <see cref="RealmSnapshot"/> comes from.</param>
    /// <param name="options">The add-on options: the stale threshold of members that have no observed heartbeat and the vehicle threshold.</param>
    /// <param name="counters">The counters the services keep.</param>
    /// <param name="time">The clock of the uptime, the rates and the ages.</param>
    /// <param name="databasePath">The SQLite file whose size is reported (never its path); a file that does not exist is 0 bytes.</param>
    /// <param name="websocket">
    /// The live status of the Home Assistant websocket, or null when it does not run (the options were refused, or there is no token): then the state is
    /// <c>notConfigured</c>.
    /// </param>
    /// <param name="staleThresholds">
    /// The effective stale threshold in minutes of each live member by id (02 section 4.7: the heartbeat the pipeline observed is part of it); a member that is
    /// not in it, a static one, reads <c>ui_stale_after_minutes</c>.
    /// </param>
    /// <param name="version">The add-on version; read from this assembly when null.</param>
    public DiagnosticsSnapshotBuilder(
        RealmState state,
        RealmOptions options,
        ServiceCounters counters,
        TimeProvider time,
        string databasePath,
        Func<HaConnectionStatus?> websocket,
        Func<IReadOnlyDictionary<string, int>> staleThresholds,
        string? version = null)
    {
        _state = state;
        _options = options;
        _counters = counters;
        _time = time;
        _databasePath = databasePath;
        _websocket = websocket;
        _staleThresholds = staleThresholds;
        _version = version ?? AddOnVersion();
    }

    /// <inheritdoc />
    public DiagnosticsSnapshot GetSnapshot()
    {
        var now = _time.GetUtcNow();
        var snapshot = _state.Current;
        var status = _websocket();
        var zoneDataOk = _counters.ZoneDataOk && ZoneResolves(snapshot.Zone);
        var dropped = _counters.IngestSkipped + _counters.WriterDropped;
        var writerDepth = _counters.WriterQueueDepth;
        var thresholds = _staleThresholds();

        var members = snapshot.Members
            .Select(member => new DiagnosticsSnapshot.MemberEntry(
                member.Id,
                member.Freshness,
                thresholds.TryGetValue(member.Id, out var minutes) ? minutes : _options.UiStaleAfterMinutes))
            .ToArray();

        return new DiagnosticsSnapshot(
            Schema: DiagnosticsSnapshot.CurrentSchema,
            Version: _version,
            Mode: "live",
            UptimeSeconds: (long)Math.Max(0, (now - _counters.StartedUtc).TotalSeconds),
            Zone: snapshot.Zone,
            ZoneDataOk: zoneDataOk,
            Circuits: new DiagnosticsSnapshot.CircuitCounts(0, 0),
            Connections: snapshot.Connections,
            Counts: new DiagnosticsSnapshot.EntityCounts(snapshot.Members.Count, snapshot.Vehicles.Count, snapshot.Places.Count),
            Ha: new DiagnosticsSnapshot.HaCounters(
                WebsocketWord(status),
                ToInt(_counters.WsReconnects),
                _counters.LastWsMessageUtc,
                _counters.WatchedEntities,
                _counters.WsMessagesPerMinute(now)),
            Ingestion: new DiagnosticsSnapshot.IngestionCounters(_counters.IngestEventsPerMinute(now), _counters.IngestQueueDepth, dropped),
            Db: new DiagnosticsSnapshot.DbCounters(
                _counters.SchemaVersion,
                _counters.UncleanShutdownAtStart,
                DatabaseBytes(),
                writerDepth,
                _counters.LastDbCommitUtc),
            Members: members,
            Warnings: WarningsOf(snapshot, status, now, dropped, writerDepth, zoneDataOk));
    }

    // The eight codes of 03 section 2.11, in the order it lists them. Each one has exactly the condition the section gives.
    private List<string> WarningsOf(RealmSnapshot snapshot, HaConnectionStatus? status, DateTimeOffset now, long dropped, int writerDepth, bool zoneDataOk)
    {
        var warnings = new List<string>(8);

        // The HomeAssistant entry the Settings chips and the banner show.
        var homeAssistant = snapshot.Connections.FirstOrDefault(connection => connection.Name == ConnectionNames.HomeAssistant);
        if (homeAssistant?.State == ConnectionState.Unavailable)
        {
            warnings.Add(WarningCodes.HaUnavailable);
        }

        if (status?.State == HaConnectionState.AuthFailed)
        {
            warnings.Add(WarningCodes.HaAuthFailed);
        }

        if (status is not null && DownSince(status, now) is { } downSince && now - downSince > WebsocketDownLimit)
        {
            warnings.Add(WarningCodes.HaWebsocketDownOver60s);
        }

        if (dropped > 0)
        {
            warnings.Add(WarningCodes.IngestDrops);
        }

        // Strictly above 80 % of the bound (02 section 7.3: 10 000 rows).
        if (writerDepth * 10L > DbWriter.QueueCapacity * 8L)
        {
            warnings.Add(WarningCodes.WriterQueueOver80Pct);
        }

        if (_counters.UncleanShutdownAtStart)
        {
            warnings.Add(WarningCodes.UncleanShutdown);
        }

        if (!zoneDataOk)
        {
            warnings.Add(WarningCodes.ZoneDataMissing);
        }

        if (_counters.PayloadSchemaMismatch)
        {
            warnings.Add(WarningCodes.PayloadSchemaMismatch);
        }

        return warnings;
    }

    // Since when the websocket has not been connected. An open socket that has heard nothing for longer than the 90 s of 02 section 1.8 has been down since
    // that limit ran out; a socket that is not open has been down since the outage began; one that never ran (no token) has no start to count from.
    private static DateTimeOffset? DownSince(HaConnectionStatus status, DateTimeOffset now)
    {
        if (status.State != HaConnectionState.Connected)
        {
            return status.OutageSinceUtc;
        }

        return status.LastActivityUtc is { } heard && now - heard > HaConnectionStatus.SilenceLimit ? heard + HaConnectionStatus.SilenceLimit : null;
    }

    // A short word for the operator, never an address (03 section 2.11); camel case like every other word of the file.
    private static string WebsocketWord(HaConnectionStatus? status) => status?.State switch
    {
        HaConnectionState.Connecting => "connecting",
        HaConnectionState.Authenticating => "authenticating",
        HaConnectionState.Connected => "connected",
        HaConnectionState.Reconnecting => "reconnecting",
        HaConnectionState.AuthFailed => "authFailed",
        _ => "notConfigured",
    };

    // The zone Home Assistant reported must exist in this machine's zone data, or every local time is UTC (03 section 5.1). The answer is remembered per id: an
    // unknown id is an exception, and the file is served on request.
    private bool ZoneResolves(string zone) =>
        string.Equals(zone, UtcZone, StringComparison.Ordinal) || _zones.GetOrAdd(zone, static id => ZoneDataSelfCheck.Resolves(id));

    private long DatabaseBytes()
    {
        try
        {
            return new FileInfo(_databasePath).Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private static int ToInt(long value) => value > int.MaxValue ? int.MaxValue : (int)value;

    // The informational version is "<Version>+<source revision>": the add-on version is the part before the plus.
    private static string AddOnVersion()
    {
        var informational = typeof(DiagnosticsSnapshotBuilder).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrEmpty(informational))
        {
            return "0.0.0-dev";
        }

        var plus = informational.IndexOf('+', StringComparison.Ordinal);
        return plus < 0 ? informational : informational[..plus];
    }
}
