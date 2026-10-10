using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Realm.Domain;
using Realm.Infrastructure.Backfill;
using Realm.Infrastructure.Data;
using Realm.Infrastructure.Diagnostics;
using Realm.Infrastructure.Ha;
using Realm.Infrastructure.Ingestion;
using Realm.Infrastructure.Options;
using Realm.Infrastructure.Retention;
using Realm.Infrastructure.Roster;
using Realm.Infrastructure.Stats;
using Realm.TestKit;
using Xunit;

namespace Realm.Ingestion.Tests;

/// <summary>
/// The services of the trip and statistics tests over a real database: a bootstrapped temporary SQLite file with the production <see cref="DbWriter"/> and
/// <see cref="SqliteRealmQueries"/>, the ingestion pipeline (which hydrates a member from the file when discovery adds it), the hydrator, the statistics
/// service and a fake Home Assistant, on one manual clock. Nothing waits for a wall-clock timer: a flush is asked for and awaited, the clock moves when a test
/// says so. The time is a Friday, so the current week (Monday start) already has its Tuesday and Wednesday behind it.
/// </summary>
internal sealed class StoreRig : IAsyncDisposable
{
    public static readonly DateTimeOffset Start = new(2026, 10, 2, 15, 0, 0, TimeSpan.Zero);

    private readonly string _folder;
    private readonly ServiceProvider _provider;
    private bool _stopped;

