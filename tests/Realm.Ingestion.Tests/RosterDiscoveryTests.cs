using System.Text.Json;
using Realm.Domain;
using Realm.Infrastructure.Ha;
using Realm.Infrastructure.Roster;
using Xunit;

namespace Realm.Ingestion.Tests;

/// <summary>
/// The roster lifecycle end to end (D113, 02 section 2.7): the discovery refresher over a fake Home Assistant, the real <see cref="RosterService"/> and a real temporary
/// database, on a manual clock. The first start (every person, the trackers that had a position in 30 days, one summary notification), a tracker that appears later (People, a
/// notification of its own), a person gone for 7 days (Not tracked, a notification, off the map and out of the watch list), the owner's own moves (never undone) and that the
/// roster is read back from the database. Each pass is a refresher that is started, waits for its first discovery and stops, so nothing here sleeps.
/// </summary>
public sealed class RosterDiscoveryTests
{
    private const string King = "person.king";
    private const string Pickup = "device_tracker.pickup";
    private const string OldVan = "device_tracker.old_van";
    private const string Scooter = "device_tracker.scooter";

    private static readonly DateTimeOffset Start = StoreRig.Start;

    // ---- the first start ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task FirstStart_PutsEveryPersonAndEveryRecentTrackerInPeople_AndSendsOneSummary()
    {
        await using var rig = await StoreRig.StartAsync();
        Seed(rig, Person(King, "Alden Smith"), Tracker(Pickup, "Pickup", Start.AddDays(-1)), Tracker(OldVan, "Old van", Start.AddDays(-60)));
        rig.Gateway.History = (entity, _, _, _) => entity == Pickup ? [Row(Pickup, "home", Start.AddDays(-2))] : [Row(OldVan, "unavailable", Start.AddDays(-40))];

        await PassAsync(rig);

        Assert.Equal([King, Pickup], Ids(rig, RosterGroup.People));
        Assert.Equal([OldVan], Ids(rig, RosterGroup.NotTracked));
        Assert.Equal("Alden", rig.Roster.Entries.Single(e => e.EntityId == King).DisplayName);
        Assert.Equal(["king", "tracker_pickup"], rig.Discovery.Current.Members.Select(m => m.Id).Order(StringComparer.Ordinal));
        var summary = Assert.Single(rig.Gateway.Notifications);
        Assert.Equal("ha_cartographer_summary", summary.Id);
        Assert.StartsWith("HA Cartographer: ", summary.Title, StringComparison.Ordinal);
        Assert.Contains("HA Cartographer → Settings → Who's on the map", summary.Message, StringComparison.Ordinal);
        Assert.Equal(2, rig.Gateway.HistoryCalls.Count);   // one per tracker, and none for the person
        Assert.All(rig.Gateway.HistoryCalls, call => Assert.Equal(Start.AddDays(-30), call.Start));
    }

    [Fact]
    public async Task FirstStart_ATrackerWhoseHistoryIsEmpty_IsJudgedByItsCurrentState()
    {
        await using var rig = await StoreRig.StartAsync();
        Seed(rig, Tracker(Pickup, "Pickup", Start.AddDays(-1)), Tracker(OldVan, "Old van", Start.AddDays(-60)));
        rig.Gateway.History = (_, _, _, _) => [];

        await PassAsync(rig);

        Assert.Equal([Pickup], Ids(rig, RosterGroup.People));
        Assert.Equal([OldVan], Ids(rig, RosterGroup.NotTracked));
    }

    [Fact]
    public async Task FirstStart_AnUnreadableHistory_KeepsTheTracker()
    {
        await using var rig = await StoreRig.StartAsync();
        Seed(rig, Tracker(Pickup, "Pickup", Start.AddDays(-1)));
        rig.Gateway.History = (_, _, _, _) => throw new HttpRequestException("history is down");

        await PassAsync(rig);

        Assert.Equal([Pickup], Ids(rig, RosterGroup.People));
    }

