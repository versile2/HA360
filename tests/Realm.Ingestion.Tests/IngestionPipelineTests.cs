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
    private const double HomeLon = -84.0;
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
            new("hall", "Jester's Hall", 33.1, -84.1, 80, false),
            new("arrival", "Arrival", 33.2, -84.2, 6000, false),
            new("shed", "Hearth", 33.3, -84.3, 50, false),   // the same name as the renamed home
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

        await rig.Pipeline.ProcessAsync(new ZonesUpdated([new RawPlace("home", "Hearth Haven", HomeLat, HomeLon, 100, false), new RawPlace("park", "Elm Park", 33.5, -84.5, 300, false)]), CancellationToken.None);

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
    public async Task TheSnapshot_HasExactlyTheTwoConnections_InOrder()
    {
        var rig = NewRig();

        await rig.DiscoverAsync(Plans.Member("king", life360: Plans.KingTracker));

        Assert.Equal(
            new[] { ConnectionNames.HomeAssistant, ConnectionNames.Life360Trackers },
            rig.State.Current.Connections.Select(c => c.Name));
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

    // ---- hydration: what a restart must not lose -------------------------------------------------------------------

    [Fact]
    public async Task ADriveThatWasOpenAtTheRestart_IsPickedUp_FromTheStoredHalfHour()
    {
        var rig = NewRig();
        var depart = Start.AddMinutes(-2);
        var rows = Drives.DriveRows(depart);
        rig.Queries.Fixes.AddRange(rows.Where(r => r.LastUpdatedUtc <= Start).Select(Drives.Fix));   // the lead-in and four fixes of driving
        var closed = new List<(string MemberId, DetectedTrip Trip)>();
        rig.Pipeline.TripClosed += (member, trip) => closed.Add((member, trip));

        await rig.DiscoverAsync(Plans.Member("king", life360: Plans.KingTracker));

        Assert.Empty(rig.Writer.Fixes);   // read back, never written again
        Assert.Empty(closed);
        foreach (var row in rows.Where(r => r.LastUpdatedUtc > Start))
        {
            rig.Time.Advance(row.LastUpdatedUtc!.Value - rig.Time.GetUtcNow());
            await rig.FeedAsync(row);
        }

        var (memberId, trip) = Assert.Single(closed);
        Assert.Equal("king", memberId);
        Assert.Equal(depart, trip.StartUtc);   // the departure the stored fixes show, not where this process first saw the car moving
    }

    [Fact]
    public async Task ATripThatClosedBeforeTheRestart_IsClosedAgainByTheReplay_ForTheRecorderToWrite()
    {
        var rig = NewRig();
        var depart = Start.AddMinutes(-14);
        rig.Queries.Fixes.AddRange(Drives.Drive(depart));   // the whole drive, its last fix half a minute before the restart
        var closed = new List<(string MemberId, DetectedTrip Trip)>();
        rig.Pipeline.TripClosed += (member, trip) => closed.Add((member, trip));

        await rig.DiscoverAsync(Plans.Member("king", life360: Plans.KingTracker));

        var (memberId, trip) = Assert.Single(closed);
        Assert.Equal("king", memberId);
        Assert.Equal(depart, trip.StartUtc);
        Assert.Empty(rig.Writer.Fixes);
    }

    [Fact]
    public async Task TheReplay_StartsAHalfHourBack()
    {
        var rig = NewRig();

        await rig.DiscoverAsync(Plans.Member("king", life360: Plans.KingTracker));

        Assert.Equal(("king", Start.AddMinutes(-30), Start.AddMilliseconds(1)), Assert.Single(rig.Queries.FixRanges));
    }

    [Fact]
    public async Task TheReplay_StartsTenMinutesBeforeAStoredTripItWouldCut_SoTheTripIsReplayedWhole()
    {
        var rig = NewRig();
        rig.Queries.Trips.Add(StoredTrip(Start.AddMinutes(-40), Start.AddMinutes(-20)));   // the half hour would begin in the middle of it

        await rig.DiscoverAsync(Plans.Member("king", life360: Plans.KingTracker));

        Assert.Equal(Start.AddMinutes(-50), Assert.Single(rig.Queries.FixRanges).From);
    }

    [Fact]
    public async Task TheReplay_FollowsAChainOfStoredTripsBack()
    {
        var rig = NewRig();
        rig.Queries.Trips.Add(StoredTrip(Start.AddMinutes(-40), Start.AddMinutes(-20)));
        rig.Queries.Trips.Add(StoredTrip(Start.AddMinutes(-70), Start.AddMinutes(-48)));   // the first move lands in the quiet time the second one needs

        await rig.DiscoverAsync(Plans.Member("king", life360: Plans.KingTracker));

        Assert.Equal(Start.AddMinutes(-80), Assert.Single(rig.Queries.FixRanges).From);
    }

    [Fact]
    public async Task TheReplay_IgnoresAStoredTripThatEndedBeforeIt()
    {
        var rig = NewRig();
        rig.Queries.Trips.Add(StoredTrip(Start.AddMinutes(-45), Start.AddMinutes(-35)));

        await rig.DiscoverAsync(Plans.Member("king", life360: Plans.KingTracker));

        Assert.Equal(Start.AddMinutes(-30), Assert.Single(rig.Queries.FixRanges).From);
    }

    [Fact]
    public async Task TheMarkOfAMember_IsWhatTheDatabaseHeldBeforeTheLiveFeedStoredAnything()
    {
        var rig = NewRig();
        var stored = new RawFix(Plans.KingTracker, FixSource.Life360, Start.AddMinutes(-3), HomeLat + 0.05, HomeLon);
        rig.Queries.Latest[("king", FixSource.Life360)] = stored;
        Assert.Null(rig.Hydrator.MarkOf("king"));

        await rig.DiscoverAsync(Plans.Member("king", life360: Plans.KingTracker));
        await rig.FeedAsync(Life360(Plans.KingTracker, Start, HomeLat + 0.06, HomeLon));   // the live feed's own fix does not move it

        Assert.Equal(new HydrationMark(Start, stored.Ts, null), rig.Hydrator.MarkOf("king"));
    }

    [Fact]
    public async Task AMemberWhoseHydrationFailed_StillHasAMark_SoTheBackfillIsNotHeldUpForEver()
    {
        var rig = NewRig();
        rig.Queries.Failure = new IOException("the database is locked");

        await rig.DiscoverAsync(Plans.Member("king", life360: Plans.KingTracker));

        Assert.Equal(new HydrationMark(Start, null, null), rig.Hydrator.MarkOf("king"));
    }

    // ---- the time zone ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task HomeAssistantsTimeZone_IsQueuedAsMeta_OnceForEachZoneItReports()
    {
        var rig = NewRig();

        await rig.DiscoverInZoneAsync("America/Chicago");
        await rig.DiscoverInZoneAsync("America/Chicago");   // the same zone again: nothing new to write
        await rig.DiscoverInZoneAsync("Europe/London");

        Assert.Equal([("ha_time_zone", "America/Chicago"), ("ha_time_zone", "Europe/London")], rig.Writer.Metas);
    }

    [Fact]
    public async Task AZoneTheWriterRefused_IsQueuedAgain_ByTheNextDiscovery()
    {
        var rig = NewRig();
        rig.Writer.RefuseMeta = true;
        await rig.DiscoverInZoneAsync("America/Chicago");
        Assert.Empty(rig.Writer.Metas);

        rig.Writer.RefuseMeta = false;
        await rig.DiscoverInZoneAsync("America/Chicago");

        Assert.Equal([("ha_time_zone", "America/Chicago")], rig.Writer.Metas);
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

    private static ResolvedVehicle Pickup(string trackerId = "device_tracker.pickup_gps", FixSource source = FixSource.Companion) =>
        new("tracker_pickup_gps", "Pickup", null, VehicleGlyph.Pickup, 0, trackerId, source);

    // A vehicle is a GPS tracker (D113): it follows the tracker's position and update time, and nothing about it is stored.
    [Fact]
    public async Task AVehicle_FollowsItsTracker_AndIsNeverStored()
    {
        var rig = NewRig();
        await rig.DiscoverWithAsync(null, [Pickup()], Plans.Member("king", life360: Plans.KingTracker));
        var seen = Start.AddMinutes(-2);

        await rig.FeedAsync(Companion("device_tracker.pickup_gps", seen, HomeLat + 0.01, HomeLon));

        var vehicle = Assert.Single(rig.State.Current.Vehicles);
        Assert.Equal("tracker_pickup_gps", vehicle.Id);
        Assert.Equal(HomeLat + 0.01, vehicle.Lat);
        Assert.Equal(HomeLon, vehicle.Lon);
        Assert.Equal(seen, vehicle.LastUpdateUtc);
        Assert.Equal(Freshness.Fresh, vehicle.Freshness);
        Assert.Empty(rig.Writer.Fixes);
    }

    [Fact]
    public async Task AVehicleWithoutAFix_HasNoPositionAndNoFix()
    {
        var rig = NewRig();

        await rig.DiscoverWithAsync(null, [Pickup()], Plans.Member("king", life360: Plans.KingTracker));

        var vehicle = Assert.Single(rig.State.Current.Vehicles);
        Assert.Null(vehicle.Lat);
        Assert.Null(vehicle.Lon);
        Assert.Equal(Freshness.NoFix, vehicle.Freshness);
        Assert.False(vehicle.IsMoving);
    }

    [Fact]
    public async Task AVehicleGoesStale_AfterFortyFiveMinutes()
    {
        var rig = NewRig();
        await rig.DiscoverWithAsync(null, [Pickup()], Plans.Member("king", life360: Plans.KingTracker));
        await rig.FeedAsync(Companion("device_tracker.pickup_gps", Start, HomeLat + 0.01, HomeLon));

        rig.Time.Advance(TimeSpan.FromMinutes(46));
        rig.Pipeline.Tick();

        Assert.Equal(Freshness.Stale, Assert.Single(rig.State.Current.Vehicles).Freshness);
    }

    // T23 (02 section 5.10) through the whole path: the tracker's speed, the parser and the snapshot's IsMoving (Domain VehicleRules, CR1-002).
    [Fact]
    public async Task AVehicleIsMovingOnlyWithAFreshSpeedAboveOneMetrePerSecond_T23()
    {
        var rig = NewRig();
        await rig.DiscoverWithAsync(null, [Pickup()], Plans.Member("king", life360: Plans.KingTracker));
        var seen = Start.AddMinutes(-3);
        HaEntitySnapshot Moving(double mps, DateTimeOffset at, double lat) =>
            Entity("device_tracker.pickup_gps", "not_home", at, ("latitude", lat), ("longitude", HomeLon), ("gps_accuracy", 9.0), ("speed", mps));

        // Parked with a speed of 0.
        await rig.FeedAsync(Moving(0, seen, HomeLat + 0.01));
        var parked = Assert.Single(rig.State.Current.Vehicles);

        // 25 mph in a fix three minutes old.
        await rig.FeedAsync(Moving(11.176, seen.AddSeconds(30), HomeLat + 0.011));
        var driving = Assert.Single(rig.State.Current.Vehicles);

        // The same fix is twelve minutes old after nine more minutes: older than the 600 s of Vehicle.SpeedMaxAgeS.
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
        Assert.DoesNotContain("84.00789", rig.Log.Text, StringComparison.Ordinal);
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
        var hydrator = new RealmStateHydrator(queries);
        var pipeline = new IngestionPipeline(options, state, notifier, writer, queries, time, log, hydrator);
        _disposables.Add(pipeline);
        _disposables.Add(notifier);
        return new Rig(pipeline, state, notifier, writer, queries, time, log, hydrator);
    }

    private static StatsTrip StoredTrip(DateTimeOffset start, DateTimeOffset end) =>
        new("king", start, end, 5000, TripQuality.Dense, DistanceBasis.Gps, 20, null, null, 0, null, null, null, null, null);

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
        RecordingLogger<IngestionPipeline> Log,
        RealmStateHydrator Hydrator)
    {
        public Task DiscoverAsync(params ResolvedMember[] members) => DiscoverWithAsync(null, null, members);

        public Task DiscoverInZoneAsync(string zone, params ResolvedMember[] members) =>
            Pipeline.ProcessAsync(new DiscoveryUpdated(Plans.Discovery(zone: zone, members: members)), CancellationToken.None);

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
        private readonly List<string> _trips = [];
        private readonly List<(string Key, string Value)> _metas = [];

        public Action<string, RawFix>? OnFix { get; set; }

        public Exception? Failure { get; set; }

        /// <summary>When true the writer refuses a meta row (its queue is full), as <see cref="IRealmWriter.EnqueueMeta"/> may.</summary>
        public bool RefuseMeta { get; set; }

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

        public IReadOnlyList<(string Key, string Value)> Metas
        {
            get
            {
                lock (_gate)
                {
                    return _metas.ToArray();
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

        public Task WriteRosterAsync(IReadOnlyList<RosterEntry> entries, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public bool EnqueueSignal(string memberId, PhoneSignal signal)
        {
            lock (_gate)
            {
                _signals.Add((memberId, signal));
            }

            return true;
        }

        public bool EnqueueMeta(string key, string value)
        {
            if (RefuseMeta)
            {
                return false;
            }

            lock (_gate)
            {
                _metas.Add((key, value));
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

        /// <summary>The stored fixes (any member, any source); a query returns those in its range.</summary>
        public List<RawFix> Fixes { get; } = [];

        /// <summary>The stored trips of "king"; a query returns those that start in its range.</summary>
        public List<StatsTrip> Trips { get; } = [];

        /// <summary>The ranges the stored fixes were asked for, in order.</summary>
        public List<(string MemberId, DateTimeOffset From, DateTimeOffset To)> FixRanges { get; } = [];

        public Exception? Failure { get; set; }

        public Task<IReadOnlyList<StatsTrip>> GetTripsAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, string? memberId, CancellationToken cancellationToken = default) =>
            Answer<IReadOnlyList<StatsTrip>>([.. Trips.Where(t => t.StartUtc >= fromUtc && t.StartUtc < toUtc && (memberId is null || t.MemberId == memberId))]);

        public Task<IReadOnlyDictionary<string, DateTimeOffset>> GetRecordingStartsAsync(CancellationToken cancellationToken = default) =>
            Answer<IReadOnlyDictionary<string, DateTimeOffset>>(new Dictionary<string, DateTimeOffset>());

        public Task<IReadOnlyList<RawFix>> GetFixesAsync(string memberId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default)
        {
            FixRanges.Add((memberId, fromUtc, toUtc));
            return Answer<IReadOnlyList<RawFix>>([.. Fixes.Where(f => f.Ts >= fromUtc && f.Ts < toUtc).OrderBy(f => f.Ts)]);
        }

        public Task<IReadOnlyList<RosterEntry>> GetRosterAsync(CancellationToken cancellationToken = default) =>
            Answer<IReadOnlyList<RosterEntry>>([]);

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
