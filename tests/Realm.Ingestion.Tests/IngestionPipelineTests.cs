using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Realm.Domain;
using Realm.Infrastructure.Ha;
using Realm.Infrastructure.Ingestion;
using Realm.Infrastructure.Options;
using Realm.TestKit;
using Xunit;

namespace Realm.Ingestion.Tests;

/// <summary>
/// 03 sections 2.8 and 2.9: the pipeline turns what Home Assistant and discovery report into the one immutable snapshot, publishes it BEFORE any row is queued
/// for the database, and announces it at most once a second (at once for a connection change). Nothing here touches a socket or a database: items go in
/// through <see cref="IngestionPipeline.ProcessAsync"/>, a recording writer and a fake query port stand in for SQLite, and a manual clock keeps all time.
/// Every id is fictional.
/// </summary>
public sealed class IngestionPipelineTests : IDisposable
{
    private const double HomeLat = 33.0;
    private const double HomeLon = -96.0;
    private const double MetresPerDegree = 111_320;

    private static readonly DateTimeOffset Start = new(2026, 9, 30, 17, 0, 0, TimeSpan.Zero);
    private static readonly RawPlace Home = new("home", "Hearth Haven", HomeLat, HomeLon, 100, false);
    private static readonly string[] AddressText = ["100 Castle Rd, Springfield, IL, USA"];

    private readonly List<IDisposable> _disposables = [];

    public void Dispose()
    {
        foreach (var disposable in _disposables)
        {
            disposable.Dispose();
        }
    }

    // ---- publish before persist -----------------------------------------------------------------------------------

    [Fact]
    public async Task TheSnapshotIsPublished_BeforeTheFirstRowIsQueuedForTheDatabase()
    {
        var rig = NewRig();
        await rig.DiscoverAsync(Plans.Member("king", life360: Plans.KingTracker));
        double? latWhenWritten = null;
        long versionWhenWritten = -1;
        var versionBefore = rig.State.Version;
        rig.Writer.OnFix = (_, _) =>
        {
            latWhenWritten = rig.State.Current.Members.Single().Lat;
            versionWhenWritten = rig.State.Version;
        };

        await rig.FeedAsync(Life360(Plans.KingTracker, Start, HomeLat + 0.01, HomeLon));

        Assert.Equal(HomeLat + 0.01, latWhenWritten);
        Assert.True(versionWhenWritten > versionBefore);
        Assert.Single(rig.Writer.Fixes);
    }

    [Fact]
    public async Task AWriterThatFails_NeverStopsWhatTheUserSees_AndIsLoggedOnce()
    {
        var rig = NewRig();
        await rig.DiscoverAsync(Plans.Member("king", life360: Plans.KingTracker));
        rig.Writer.Failure = new IOException("disk full");

        await rig.FeedAsync(Life360(Plans.KingTracker, Start, HomeLat + 0.01, HomeLon));
        rig.Time.Advance(TimeSpan.FromSeconds(10));
        await rig.FeedAsync(Life360(Plans.KingTracker, Start.AddSeconds(10), HomeLat + 0.02, HomeLon));

        Assert.Equal(HomeLat + 0.02, rig.State.Current.Members.Single().Lat);
        Assert.Single(rig.Log.Messages(LogLevel.Error));   // one line a minute, whatever the rate
    }

    [Fact]
    public async Task EveryAcceptedFix_IsQueuedWithTheDetectorsDecision()
    {
        var rig = NewRig();
        await rig.DiscoverAsync(Plans.Member("king", life360: Plans.KingTracker));

        await rig.FeedAsync(Life360(Plans.KingTracker, Start, HomeLat + 0.01, HomeLon));
        await rig.FeedAsync(Life360(Plans.KingTracker, Start, HomeLat + 0.01, HomeLon));   // the same last_seen: not a new fix, not queued again

        var (memberId, fix, _, _) = Assert.Single(rig.Writer.Fixes);
        Assert.Equal("king", memberId);
        Assert.Equal(Plans.KingTracker, fix.EntityId);
        Assert.Equal(FixSource.Life360, fix.Source);
        Assert.Equal(Start, fix.Ts);
    }

    // ---- the member rows ------------------------------------------------------------------------------------------

    [Fact]
    public async Task ALife360Fix_BecomesAFreshMemberWithItsBatteryAndAddress()
    {
        var rig = NewRig();
        await rig.DiscoverAsync(Plans.Member("king", life360: Plans.KingTracker, avatar: "/api/image/serve/abc123/512x512"));

        await rig.FeedAsync(Life360(Plans.KingTracker, Start, HomeLat + 0.01, HomeLon, battery: 80, charging: true));

        var king = Assert.Single(rig.State.Current.Members);
        Assert.Equal("king", king.Id);
        Assert.Equal(Freshness.Fresh, king.Freshness);
        Assert.Equal(HomeLat + 0.01, king.Lat);
        Assert.Equal(80, king.BatteryPct);
        Assert.True(king.Charging);
        Assert.Equal("100 Castle Rd", king.Street);
        Assert.Equal("Springfield", king.City);
        Assert.Equal("IL", king.Region);
        Assert.Equal(Start, king.LastUpdateUtc);
        Assert.Equal("avatars/king", king.AvatarUrl);   // our own proxy, never the upstream address
        Assert.False(king.IsDriving);
    }

    [Fact]
    public async Task AMemberWithoutAPicture_HasNoAvatarUrl()
    {
        var rig = NewRig();
        await rig.DiscoverAsync(Plans.Member("king", life360: Plans.KingTracker, avatar: null));

        Assert.Null(Assert.Single(rig.State.Current.Members).AvatarUrl);
    }

