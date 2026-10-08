using Realm.Domain;
using Realm.Infrastructure.Ha;
using Realm.Infrastructure.Ingestion;
using Realm.Infrastructure.Options;
using Realm.TestKit;
using Xunit;

namespace Realm.Ingestion.Tests;

// 02 section 1.8 and 03 section 2.5 as the Live host runs them: the real HaWebSocketConnection against FakeHaServer feeds the real IngestionPipeline, and one
// ManualTimeProvider keeps all time (the connection's pings, the pipeline's data tick and its own checks). The connection tells the pipeline when its STATE
// changes and never when a state event or a ping reply arrives, yet the HomeAssistant entry of the snapshot is Connected only while one of those arrived
// within 90 s (review R3-01): a pipeline that kept the status of the last change read Reconnecting 90 s after every connect, Unavailable 15 s later, and
// showed the "royal messengers" banner on a healthy socket. Entity ids are fictional.
public class LiveConnectionStatusTests
{
    private const string Fuel = "sensor.wagon_fuel";

    // Shorter than the 30 s of the data tick, so that a wait for this timer is never satisfied by the tick's own.
    private static readonly TimeSpan Ping = TimeSpan.FromSeconds(20);

    // Longer than any test: no ping goes out, so only the 90 s rule of 02 section 1.8 is under test, not the reconnect that a missing pong forces.
    private static readonly TimeSpan NoPing = TimeSpan.FromHours(1);

    [Fact]
    public async Task AHealthyConnection_StaysConnected_ForFiveMinutes_AndItsLastSyncFollowsThePongs()
    {
        await using var live = await Live.StartAsync(options => options with { PingInterval = Ping });
        var connectedAt = live.Time.GetUtcNow();
        Assert.Equal(ConnectionState.Connected, live.HomeAssistant.State);
        Assert.Equal(connectedAt, live.HomeAssistant.LastSyncUtc);

        for (var round = 0; round < 15; round++)
        {
            // 15 rounds of 20 s: a ping goes out each time and the fake answers it, and nothing else ever arrives.
            await live.Rig.TimerAsync(Ping);
            live.Time.Advance(Ping);
            await live.Rig.Guard(live.Session.WaitForMessageAsync("ping", round), "a ping");
            await live.Rig.TimerAsync(Ping); // the pong arrived and the next ping is scheduled

            // The snapshot that the 30 s data tick published inside this round was built from the connection's status of that moment.
            Assert.Equal(ConnectionState.Connected, live.HomeAssistant.State);

            live.Pipeline.Tick();
            Assert.Equal(ConnectionState.Connected, live.HomeAssistant.State);
            Assert.Equal(live.Time.GetUtcNow(), live.HomeAssistant.LastSyncUtc); // the pong that has just arrived, not the instant of the connect
        }

        Assert.Equal(connectedAt + TimeSpan.FromMinutes(5), live.Time.GetUtcNow());
        Assert.Equal(1, live.Rig.Server.AttemptCount);
        Assert.Equal(0, live.Rig.Connection.ReconnectCount);
    }

    [Fact]
    public async Task ASocketThatHearsNothing_ReadsReconnectingAfter90Seconds_AndUnavailableAfter15More()
    {
        await using var live = await Live.StartAsync(options => options with { PingInterval = NoPing });
        var connectedAt = live.Time.GetUtcNow();

        live.Time.Advance(TimeSpan.FromSeconds(90)); // three data ticks, the last one at exactly the limit
        Assert.Equal(ConnectionState.Connected, live.HomeAssistant.State);
        Assert.Equal(connectedAt, live.HomeAssistant.LastSyncUtc); // nothing has arrived since the connect

        live.Time.Advance(TimeSpan.FromSeconds(1)); // 91 s: the pipeline's own check, not the 30 s tick (the next tick is at 120 s)
        Assert.Equal(ConnectionState.Reconnecting, live.HomeAssistant.State);
        Assert.Equal(connectedAt, live.HomeAssistant.LastSyncUtc);

        live.Time.Advance(TimeSpan.FromSeconds(14)); // 105 s: 15 s into the outage that began at 90 s
        Assert.Equal(ConnectionState.Reconnecting, live.HomeAssistant.State);

        live.Time.Advance(TimeSpan.FromSeconds(1)); // 106 s
        Assert.Equal(ConnectionState.Unavailable, live.HomeAssistant.State);
        Assert.Equal(connectedAt, live.HomeAssistant.LastSyncUtc);
        Assert.Equal(HaConnectionState.Connected, live.Rig.Connection.Status.State); // the socket is open: it is the silence that the entry reports
    }