    private StoreRig(RealmOptions options, DateTimeOffset now)
    {
        _folder = Path.Combine(Path.GetTempPath(), "realm-ingestion-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
        FilePath = Path.Combine(_folder, "realm.db");
        Options = options;
        Time = new ManualTimeProvider(now);
        Counters = new ServiceCounters(Time);
        new SchemaBootstrap(FilePath, new RecordingLogger<SchemaBootstrap>(), Time, Counters).Run();
        _provider = new ServiceCollection()
            .AddPooledDbContextFactory<RealmDb>(builder => RealmDb.Configure(builder, FilePath))
            .BuildServiceProvider();
        var factory = _provider.GetRequiredService<IDbContextFactory<RealmDb>>();
        Writer = new DbWriter(factory, Time, new RecordingLogger<DbWriter>(), Counters);
        Queries = new SqliteRealmQueries(factory);
        Roster = new RosterService(Queries, Writer, Time, new RecordingLogger<RosterService>());
        Gateway = new FakeHaGateway { History = History.Answer };
        (State, Notifier, Discovery, Hydrator, Stats, Pipeline, PipelineLog) = NewProcess();
    }

    public RealmOptions Options { get; }

    public ManualTimeProvider Time { get; }

    /// <summary>The counters every service of the rig fills in, as the Live host's single instance does for <c>diagnostics.json</c>.</summary>
    public ServiceCounters Counters { get; }

    public string FilePath { get; }

    public DbWriter Writer { get; }

    public SqliteRealmQueries Queries { get; }

    /// <summary>The Live roster over the rig's database (D113).</summary>
    public RosterService Roster { get; }

    /// <summary>The columns that make a stored trip comparable from one run to another (everything but its generated id).</summary>
    public const string TripColumns =
        "member_id, start_ts, end_ts, duration_s, start_lat, start_lon, end_lat, end_lon, distance_m, distance_gps_m, top_speed_mps, top_speed_ts, speeding_count, "
        + "phone_count, quality, has_gap, ended_by, source_mask, algo_version, derive_hash";

    /// <summary>The columns that make a stored fix comparable from one run to another, with the detector's decision about it.</summary>
    public const string FixColumns = "member_id, ts, source, lat, lon, acc_m, speed_mps, battery_pct, driving, track, reason";

    // The in-memory side of the add-on: it is lost at a restart, the database is not (see Restart).
    public RealmState State { get; private set; }

    public ChangeNotifier Notifier { get; private set; }

    public DiscoveryState Discovery { get; private set; }

    public RealmStateHydrator Hydrator { get; private set; }

    public StatsService Stats { get; private set; }

    public IngestionPipeline Pipeline { get; private set; }

    public RecordingLogger<IngestionPipeline> PipelineLog { get; private set; }

    /// <summary>The fake Home Assistant; its history answers from <see cref="History"/>.</summary>
    public FakeHaGateway Gateway { get; }

    public FakeHistory History { get; } = new();

    /// <summary>A rig with the writer running.</summary>
    public static async Task<StoreRig> StartAsync(RealmOptions? options = null, DateTimeOffset? now = null)
    {
        var rig = new StoreRig(options ?? OptionsBinding.Defaults, now ?? Start);
        await rig.Writer.StartAsync(CancellationToken.None);
        return rig;
    }

    /// <summary>
    /// The add-on restarts: the state, the discovery result, the hydrator, the statistics service and the pipeline (with every detector in it) are new, and
    /// whatever was subscribed to the old pipeline is left behind. The database, the writer and the clock carry on, as they would if the file were reopened.
    /// </summary>
    public void Restart()
    {
        Pipeline.Dispose();
        Notifier.Dispose();
        (State, Notifier, Discovery, Hydrator, Stats, Pipeline, PipelineLog) = NewProcess();
    }

    /// <summary>The trip recorder over the current pipeline and statistics service.</summary>
    public TripRecorder NewRecorder(RecordingLogger<TripRecorder>? log = null) => new(Pipeline, Stats, Time, log ?? new RecordingLogger<TripRecorder>());

    /// <summary>The backfill service over this rig, on its own recording logger.</summary>
    public BackfillService NewBackfill(RecordingLogger<BackfillService>? log = null) =>
        new(Gateway, Queries, Writer, Discovery, Hydrator, State, Stats, Options, Time, log ?? new RecordingLogger<BackfillService>(), Counters);

    /// <summary>
    /// The retention job over this rig. A test that moves the clock by days gives it a clock of its own, so the writer's two second timer does not fire a
    /// hundred thousand times on the way.
    /// </summary>
    public RetentionService NewRetention(TimeProvider? time = null, RecordingLogger<RetentionService>? log = null) =>
        new(Writer, State, Options, time ?? Time, log ?? new RecordingLogger<RetentionService>(), Counters);

    /// <summary>Discovery reports these members: the discovery state is published and the pipeline applies it (and hydrates each new member from the file).</summary>
    public Task DiscoverAsync(params ResolvedMember[] members) => DiscoverInZoneAsync("UTC", members);

    public async Task DiscoverInZoneAsync(string zone, params ResolvedMember[] members)
    {
        var plan = Plans.Discovery(zone: zone, members: members);
        Discovery.Publish(plan);
        await Pipeline.ProcessAsync(new DiscoveryUpdated(plan), CancellationToken.None);
    }

    /// <summary>As <see cref="DiscoverAsync"/>, with the trackers under Trackers (vehicles) too.</summary>
    public async Task DiscoverWithVehiclesAsync(IReadOnlyList<ResolvedVehicle> vehicles, params ResolvedMember[] members)
    {
        var plan = Plans.Discovery(vehicles: vehicles, members: members);
        Discovery.Publish(plan);
        await Pipeline.ProcessAsync(new DiscoveryUpdated(plan), CancellationToken.None);
    }

    /// <summary>Each entity goes into the pipeline as a state change, the way the websocket delivers it.</summary>
    public async Task FeedAsync(params HaEntitySnapshot[] entities)
    {
        foreach (var entity in entities)
        {
            await Pipeline.ProcessAsync(new FeedItem(new HaStateChanged(entity.EntityId, entity, null, Time.GetUtcNow())), CancellationToken.None);
        }
    }

    /// <summary>Stores fixes the way the live feed did in an earlier run (they count as stored history, not as live data) and commits them.</summary>
    public async Task StoreFixesAsync(string memberId, IEnumerable<RawFix> fixes)
    {
        foreach (var fix in fixes)
        {
            Assert.True(Writer.EnqueueFix(memberId, fix));
        }

        await Writer.FlushAsync();
    }

    /// <summary>Stores phone signals of an earlier run and commits them.</summary>
    public async Task StoreSignalsAsync(string memberId, IEnumerable<PhoneSignal> signals)
    {
        foreach (var signal in signals)
        {
            Assert.True(Writer.EnqueueSignal(memberId, signal));
        }

        await Writer.FlushAsync();
    }

    public long Count(string table) => Long("SELECT count(*) FROM " + table);

    public long Long(string sql)
    {
        using var connection = RealmDb.OpenConnection(FilePath, pooling: false);
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    public double Double(string sql)
    {
        using var connection = RealmDb.OpenConnection(FilePath, pooling: false);
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToDouble(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    public string? Text(string sql)
    {
        using var connection = RealmDb.OpenConnection(FilePath, pooling: false);
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        var value = command.ExecuteScalar();
        return value is null or DBNull ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
    }

    /// <summary>The rows of a query as text, each column separated by <c>|</c>: a comparable picture of a table (leave out the generated ids).</summary>
    public IReadOnlyList<string> Rows(string sql)
    {
        using var connection = RealmDb.OpenConnection(FilePath, pooling: false);
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        var rows = new List<string>();
        while (reader.Read())
        {
            rows.Add(string.Join('|', Enumerable.Range(0, reader.FieldCount).Select(i => Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture))));
        }

        return rows;
    }

    public void Exec(string sql)
    {
        using var connection = RealmDb.OpenConnection(FilePath, pooling: false);
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// A connection of its own, outside every pool, that stays open (and idle: it has run only its PRAGMAs, so it holds no read transaction and cannot hold up
    /// <c>wal_checkpoint(TRUNCATE)</c>) until the caller disposes it. SQLite deletes the <c>-wal</c> file when the last connection to the database closes, and the
    /// pooled connections of this rig close whenever any other rig in the process clears the pools; while this one is open the file stays where it is, so a test
    /// can measure the log with <see cref="LogBytes"/> whenever it likes. The caller disposes it before the rig.
    /// </summary>
    public SqliteConnection HoldLogOpen() => RealmDb.OpenConnection(FilePath, pooling: false);

    /// <summary>The size of the write-ahead log in bytes. A log that has no file is an empty log (SQLite removes it when the database is closed), so that is 0, not an error.</summary>
    public long LogBytes()
    {
        try
        {
            return new FileInfo(FilePath + "-wal").Length;
        }
        catch (FileNotFoundException)
        {
            return 0;
        }
    }

    private (RealmState, ChangeNotifier, DiscoveryState, RealmStateHydrator, StatsService, IngestionPipeline, RecordingLogger<IngestionPipeline>) NewProcess()
    {
        var state = RealmState.CreateInitial(Options, Time);
        var notifier = new ChangeNotifier(Time, new RecordingLogger<ChangeNotifier>());
        var discovery = new DiscoveryState();
        var hydrator = new RealmStateHydrator(Queries);
        var stats = new StatsService(state, discovery, notifier, Queries, Writer, Options, Time);
        var log = new RecordingLogger<IngestionPipeline>();
        var pipeline = new IngestionPipeline(Options, state, notifier, Writer, Queries, Time, log, hydrator, null, Counters);
        return (state, notifier, discovery, hydrator, stats, pipeline, log);
    }

    public async ValueTask DisposeAsync()
    {
        Pipeline.Dispose();
        Notifier.Dispose();
        if (!_stopped)
        {
            _stopped = true;
            await Writer.StopAsync(CancellationToken.None);
        }

        Writer.Dispose();
        await _provider.DisposeAsync();
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // Best effort: a leftover temp folder must not fail the test that already finished.
        }
    }
}