    [Fact]
    public async Task AMemberNobodyHasHeardFrom_IsNoFix()
    {
        var rig = NewRig();

        await rig.DiscoverAsync(Plans.Member("king", life360: Plans.KingTracker));

        var king = Assert.Single(rig.State.Current.Members);
        Assert.Equal(Freshness.NoFix, king.Freshness);
        Assert.Null(king.Lat);
    }

    [Fact]
    public async Task TheFreshestSourceWins_AndTheAndroidBatterySensorFillsInABatteryTheFixLacks()
    {
        var sensors = new CompanionSensors("sensor.king_phone_battery_level", "sensor.king_phone_battery_state", null, null, null);
        var rig = NewRig();
        await rig.DiscoverAsync(Plans.Member("king", life360: Plans.KingTracker, companion: Plans.KingPhone, sensors: sensors));
        await rig.FeedAsync(Life360(Plans.KingTracker, Start, HomeLat + 0.01, HomeLon, battery: 80));

        rig.Time.Advance(TimeSpan.FromSeconds(10));
        await rig.FeedAsync(Companion(Plans.KingPhone, rig.Time.GetUtcNow(), HomeLat + 0.02, HomeLon));
        rig.Time.Advance(TimeSpan.FromSeconds(10));
        await rig.FeedAsync(Sensor("sensor.king_phone_battery_level", "42", rig.Time.GetUtcNow()));
        await rig.FeedAsync(Sensor("sensor.king_phone_battery_state", "discharging", rig.Time.GetUtcNow()));

        var king = Assert.Single(rig.State.Current.Members);
        Assert.Equal(HomeLat + 0.02, king.Lat);   // the companion fix is newer
        Assert.Equal(42, king.BatteryPct);        // the sensor reading is newer than the Life360 battery
        Assert.False(king.Charging);
        Assert.Equal(2, rig.Writer.Fixes.Count);
    }

    // 02 section 1.6 (CR1-006): a companion fix counts only when the coordinates changed, so a state that only changes its attributes (accuracy, altitude)
    // minutes later is neither stored nor does it make the member look fresher than the last position report.
    [Fact]
    public async Task ACompanionStateWithUnchangedCoordinates_IsNotANewFix()
    {
        var rig = NewRig();
        await rig.DiscoverAsync(Plans.Member("king", companion: Plans.KingPhone));
        var first = rig.Time.GetUtcNow();
        await rig.FeedAsync(Companion(Plans.KingPhone, first, HomeLat + 0.02, HomeLon));

        rig.Time.Advance(TimeSpan.FromMinutes(10));
        var later = rig.Time.GetUtcNow();
        await rig.FeedAsync(Entity(Plans.KingPhone, "not_home", later, ("latitude", HomeLat + 0.02), ("longitude", HomeLon), ("gps_accuracy", 4.0), ("altitude", 212.0)));

        Assert.Single(rig.Writer.Fixes);
        Assert.Equal(first, rig.State.Current.Members.Single().LastUpdateUtc);

        rig.Time.Advance(TimeSpan.FromMinutes(1));
        await rig.FeedAsync(Companion(Plans.KingPhone, rig.Time.GetUtcNow(), HomeLat + 0.021, HomeLon));

        Assert.Equal(2, rig.Writer.Fixes.Count);
        Assert.Equal(rig.Time.GetUtcNow(), rig.State.Current.Members.Single().LastUpdateUtc);
    }

    [Fact]
    public async Task AStaticMember_IsAPinThatIsNeverStale_AndHasNoDetector()
    {
        var rig = NewRig();
        var prince = Plans.Member("prince") with
        {
            Kind = MemberKind.Static,
            StaticLabel = "Home",
            StaticLat = HomeLat,
            StaticLon = HomeLon,
        };

        await rig.DiscoverWithAsync([Home], null, prince);
        rig.Time.Advance(TimeSpan.FromDays(3));
        rig.Pipeline.Tick();

        var member = Assert.Single(rig.State.Current.Members);
        Assert.Equal(Freshness.Static, member.Freshness);
        Assert.Equal(HomeLat, member.Lat);
        Assert.Equal("Home", member.StaticLabel);
        Assert.Equal("home", member.PlaceId);
        Assert.Empty(rig.Writer.Fixes);
    }

    // ---- places ----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task AMemberInsideAZone_IsInThatPlace_UntilTheyLeaveIt()
    {
        var rig = NewRig();
        await rig.DiscoverWithAsync([Home], null, Plans.Member("king", life360: Plans.KingTracker));

        await rig.FeedAsync(Life360(Plans.KingTracker, Start, HomeLat, HomeLon, state: "home"));
        var inside = rig.State.Current;
        rig.Time.Advance(TimeSpan.FromMinutes(5));
        await rig.FeedAsync(Life360(Plans.KingTracker, rig.Time.GetUtcNow(), HomeLat + 0.02, HomeLon));
        var outside = rig.State.Current;

        Assert.Equal("home", inside.Members.Single().PlaceId);
        Assert.Equal(new[] { "king" }, inside.Places.Single().MemberIdsInside);
        Assert.Equal(Start, inside.Members.Single().SinceUtc);
        Assert.Null(outside.Members.Single().PlaceId);
        Assert.Empty(outside.Places.Single().MemberIdsInside);
    }