    [Fact]
    public async Task WhenTrafficResumes_TheEntryReadsConnectedAtOnce_AndTheSilenceClockStartsOver()
    {
        await using var live = await Live.StartAsync(options => options with { PingInterval = NoPing });
        live.Time.Advance(TimeSpan.FromSeconds(106));
        Assert.Equal(ConnectionState.Unavailable, live.HomeAssistant.State);

        // A state event of an entity that no member shows: it changes nothing but the proof that Home Assistant is talking.
        await live.Session.SendEventAsync("""{"c":{"sensor.wagon_fuel":{"+":{"s":"61"}}}}""");
        await live.Rig.WaitForItemsAsync(2); // the pipeline has handled it when this returns

        Assert.Equal(ConnectionState.Connected, live.HomeAssistant.State);
        Assert.Equal(live.Time.GetUtcNow(), live.HomeAssistant.LastSyncUtc);
        var heardAt = live.Time.GetUtcNow();

        live.Time.Advance(TimeSpan.FromSeconds(90)); // data ticks at 30 s intervals all find it inside the limit
        Assert.Equal(ConnectionState.Connected, live.HomeAssistant.State);
        Assert.Equal(heardAt, live.HomeAssistant.LastSyncUtc);

        live.Time.Advance(TimeSpan.FromSeconds(1)); // 91 s after the event
        Assert.Equal(ConnectionState.Reconnecting, live.HomeAssistant.State);
    }

    // The real connection and the real pipeline, wired as the Live registration wires them: items and status changes go to the pipeline (the rig hands them
    // over before it records them, so that a wait in a test means the pipeline has seen them), and the pipeline is given the connection to read.
    private sealed class Live : IAsyncDisposable
    {
        private readonly ChangeNotifier _notifier;

        private Live(ConnectionRig rig, RealmState state, IngestionPipeline pipeline, ChangeNotifier notifier, FakeHaSession session)
        {
            Rig = rig;
            State = state;
            Pipeline = pipeline;
            _notifier = notifier;
            Session = session;
        }

        public ConnectionRig Rig { get; }

        public RealmState State { get; }

        public IngestionPipeline Pipeline { get; }

        public FakeHaSession Session { get; }

        public ManualTimeProvider Time => Rig.Time;

        /// <summary>The HomeAssistant entry of the snapshot that was published last.</summary>
        public ConnectionVm HomeAssistant => State.Current.Connections.Single(c => c.Name == ConnectionNames.HomeAssistant);

        /// <summary>Starts the connection and returns once it is Connected and the pipeline has handled the initial snapshot and that state change.</summary>
        public static async Task<Live> StartAsync(Func<HaWebSocketOptions, HaWebSocketOptions> configure)
        {
            var rig = await ConnectionRig.StartAsync(Seed, configure: configure, start: false);
            var options = OptionsBinding.Defaults;
            var state = RealmState.CreateInitial(options, rig.Time);
            var notifier = new ChangeNotifier(rig.Time, new RecordingLogger<ChangeNotifier>());
            var pipeline = new IngestionPipeline(
                options,
                state,
                notifier,
                new NullWriter(),
                new NullQueries(),
                rig.Time,
                new RecordingLogger<IngestionPipeline>(),
                connection: rig.Connection);
            rig.Forward = (item, token) => new ValueTask(pipeline.ProcessAsync(new FeedItem(item), token));
            rig.ForwardStatus = pipeline.ApplyConnectionStatus;
            notifier.Start(); // the 30 s data tick, which the hosted pipeline starts in its consumer loop

            await rig.Connection.StartAsync(CancellationToken.None);
            var session = await rig.NextSessionAsync();
            await rig.WaitForItemsAsync(1);
            await rig.WaitForStateAsync(HaConnectionState.Connected);
            return new Live(rig, state, pipeline, notifier, session);
        }

        public async ValueTask DisposeAsync()
        {
            await Rig.DisposeAsync();
            Pipeline.Dispose();
            _notifier.Dispose();
        }

        private static void Seed(FakeHaServer server)
        {
            server.SetEntity(Fuel, "62", new Dictionary<string, object?> { ["unit_of_measurement"] = "%" });
        }
    }

    // The pipeline of these tests has no member, so nothing is ever written or read.
    private sealed class NullWriter : IRealmWriter
    {
        public bool EnqueueFix(string memberId, RawFix fix, bool inTrack = true, TrackReason? reason = null) => true;

        public bool EnqueueSignal(string memberId, PhoneSignal signal) => true;

        public bool EnqueueMeta(string key, string value) => true;

        public Task<bool> WriteTripAsync(string memberId, DetectedTrip trip, int algoVersion, string deriveHash, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task WriteRosterAsync(IReadOnlyList<RosterEntry> entries, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task FlushAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class NullQueries : IRealmQueries
    {
        public Task<IReadOnlyList<StatsTrip>> GetTripsAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, string? memberId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<StatsTrip>>([]);

        public Task<IReadOnlyDictionary<string, DateTimeOffset>> GetRecordingStartsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, DateTimeOffset>>(new Dictionary<string, DateTimeOffset>());

        public Task<IReadOnlyList<RawFix>> GetFixesAsync(string memberId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RawFix>>([]);

        public Task<RawFix?> GetLatestFixAsync(string memberId, FixSource source, CancellationToken cancellationToken = default) =>
            Task.FromResult<RawFix?>(null);

        public Task<IReadOnlyList<RosterEntry>> GetRosterAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RosterEntry>>([]);

        public Task<IReadOnlyList<DateTimeOffset>> GetFixTimesAsync(string memberId, FixSource source, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DateTimeOffset>>([]);

        public Task<RawFix?> GetLatestAddressFixAsync(string memberId, DateTimeOffset atOrBeforeUtc, CancellationToken cancellationToken = default) =>
            Task.FromResult<RawFix?>(null);

        public Task<IReadOnlyList<PhoneSignal>> GetPhoneSignalsAsync(string memberId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PhoneSignal>>([]);
    }
}