    // ---- later passes -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task ANewTrackerLater_JoinsPeople_WithANotificationOfItsOwn_AndNoHistoryRead()
    {
        await using var rig = await StoreRig.StartAsync();
        Seed(rig, Person(King, "Alden Smith"));
        await PassAsync(rig);
        var historyCalls = rig.Gateway.HistoryCalls.Count;
        rig.Time.Advance(TimeSpan.FromHours(1));
        Seed(rig, Person(King, "Alden Smith"), Tracker(Scooter, "Scooter", rig.Time.GetUtcNow()));

        await PassAsync(rig);

        Assert.Equal([King, Scooter], Ids(rig, RosterGroup.People));
        Assert.Equal(["ha_cartographer_summary", "ha_cartographer_device_tracker.scooter"], rig.Gateway.Notifications.Select(n => n.Id));
        Assert.Equal(historyCalls, rig.Gateway.HistoryCalls.Count);
        Assert.Contains("Who's on the map", rig.Gateway.Notifications[1].Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task APassThatChangesNothing_SendsNothing()
    {
        await using var rig = await StoreRig.StartAsync();
        Seed(rig, Person(King, "Alden Smith"));
        await PassAsync(rig);
        rig.Time.Advance(TimeSpan.FromHours(1));

        await PassAsync(rig);

        Assert.Single(rig.Gateway.Notifications);
    }

    [Fact]
    public async Task APersonGoneForSixDays_StaysInPeople_AndAtSevenMovesToNotTracked_WithANotification()
    {
        await using var rig = await StoreRig.StartAsync();
        Seed(rig, Person(King, "Alden Smith"));
        await PassAsync(rig);

        rig.Time.Advance(TimeSpan.FromDays(6));
        Seed(rig, Person(King, "Alden Smith", state: "unavailable"));
        await PassAsync(rig);
        Assert.Equal([King], Ids(rig, RosterGroup.People));
        Assert.Single(rig.Gateway.Notifications);

        rig.Time.Advance(TimeSpan.FromDays(1));
        await PassAsync(rig);

        var king = Assert.Single(rig.Roster.Entries);
        Assert.Equal(RosterGroup.NotTracked, king.Group);
        Assert.Equal(rig.Time.GetUtcNow(), king.AutoMovedUtc);
        Assert.Empty(rig.Discovery.Current.Members);
        Assert.DoesNotContain(King, rig.Discovery.Current.WatchList);
        Assert.Equal("ha_cartographer_person.king", rig.Gateway.Notifications[^1].Id);
    }

    [Fact]
    public async Task AnEntityRemovedFromHomeAssistant_IsRetiredTheSameWay()
    {
        await using var rig = await StoreRig.StartAsync();
        Seed(rig, Person(King, "Alden Smith"), Tracker(Pickup, "Pickup", Start));
        rig.Gateway.History = (_, _, _, _) => [Row(Pickup, "home", Start)];
        await PassAsync(rig);

        rig.Time.Advance(TimeSpan.FromDays(8));
        Seed(rig, Person(King, "Alden Smith"));
        await PassAsync(rig);

        Assert.Equal([King], Ids(rig, RosterGroup.People));
        Assert.Equal([Pickup], Ids(rig, RosterGroup.NotTracked));
        Assert.Equal("ha_cartographer_device_tracker.pickup", rig.Gateway.Notifications[^1].Id);
    }

    [Fact]
    public async Task WhatTheOwnerMovedToNotTracked_StaysThere_NotWatched_AndNeverNotified()
    {
        await using var rig = await StoreRig.StartAsync();
        Seed(rig, Person(King, "Alden Smith"));
        await PassAsync(rig);
        await rig.Roster.MoveAsync(King, RosterGroup.NotTracked);
        rig.Time.Advance(TimeSpan.FromHours(1));

        await PassAsync(rig);

        Assert.Equal(RosterGroup.NotTracked, Assert.Single(rig.Roster.Entries).Group);
        Assert.Null(rig.Roster.Entries[0].AutoMovedUtc);
        Assert.Empty(rig.Discovery.Current.Members);
        Assert.DoesNotContain(King, rig.Discovery.Current.WatchList);
        Assert.Single(rig.Gateway.Notifications);
    }

    [Fact]
    public async Task AVehicle_ComesFromTheTrackerOfItsEntry_AndNotTracked_FollowsNobody()
    {
        await using var rig = await StoreRig.StartAsync();
        Seed(rig, Tracker(Pickup, "Pickup", Start));
        rig.Gateway.History = (_, _, _, _) => [Row(Pickup, "home", Start)];
        await PassAsync(rig);

        await rig.Roster.MoveAsync(Pickup, RosterGroup.Vehicles);
        await PassAsync(rig);

        var vehicle = Assert.Single(rig.Discovery.Current.Vehicles);
        Assert.Equal("tracker_pickup", vehicle.Id);
        Assert.Equal(Pickup, vehicle.TrackerId);
        Assert.Empty(rig.Discovery.Current.Members);
        Assert.Contains(Pickup, rig.Discovery.Current.WatchList);
    }

    [Fact]
    public async Task ANotificationHomeAssistantRefuses_DoesNotStopTheDiscovery()
    {
        await using var rig = await StoreRig.StartAsync();
        Seed(rig, Person(King, "Alden Smith"));
        rig.Gateway.NotifyFailure = new HttpRequestException("HA said no");

        await PassAsync(rig);

        Assert.Empty(rig.Gateway.Notifications);
        Assert.Equal([King], Ids(rig, RosterGroup.People));
        Assert.Single(rig.Discovery.Current.Members);
    }

    // ---- the roster is stored -------------------------------------------------------------------------------------------

    [Fact]
    public async Task TheRoster_IsReadBackFromTheDatabase_WithTheOwnersMovesAndEdits()
    {
        await using var rig = await StoreRig.StartAsync();
        Seed(rig, Person(King, "Alden Smith"), Tracker(Pickup, "Pickup", Start));
        rig.Gateway.History = (_, _, _, _) => [Row(Pickup, "home", Start)];
        await PassAsync(rig);
        await rig.Roster.MoveAsync(Pickup, RosterGroup.Vehicles);
        await rig.Roster.UpdateAsync(King, "Alden the Bold", "Keeper of the Keys", "#112233");

        var again = new RosterService(rig.Queries, rig.Writer, rig.Time, new RecordingLogger<RosterService>());
        await again.LoadAsync(CancellationToken.None);

        Assert.Equal(rig.Roster.Entries, again.Entries);
        var king = again.Entries.Single(e => e.EntityId == King);
        Assert.Equal(("Alden the Bold", "Keeper of the Keys", "#112233"), (king.DisplayName, king.LoreTitle, king.Color));
        Assert.Equal(RosterGroup.Vehicles, again.Entries.Single(e => e.EntityId == Pickup).Group);
    }

    [Fact]
    public async Task TheRosterRaisesChanged_ForAnEditAndForAMove_AndNotForAnUnknownEntity()
    {
        await using var rig = await StoreRig.StartAsync();
        Seed(rig, Person(King, "Alden Smith"));
        await PassAsync(rig);
        var raised = 0;
        rig.Roster.Changed += () => Interlocked.Increment(ref raised);

        await rig.Roster.UpdateAsync(King, "Alden B", null, null);
        await rig.Roster.MoveAsync(King, RosterGroup.Vehicles);
        await rig.Roster.MoveAsync("person.nobody", RosterGroup.Vehicles);

        Assert.Equal(2, raised);
        Assert.Equal(RosterGroup.Vehicles, Assert.Single(rig.Roster.Entries).Group);
    }

    // ---- helpers ------------------------------------------------------------------------------------------------------

    private static async Task PassAsync(StoreRig rig)
    {
        using var refresher = new HaDiscoveryRefresher(
            rig.Gateway,
            rig.Roster,
            rig.Discovery,
            (_, _) => ValueTask.CompletedTask,
            rig.Time,
            new RecordingLogger<HaDiscoveryRefresher>(),
            rig.Counters);

        await refresher.StartAsync(CancellationToken.None);
        Assert.True(SpinWait.SpinUntil(() => refresher.LastRefreshUtc is not null, TimeSpan.FromSeconds(10)), "The discovery did not finish");

        // Stopping waits for the loop to leave its pass, so the notifications of the pass are sent by then.
        await refresher.StopAsync(CancellationToken.None);
    }

    private static void Seed(StoreRig rig, params HaEntitySnapshot[] states) => rig.Gateway.States = states;

    private static string[] Ids(StoreRig rig, RosterGroup group) =>
        [.. rig.Roster.Entries.Where(e => e.Group == group).Select(e => e.EntityId).Order(StringComparer.Ordinal)];

    private static HaEntitySnapshot Person(string id, string friendlyName, string state = "home") =>
        new(id, state, new Dictionary<string, JsonElement> { ["friendly_name"] = Json(friendlyName) }, Start, Start);

    private static HaEntitySnapshot Tracker(string id, string friendlyName, DateTimeOffset updated) =>
        new(
            id,
            "home",
            new Dictionary<string, JsonElement>
            {
                ["friendly_name"] = Json(friendlyName),
                ["latitude"] = Json(31.0),
                ["longitude"] = Json(-85.0),
                ["source_type"] = Json("gps"),
            },
            updated,
            updated);

    private static HaEntitySnapshot Row(string id, string state, DateTimeOffset at) =>
        new(id, state, new Dictionary<string, JsonElement>(), at, at);

    private static JsonElement Json<T>(T value) => JsonSerializer.SerializeToElement(value);
}