    [Fact]
    public async Task TheZonesAsDrawn_FollowTheOptions_AndTheOversizedAreLeftOut()
    {
        var options = OptionsBinding.Defaults with
        {
            Places =
            [
                new PlaceOption("zone.home", "Hearth", "The keep", PlaceKind.Home, Hidden: false),
                new PlaceOption("zone.hall", null, null, PlaceKind.Fun, Hidden: true),
            ],
        };
        var rig = NewRig(options);
        RawPlace[] zones =
        [
            Home,
            new("hall", "Jester's Hall", 33.1, -96.1, 80, false),
            new("arrival", "Arrival", 33.2, -96.2, 6000, false),
            new("shed", "Hearth", 33.3, -96.3, 50, false),   // the same name as the renamed home
        ];

        await rig.DiscoverWithAsync(zones, null, Plans.Member("king", life360: Plans.KingTracker));

        var places = rig.State.Current.Places;
        Assert.Equal(new[] { "home", "shed" }, places.Select(p => p.Id));
        Assert.Equal(new[] { "Hearth", "Hearth (2)" }, places.Select(p => p.DisplayName));
        Assert.Equal("The keep", places[0].Subtitle);
        Assert.Equal(PlaceKind.Home, places[0].Kind);
    }

    [Fact]
    public async Task AZoneUpdate_ReplacesTheZones()
    {
        var rig = NewRig();
        await rig.DiscoverWithAsync([Home], null, Plans.Member("king", life360: Plans.KingTracker));

        await rig.Pipeline.ProcessAsync(new ZonesUpdated([new RawPlace("home", "Hearth Haven", HomeLat, HomeLon, 100, false), new RawPlace("park", "Elm Park", 33.5, -96.5, 300, false)]), CancellationToken.None);

        Assert.Equal(new[] { "home", "park" }, rig.State.Current.Places.Select(p => p.Id).Order());
    }

    // ---- driving ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task ADriveIsLiveAsItHappens_AndEndsWhenTheFixesStop()
    {
        var rig = NewRig();
        await rig.DiscoverWithAsync([Home], null, Plans.Member("king", life360: Plans.KingTracker));
        var closed = new List<(string MemberId, DetectedTrip Trip)>();
        rig.Pipeline.TripClosed += (memberId, trip) => closed.Add((memberId, trip));

        // 15 m/s due north, a fix every 30 s, from a quarter of a kilometre out.
        var lat = HomeLat + 0.01;
        for (var i = 0; i < 8; i++)
        {
            await rig.FeedAsync(Life360(Plans.KingTracker, rig.Time.GetUtcNow(), lat, HomeLon, speedMph: 15 / FixParser.MphToMps));
            lat += 450 / MetresPerDegree;
            rig.Time.Advance(TimeSpan.FromSeconds(30));
        }

        var driving = rig.State.Current.Members.Single();
        Assert.True(driving.IsDriving);
        Assert.NotNull(driving.SinceUtc);
        Assert.Empty(closed);
        Assert.All(rig.Writer.Fixes, fix => Assert.Equal("king", fix.MemberId));

        // The last fix is 30 s old; after a quiet quarter hour the 30 s tick closes the trip.
        rig.Time.Advance(TimeSpan.FromMinutes(15));
        rig.Pipeline.Tick();

        Assert.False(rig.State.Current.Members.Single().IsDriving);
        var (memberId, trip) = Assert.Single(closed);
        Assert.Equal("king", memberId);
        Assert.True(trip.DistanceGpsM > 2000);
        Assert.Empty(rig.Writer.Trips);   // persisting a trip is the stats service's job: the pipeline only raises it
    }

    [Fact]
    public async Task ATripClosedSubscriberThatThrows_IsLogged_AndTheOthersStillRun()
    {
        var rig = NewRig();
        await rig.DiscoverWithAsync([Home], null, Plans.Member("king", life360: Plans.KingTracker));
        var ran = 0;
        rig.Pipeline.TripClosed += (_, _) => throw new InvalidOperationException("broken subscriber");
        rig.Pipeline.TripClosed += (_, _) => ran++;
        var lat = HomeLat + 0.01;
        for (var i = 0; i < 8; i++)
        {
            await rig.FeedAsync(Life360(Plans.KingTracker, rig.Time.GetUtcNow(), lat, HomeLon, speedMph: 15 / FixParser.MphToMps));
            lat += 450 / MetresPerDegree;
            rig.Time.Advance(TimeSpan.FromSeconds(30));
        }

        rig.Time.Advance(TimeSpan.FromMinutes(15));
        rig.Pipeline.Tick();

        Assert.Equal(1, ran);
        Assert.Single(rig.Log.Messages(LogLevel.Warning));
    }

    // ---- freshness and the tick ------------------------------------------------------------------------------------

    [Fact]
    public async Task TheTick_AgesAMemberIntoStaleAndOfflineWithoutAnyEvent()
    {
        var rig = NewRig();
        await rig.DiscoverAsync(Plans.Member("king", life360: Plans.KingTracker));
        await rig.FeedAsync(Life360(Plans.KingTracker, Start, HomeLat + 0.01, HomeLon));
        Assert.Equal(Freshness.Fresh, rig.State.Current.Members.Single().Freshness);

        rig.Time.Advance(TimeSpan.FromHours(2));
        rig.Pipeline.Tick();
        Assert.Equal(Freshness.Stale, rig.State.Current.Members.Single().Freshness);

        rig.Time.Advance(TimeSpan.FromHours(23));
        rig.Pipeline.Tick();
        Assert.Equal(Freshness.Offline, rig.State.Current.Members.Single().Freshness);
        Assert.Equal(rig.Time.GetUtcNow(), rig.State.Current.ServerNowUtc);
    }

    // ---- connections -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task TheSnapshot_HasExactlyTheFourConnections_InOrder()
    {
        var rig = NewRig();

        await rig.DiscoverAsync(Plans.Member("king", life360: Plans.KingTracker));

        Assert.Equal(
            new[] { ConnectionNames.HomeAssistant, ConnectionNames.Life360Trackers, ConnectionNames.FordPass, ConnectionNames.VehiclePlaceholder },
            rig.State.Current.Connections.Select(c => c.Name));
        Assert.Equal(ConnectionState.NotConnected, rig.State.Current.Connections[3].State);
        Assert.Equal(ConnectionState.NotConnected, rig.State.Current.Connections[2].State);   // no vehicle
    }

    [Fact]
    public void HomeAssistant_ReadsReconnectingForFifteenSeconds_ThenUnavailable_AndTheChangeIsAnnouncedAtOnce()
    {
        var rig = NewRig();
        var changes = rig.CountChanges();
        var outage = rig.Time.GetUtcNow();

        rig.Pipeline.ApplyConnectionStatus(new HaConnectionStatus(HaConnectionState.Reconnecting, outage, outage - TimeSpan.FromSeconds(5), 1, outage + TimeSpan.FromSeconds(1)));
        Assert.Equal(ConnectionState.Reconnecting, HomeAssistantState(rig));

        rig.Time.Advance(HaConnectionStatus.ReconnectingWindow + TimeSpan.FromSeconds(1));   // the pipeline's own check, not the 30 s tick

        Assert.Equal(ConnectionState.Unavailable, HomeAssistantState(rig));
        changes.WaitFor(2);   // Reconnecting, then Unavailable: each a state change, so neither waits for the one-second interval
    }

    [Fact]
    public void HomeAssistant_WhenConnected_ReadsConnectedAndCarriesTheLastActivity()
    {
        var rig = NewRig();
        var now = rig.Time.GetUtcNow();

        rig.Pipeline.ApplyConnectionStatus(new HaConnectionStatus(HaConnectionState.Connected, null, now, 0, null));

        var connection = rig.State.Current.Connections[0];
        Assert.Equal(ConnectionState.Connected, connection.State);
        Assert.Equal(now, connection.LastSyncUtc);
    }

    [Fact]
    public void AnAuthFailure_IsUnavailableAtOnce()
    {
        var rig = NewRig();

        rig.Pipeline.ApplyConnectionStatus(new HaConnectionStatus(HaConnectionState.AuthFailed, rig.Time.GetUtcNow(), null, 1, null));

        Assert.Equal(ConnectionState.Unavailable, HomeAssistantState(rig));
    }

    [Fact]
    public async Task Life360_IsUnavailableOnlyWhenEveryTrackerHasBeenUnavailableForFiveMinutes()
    {
        var rig = NewRig();
        await rig.DiscoverAsync(Plans.Member("king", life360: Plans.KingTracker), Plans.Member("queen", life360: Plans.QueenTracker));
        await rig.FeedAsync(Life360(Plans.KingTracker, Start, HomeLat + 0.01, HomeLon), Life360(Plans.QueenTracker, Start, HomeLat + 0.02, HomeLon));
        Assert.Equal(ConnectionState.Connected, Life360State(rig));

        // One tracker down is not the Life360 banner.
        rig.Time.Advance(TimeSpan.FromMinutes(1));
        await rig.FeedAsync(Unavailable(Plans.KingTracker, rig.Time.GetUtcNow()));
        rig.Time.Advance(TimeSpan.FromMinutes(10));
        rig.Pipeline.Tick();
        Assert.Equal(ConnectionState.Connected, Life360State(rig));

        // Both down: Reconnecting at first, Unavailable once the later of them has been down for 5 minutes.
        await rig.FeedAsync(Unavailable(Plans.QueenTracker, rig.Time.GetUtcNow()));
        rig.Time.Advance(TimeSpan.FromMinutes(4));
        rig.Pipeline.Tick();
        Assert.Equal(ConnectionState.Reconnecting, Life360State(rig));
        rig.Time.Advance(TimeSpan.FromMinutes(1));
        rig.Pipeline.Tick();
        Assert.Equal(ConnectionState.Unavailable, Life360State(rig));

        // One of them reports again: connected, and the member is back.
        await rig.FeedAsync(Life360(Plans.KingTracker, rig.Time.GetUtcNow(), HomeLat + 0.03, HomeLon));
        Assert.Equal(ConnectionState.Connected, Life360State(rig));
    }

    // ---- announcing ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task AnItemThatChangesNothing_PublishesAndAnnouncesNothing()
    {
        var rig = NewRig();
        await rig.DiscoverAsync(Plans.Member("king", life360: Plans.KingTracker));
        var changes = rig.CountChanges();
        var version = rig.State.Version;

        await rig.FeedAsync(Sensor("sensor.nobody_cares", "1", rig.Time.GetUtcNow()));   // not on the watch list

        Assert.Equal(version, rig.State.Version);
        await changes.ExpectNoMoreAsync(0);
    }

    [Fact]
    public async Task Changes_AreAnnouncedAtMostOncePerSecond_WithTheLastOneFollowingAtTheEndOfIt()
    {
        var rig = NewRig();
        await rig.DiscoverAsync(Plans.Member("king", life360: Plans.KingTracker));
        var changes = rig.CountChanges();
        rig.Time.Advance(TimeSpan.FromSeconds(2));

        await rig.FeedAsync(Life360(Plans.KingTracker, rig.Time.GetUtcNow(), HomeLat + 0.01, HomeLon));
        changes.WaitFor(1);                                   // after a quiet second the first change goes out at once
        rig.Time.Advance(TimeSpan.FromMilliseconds(100));
        await rig.FeedAsync(Life360(Plans.KingTracker, rig.Time.GetUtcNow(), HomeLat + 0.02, HomeLon));
        rig.Time.Advance(TimeSpan.FromMilliseconds(100));
        await rig.FeedAsync(Life360(Plans.KingTracker, rig.Time.GetUtcNow(), HomeLat + 0.03, HomeLon));
        await changes.ExpectNoMoreAsync(1);                   // folded into one event at the end of the second

        rig.Time.Advance(TimeSpan.FromMilliseconds(800));
        changes.WaitFor(2);
        await changes.ExpectNoMoreAsync(2);
        Assert.Equal(HomeLat + 0.03, rig.State.Current.Members.Single().Lat);
    }

    [Fact]
    public async Task ASubscriberThatThrows_NeverStopsThePipeline_OrTheOtherSubscribers()
    {
        var rig = NewRig();
        await rig.DiscoverAsync(Plans.Member("king", life360: Plans.KingTracker));
        rig.Notifier.Changed += () => throw new InvalidOperationException("a broken circuit");
        var changes = rig.CountChanges();
        rig.Time.Advance(TimeSpan.FromSeconds(2));

        await rig.FeedAsync(Life360(Plans.KingTracker, rig.Time.GetUtcNow(), HomeLat + 0.01, HomeLon));

        changes.WaitFor(1);
        Assert.Equal(HomeLat + 0.01, rig.State.Current.Members.Single().Lat);
    }

    // ---- discovery -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task ANewDiscovery_KeepsWhatTheMembersThatStayAlreadyKnow_AndDropsTheOnesThatLeave()
    {
        var rig = NewRig();
        await rig.DiscoverAsync(Plans.Member("king", life360: Plans.KingTracker), Plans.Member("queen", life360: Plans.QueenTracker));
        await rig.FeedAsync(Life360(Plans.KingTracker, Start, HomeLat + 0.01, HomeLon), Life360(Plans.QueenTracker, Start, HomeLat + 0.02, HomeLon));

        await rig.DiscoverAsync(Plans.Member("king", life360: Plans.KingTracker));

        var king = Assert.Single(rig.State.Current.Members);
        Assert.Equal("king", king.Id);
        Assert.Equal(HomeLat + 0.01, king.Lat);
        Assert.Equal(2, rig.Writer.Fixes.Count);   // the discovery re-reads the held states and queues nothing twice
    }

    [Fact]
    public async Task TheStoredFixes_SeedANewMember_SoItShowsAtOnceAfterARestart()
    {
        var rig = NewRig();
        var stored = new RawFix(Plans.KingTracker, FixSource.Life360, Start.AddMinutes(-3), HomeLat + 0.05, HomeLon, AccuracyM: 12, BatteryPct: 61);
        rig.Queries.Latest[("king", FixSource.Life360)] = stored;
        rig.Queries.Times[("king", FixSource.Life360)] = [stored.Ts.AddMinutes(-5), stored.Ts.AddMinutes(-4), stored.Ts];

        await rig.DiscoverAsync(Plans.Member("king", life360: Plans.KingTracker));

        var king = Assert.Single(rig.State.Current.Members);
        Assert.Equal(HomeLat + 0.05, king.Lat);
        Assert.Equal(Freshness.Fresh, king.Freshness);
        Assert.Equal(61, king.BatteryPct);
        Assert.Empty(rig.Writer.Fixes);   // read, not re-written
    }

    [Fact]
    public async Task ADatabaseThatCannotAnswer_CostsOnlyTheSeed()
    {
        var rig = NewRig();
        rig.Queries.Failure = new IOException("the database is locked at /data/realm.db");

        await rig.DiscoverAsync(Plans.Member("king", life360: Plans.KingTracker));
        await rig.FeedAsync(Life360(Plans.KingTracker, Start, HomeLat + 0.01, HomeLon));

        Assert.Equal(HomeLat + 0.01, rig.State.Current.Members.Single().Lat);
        var warning = Assert.Single(rig.Log.Messages(LogLevel.Warning));
        Assert.DoesNotContain("realm.db", warning, StringComparison.Ordinal);   // the type of the failure, never its message
    }

    // ---- phone signals and vehicles --------------------------------------------------------------------------------

    [Fact]
    public async Task APhoneSensorTransition_IsQueuedOnce_AtTheTimeHomeAssistantChangedIt()
    {
        var sensors = new CompanionSensors(null, null, "binary_sensor.king_phone_interactive", null, null);
        var rig = NewRig();
        await rig.DiscoverAsync(Plans.Member("king", companion: Plans.KingPhone, sensors: sensors, phoneCapable: true));
        var on = Sensor("binary_sensor.king_phone_interactive", "on", Start.AddMinutes(-1));

        await rig.FeedAsync(on);
        await rig.Pipeline.ProcessAsync(new FeedItem(new HaSnapshot([on], Start)), CancellationToken.None);   // a reconnect delivers it again
        await rig.FeedAsync(Sensor("binary_sensor.king_phone_interactive", "off", Start));

        Assert.Equal(
            new[] { (Start.AddMinutes(-1), true), (Start, false) },
            rig.Writer.Signals.Select(s => (s.Signal.Ts, s.Signal.IsOn == true)));
        Assert.All(rig.Writer.Signals, s => Assert.Equal(PhoneSignalKind.Screen, s.Signal.Kind));
    }

    [Fact]
    public async Task AFordPassVehicle_ShowsItsSensors_AndIsQueuedAsOneSample()
    {
        var car = new ResolvedVehicle(
            "wagon",
            "Wagon",
            null,
            VehicleGlyph.Car,
            false,
            null,
            0,
            "fordpass_test",
            "device_tracker.fordpass_test",
            ["sensor.fordpass_test_odometer", "sensor.fordpass_test_fuel", "sensor.fordpass_test_lastrefresh"]);
        var rig = NewRig();
        await rig.DiscoverWithAsync(null, [car], Plans.Member("king", life360: Plans.KingTracker));
        var refreshed = Start.AddMinutes(-2);

        await rig.FeedAsync(
            Entity("sensor.fordpass_test_odometer", "12000", refreshed, ("unit_of_measurement", "mi")),
            Entity("sensor.fordpass_test_fuel", "62", refreshed),
            Entity("sensor.fordpass_test_lastrefresh", refreshed.ToString("O"), refreshed));

        var vehicle = Assert.Single(rig.State.Current.Vehicles);
        Assert.Equal(62, vehicle.FuelPct);
        Assert.Equal(12000 * 1609.344, vehicle.OdometerM);
        Assert.Equal(refreshed, vehicle.LastUpdateUtc);
        Assert.Equal(Freshness.Fresh, vehicle.Freshness);
        Assert.Equal(ConnectionState.Connected, rig.State.Current.Connections[2].State);
        var sample = rig.Writer.Samples.Last();
        Assert.Equal("wagon", sample.VehicleId);
        Assert.Equal(refreshed, sample.Ts);
        Assert.Equal(62, sample.FuelPct);
    }

    // T23 (02 section 5.10) through the whole path: the sensors, the parser and the snapshot's IsMoving (Domain VehicleRules, CR1-002).
    [Fact]
    public async Task AVehicleIsMovingOnlyWithTheIgnitionOn_AndAFreshSpeed_T23()
    {
        var car = new ResolvedVehicle(
            "wagon",
            "Wagon",
            null,
            VehicleGlyph.Car,
            false,
            null,
            0,
            "fordpass_test",
            "device_tracker.fordpass_test",
            ["sensor.fordpass_test_ignitionstatus", "sensor.fordpass_test_speed", "sensor.fordpass_test_lastrefresh"]);
        var rig = NewRig();
        await rig.DiscoverWithAsync(null, [car], Plans.Member("king", life360: Plans.KingTracker));
        var refreshed = Start.AddMinutes(-3);
        HaEntitySnapshot Speed(string mph) => Entity("sensor.fordpass_test_speed", mph, refreshed, ("unit_of_measurement", "mph"));

        // Parked with the brake on: the ignition reads ON and the speed 0.
        await rig.FeedAsync(
            Entity("sensor.fordpass_test_ignitionstatus", "ON", refreshed),
            Speed("0"),
            Entity("sensor.fordpass_test_lastrefresh", refreshed.ToString("O"), refreshed));
        var parked = Assert.Single(rig.State.Current.Vehicles);

        // 25 mph in a sample three minutes old.
        await rig.FeedAsync(Speed("25"));
        var driving = Assert.Single(rig.State.Current.Vehicles);

        // The same sample is twelve minutes old after nine more minutes: older than the 600 s of Vehicle.SpeedMaxAgeS.
        rig.Time.Advance(TimeSpan.FromMinutes(9));
        rig.Pipeline.Tick();
        var old = Assert.Single(rig.State.Current.Vehicles);

        Assert.False(parked.IsMoving);
        Assert.Equal(0, parked.SpeedMps);
        Assert.True(driving.IsMoving);
        Assert.Equal(11.176, driving.SpeedMps ?? double.NaN, 3);
        Assert.False(old.IsMoving);
        Assert.Null(old.SpeedMps);
    }

    [Fact]
    public async Task APlaceholderVehicle_IsARowWithNoData_AndTheFordPassConnectionStaysNotConnected()
    {
        var chariot = new ResolvedVehicle("chariot", "Chariot", null, VehicleGlyph.Car, true, "Coming soon", 1, null, null, []);
        var rig = NewRig();

        await rig.DiscoverWithAsync(null, [chariot], Plans.Member("king", life360: Plans.KingTracker));

        var vehicle = Assert.Single(rig.State.Current.Vehicles);
        Assert.True(vehicle.IsPlaceholder);
        Assert.Equal("Coming soon", vehicle.PlaceholderNote);
        Assert.Equal(ConnectionState.NotConnected, rig.State.Current.Connections[2].State);
    }

    // ---- the queue and the consumer --------------------------------------------------------------------------------

    [Fact]
    public async Task TheConsumer_ProcessesWhatIsQueued_AndWhatIsStillQueuedAtShutdown()
    {
        var rig = NewRig();
        await rig.DiscoverAsync(Plans.Member("king", life360: Plans.KingTracker));
        rig.Time.Advance(TimeSpan.FromSeconds(200));   // every fix below is in the past, so none is clamped to "now" and taken for a repeat
        await rig.StartConsumerAsync();
        var processedBefore = rig.Pipeline.ProcessedCount;

        for (var i = 0; i < 100; i++)
        {
            await rig.Pipeline.EnqueueAsync(new FeedItem(new HaStateChanged(Plans.KingTracker, Life360(Plans.KingTracker, Start.AddSeconds(i), HomeLat + 0.01 + (i * 0.0001), HomeLon), null, Start)), CancellationToken.None);
        }

        await rig.Pipeline.StopAsync(CancellationToken.None);   // completes the queue and waits for the consumer to drain it

        Assert.Equal(processedBefore + 100, rig.Pipeline.ProcessedCount);
        Assert.Equal(0, rig.Pipeline.QueueDepth);
        Assert.Equal(100, rig.Writer.Fixes.Count);
        Assert.False(rig.Pipeline.Health.IsFaulted);
    }

    [Fact]
    public async Task AFullQueue_SlowsTheProducer_AndNeverDropsAnItem()
    {
        var rig = NewRig();
        const int capacity = 4096;
        for (var i = 0; i < capacity; i++)
        {
            await rig.Pipeline.EnqueueAsync(new ZonesUpdated([]), CancellationToken.None);
        }

        var waiting = rig.Pipeline.EnqueueAsync(new ZonesUpdated([]), CancellationToken.None);
        await Task.Delay(50);
        Assert.False(waiting.IsCompleted);   // back-pressure: the 4 097th waits for room
        Assert.Equal(capacity, rig.Pipeline.QueueDepth);

        await rig.Pipeline.StartAsync(CancellationToken.None);
        await waiting;
        await rig.Pipeline.StopAsync(CancellationToken.None);

        Assert.Equal(capacity + 1, rig.Pipeline.ProcessedCount);
    }

    [Fact]
    public async Task AnItemThatFails_IsSkipped_AndTheStreamGoesOn()
    {
        var rig = NewRig();
        await rig.DiscoverAsync(Plans.Member("king", life360: Plans.KingTracker));
        await rig.StartConsumerAsync();

        await rig.Pipeline.EnqueueAsync(new DiscoveryUpdated(null!), CancellationToken.None);   // a bug: processing it throws
        await rig.Pipeline.EnqueueAsync(new FeedItem(new HaStateChanged(Plans.KingTracker, Life360(Plans.KingTracker, Start, HomeLat + 0.01, HomeLon), null, Start)), CancellationToken.None);
        await rig.Pipeline.StopAsync(CancellationToken.None);

        Assert.Equal(HomeLat + 0.01, rig.State.Current.Members.Single().Lat);
        Assert.Single(rig.Log.Messages(LogLevel.Error));
    }

    [Fact]
    public async Task NothingThatIsLogged_CarriesAPosition()
    {
        var rig = NewRig();
        rig.Queries.Failure = new IOException("boom");
        await rig.DiscoverAsync(Plans.Member("king", life360: Plans.KingTracker));
        rig.Writer.Failure = new IOException("disk full");
        await rig.FeedAsync(Life360(Plans.KingTracker, Start, HomeLat + 0.01234, HomeLon - 0.00789));

        Assert.NotEmpty(rig.Log.Entries);
        Assert.DoesNotContain("33.01234", rig.Log.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("96.00789", rig.Log.Text, StringComparison.Ordinal);
    }

    // ---- rig and builders -----------------------------------------------------------------------------------------

    private static ConnectionState HomeAssistantState(Rig rig) => rig.State.Current.Connections.Single(c => c.Name == ConnectionNames.HomeAssistant).State;

    private static ConnectionState Life360State(Rig rig) => rig.State.Current.Connections.Single(c => c.Name == ConnectionNames.Life360Trackers).State;

    private Rig NewRig(RealmOptions? options = null)
    {
        options ??= OptionsBinding.Defaults;
        var time = new ManualTimeProvider(Start);
        var state = RealmState.CreateInitial(options, time);
        var notifier = new ChangeNotifier(time, new RecordingLogger<ChangeNotifier>());
        var writer = new RecordingWriter(state);
        var queries = new FakeQueries();
        var log = new RecordingLogger<IngestionPipeline>();
        var pipeline = new IngestionPipeline(options, state, notifier, writer, queries, time, log);
        _disposables.Add(pipeline);
        _disposables.Add(notifier);
        return new Rig(pipeline, state, notifier, writer, queries, time, log);
    }

    private static HaEntitySnapshot Entity(string id, string state, DateTimeOffset at, params (string Key, object? Value)[] attributes) =>
        new(id, state, Attributes(attributes), at, at);

    private static HaEntitySnapshot Sensor(string id, string state, DateTimeOffset at) => Entity(id, state, at);

    private static HaEntitySnapshot Unavailable(string id, DateTimeOffset at) => Entity(id, "unavailable", at);

    // A Life360 tracker state: speed in mph, position and last_seen as the integration reports them.
    private static HaEntitySnapshot Life360(
        string id,
        DateTimeOffset seen,
        double lat,
        double lon,
        double? speedMph = null,
        int? battery = 75,
        bool? charging = null,
        string state = "not_home") =>
        Entity(
            id,
            state,
            seen,
            ("latitude", lat),
            ("longitude", lon),
            ("gps_accuracy", 12.0),
            ("speed", speedMph),
            ("battery_level", battery),
            ("battery_charging", charging),
            ("address", AddressText[0]),
            ("last_seen", seen.ToString("O")));

    // An Android companion tracker state: speed in m/s, the time of the position is the state's last update.
    private static HaEntitySnapshot Companion(string id, DateTimeOffset at, double lat, double lon) =>
        Entity(id, "not_home", at, ("latitude", lat), ("longitude", lon), ("gps_accuracy", 9.0));

    private static IReadOnlyDictionary<string, JsonElement> Attributes(IEnumerable<(string Key, object? Value)> values) =>
        values.Where(v => v.Value is not null).ToDictionary(v => v.Key, v => JsonSerializer.SerializeToElement(v.Value), StringComparer.Ordinal);

    private sealed record Rig(
        IngestionPipeline Pipeline,
        RealmState State,
        ChangeNotifier Notifier,
        RecordingWriter Writer,
        FakeQueries Queries,
        ManualTimeProvider Time,
        RecordingLogger<IngestionPipeline> Log)
    {
        public Task DiscoverAsync(params ResolvedMember[] members) => DiscoverWithAsync(null, null, members);

        public Task DiscoverWithAsync(IReadOnlyList<RawPlace>? zones, IReadOnlyList<ResolvedVehicle>? vehicles, params ResolvedMember[] members) =>
            Pipeline.ProcessAsync(new DiscoveryUpdated(Plans.Discovery(zones: zones, vehicles: vehicles, members: members)), CancellationToken.None);

        // Each entity goes in as a state change, the way the websocket delivers it.
        public async Task FeedAsync(params HaEntitySnapshot[] entities)
        {
            foreach (var entity in entities)
            {
                await Pipeline.ProcessAsync(new FeedItem(new HaStateChanged(entity.EntityId, entity, null, Time.GetUtcNow())), CancellationToken.None);
            }
        }

        public ChangeCounter CountChanges() => new(Notifier);

        // Starts the hosted service and waits until its consumer is demonstrably reading the queue: a host that is stopped before the loop got going never
        // reads it, which is fine at shutdown but would make a test of the drain depend on the thread pool.
        public async Task StartConsumerAsync()
        {
            var before = Pipeline.ProcessedCount;
            await Pipeline.StartAsync(CancellationToken.None);
            await Pipeline.EnqueueAsync(new ZonesUpdated([]), CancellationToken.None);
            Assert.True(SpinWait.SpinUntil(() => Pipeline.ProcessedCount > before, TimeSpan.FromSeconds(10)), "The consumer did not start");
        }
    }

    // Counts the announcements, which arrive on the thread pool.
    private sealed class ChangeCounter : IDisposable
    {
        private readonly ChangeNotifier _notifier;
        private int _count;

        public ChangeCounter(ChangeNotifier notifier)
        {
            _notifier = notifier;
            _notifier.Changed += OnChanged;
        }

        public int Count => Volatile.Read(ref _count);

        public void WaitFor(int count)
        {
            Assert.True(SpinWait.SpinUntil(() => Count >= count, TimeSpan.FromSeconds(5)), $"Expected {count} announcements, saw {Count}");
        }

        // Nothing else arrives: the thread pool had time to deliver anything that was on its way.
        public async Task ExpectNoMoreAsync(int expected)
        {
            await Task.Delay(100);
            Assert.Equal(expected, Count);
        }

        public void Dispose() => _notifier.Changed -= OnChanged;

        private void OnChanged() => Interlocked.Increment(ref _count);
    }

    private sealed class RecordingWriter(RealmState state) : IRealmWriter
    {
        private readonly object _gate = new();
        private readonly List<(string MemberId, RawFix Fix, bool InTrack, TrackReason? Reason)> _fixes = [];
        private readonly List<(string MemberId, PhoneSignal Signal)> _signals = [];
        private readonly List<VehicleSample> _samples = [];
        private readonly List<string> _trips = [];

        public Action<string, RawFix>? OnFix { get; set; }

        public Exception? Failure { get; set; }

        public IReadOnlyList<(string MemberId, RawFix Fix, bool InTrack, TrackReason? Reason)> Fixes
        {
            get
            {
                lock (_gate)
                {
                    return _fixes.ToArray();
                }
            }
        }

        public IReadOnlyList<(string MemberId, PhoneSignal Signal)> Signals
        {
            get
            {
                lock (_gate)
                {
                    return _signals.ToArray();
                }
            }
        }

        public IReadOnlyList<VehicleSample> Samples
        {
            get
            {
                lock (_gate)
                {
                    return _samples.ToArray();
                }
            }
        }

        public IReadOnlyList<string> Trips
        {
            get
            {
                lock (_gate)
                {
                    return _trips.ToArray();
                }
            }
        }

        public bool EnqueueFix(string memberId, RawFix fix, bool inTrack = true, TrackReason? reason = null)
        {
            _ = state.Version;
            OnFix?.Invoke(memberId, fix);
            if (Failure is { } failure)
            {
                throw failure;
            }

            lock (_gate)
            {
                _fixes.Add((memberId, fix, inTrack, reason));
            }

            return true;
        }

        public bool EnqueueVehicleSample(VehicleSample sample)
        {
            lock (_gate)
            {
                _samples.Add(sample);
            }

            return true;
        }

        public bool EnqueueSignal(string memberId, PhoneSignal signal)
        {
            lock (_gate)
            {
                _signals.Add((memberId, signal));
            }

            return true;
        }

        public Task<bool> WriteTripAsync(string memberId, DetectedTrip trip, int algoVersion, string deriveHash, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                _trips.Add(memberId);
            }

            return Task.FromResult(true);
        }

        public Task FlushAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeQueries : IRealmQueries
    {
        public Dictionary<(string MemberId, FixSource Source), RawFix> Latest { get; } = [];

        public Dictionary<(string MemberId, FixSource Source), DateTimeOffset[]> Times { get; } = [];

        public Exception? Failure { get; set; }

        public Task<IReadOnlyList<StatsTrip>> GetTripsAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, string? memberId, CancellationToken cancellationToken = default) =>
            Answer<IReadOnlyList<StatsTrip>>([]);

        public Task<IReadOnlyDictionary<string, DateTimeOffset>> GetRecordingStartsAsync(CancellationToken cancellationToken = default) =>
            Answer<IReadOnlyDictionary<string, DateTimeOffset>>(new Dictionary<string, DateTimeOffset>());

        public Task<IReadOnlyList<RawFix>> GetFixesAsync(string memberId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default) =>
            Answer<IReadOnlyList<RawFix>>([]);

        public Task<RawFix?> GetLatestFixAsync(string memberId, FixSource source, CancellationToken cancellationToken = default) =>
            Answer<RawFix?>(Latest.GetValueOrDefault((memberId, source)));

        public Task<IReadOnlyList<DateTimeOffset>> GetFixTimesAsync(string memberId, FixSource source, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default) =>
            Answer<IReadOnlyList<DateTimeOffset>>(Times.GetValueOrDefault((memberId, source)) ?? []);

        public Task<RawFix?> GetLatestAddressFixAsync(string memberId, DateTimeOffset atOrBeforeUtc, CancellationToken cancellationToken = default) =>
            Answer<RawFix?>(null);

        public Task<IReadOnlyList<PhoneSignal>> GetPhoneSignalsAsync(string memberId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default) =>
            Answer<IReadOnlyList<PhoneSignal>>([]);

        private Task<T> Answer<T>(T value) => Failure is { } failure ? Task.FromException<T>(failure) : Task.FromResult(value);
    }
}
