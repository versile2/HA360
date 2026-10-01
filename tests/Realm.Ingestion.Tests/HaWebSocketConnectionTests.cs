using System.Net.WebSockets;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Realm.Domain;
using Realm.Infrastructure.Ha;
using Realm.TestKit;
using Xunit;

namespace Realm.Ingestion.Tests;

// The connection against FakeHaServer: real loopback sockets, a manual clock for every wait (03 sections 2.5 and 8.2). Each test waits for the state it needs
// (a state, feed items, a timer of a known length) and then moves time, so nothing sleeps. Entity ids are fictional.
public class HaWebSocketConnectionTests
{
    private const string King = "device_tracker.life360_king";
    private const string Fuel = "sensor.wagon_fuel";
    private const string Home = "zone.home";

    private static readonly TimeSpan OneSecond = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ThirtySeconds = TimeSpan.FromSeconds(30);

    private static void Seed(FakeHaServer server)
    {
        server.SetEntity(
            King,
            "home",
            new Dictionary<string, object?> { ["latitude"] = 38.89, ["longitude"] = -77.03, ["gps_accuracy"] = 15.2, ["battery_level"] = 80 },
            lastChanged: 1759200000.123,
            lastUpdated: 1759200050.456);
        server.SetEntity(Fuel, "62", new Dictionary<string, object?> { ["unit_of_measurement"] = "%" });
        server.SetEntity(Home, "0", new Dictionary<string, object?> { ["radius"] = 100 });
    }

    private static JsonDocument Parse(string json) => JsonDocument.Parse(json);

    // --- handshake and subscription -----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_handshake_authenticates_enables_coalescing_subscribes_and_delivers_the_snapshot()
    {
        await using var rig = await ConnectionRig.StartAsync(Seed);
        var session = await rig.NextSessionAsync();

        var items = await rig.WaitForItemsAsync(1);
        await rig.WaitForStateAsync(HaConnectionState.Connected);

        Assert.Equal(["auth", "supported_features", "subscribe_entities"], session.ReceivedTypes);
        using (var auth = Parse(session.Received[0]))
        {
            Assert.Equal(ConnectionRig.Token, auth.RootElement.GetProperty("access_token").GetString());
            Assert.False(auth.RootElement.TryGetProperty("id", out _)); // the auth message carries no command id
        }

        using (var features = Parse(session.Received[1]))
        {
            Assert.Equal(1, features.RootElement.GetProperty("id").GetInt32());
            Assert.Equal(1, features.RootElement.GetProperty("features").GetProperty("coalesce_messages").GetInt32());
        }

        using (var subscribe = Parse(session.Received[2]))
        {
            Assert.Equal(2, subscribe.RootElement.GetProperty("id").GetInt32());
            Assert.False(subscribe.RootElement.TryGetProperty("entity_ids", out _)); // no watch list: every entity
        }

        Assert.Equal([HaConnectionState.Connecting, HaConnectionState.Authenticating, HaConnectionState.Connected], rig.StateHistory);
        var snapshot = Assert.IsType<HaSnapshot>(Assert.Single(items));
        Assert.Equal(rig.Time.GetUtcNow(), snapshot.ReceivedAt);
        Assert.Equal([King, Fuel, Home], snapshot.Entities.Select(entity => entity.EntityId)); // ordered by id
        var king = snapshot.Entities.Single(entity => entity.EntityId == King);
        Assert.Equal("home", king.State);
        Assert.Equal(38.89, king.Attributes["latitude"].GetDouble());
        Assert.Equal(new DateTimeOffset(2025, 9, 30, 2, 40, 0, 123, TimeSpan.Zero), king.LastChangedUtc);
        Assert.Equal(new DateTimeOffset(2025, 9, 30, 2, 40, 50, 456, TimeSpan.Zero), king.LastUpdatedUtc);
        Assert.Equal(3, rig.Connection.EntityCount);

        var vm = rig.Connection.Status.ToConnectionVm(rig.Time.GetUtcNow());
        Assert.Equal(ConnectionNames.HomeAssistant, vm.Name);
        Assert.Equal(ConnectionState.Connected, vm.State);
        Assert.Equal(rig.Time.GetUtcNow(), vm.LastSyncUtc);
        Assert.DoesNotContain(ConnectionRig.Token, rig.Log.Text);
    }

    [Fact]
    public async Task The_subscription_carries_the_watch_list_as_entity_ids_and_the_snapshot_holds_only_those()
    {
        await using var rig = await ConnectionRig.StartAsync(Seed, start: false);
        rig.Connection.SetWatchList([Home, King, Home]);
        await rig.Connection.StartAsync(CancellationToken.None);
        var session = await rig.NextSessionAsync();

        var items = await rig.WaitForItemsAsync(1);

        using var subscribe = Parse(await session.WaitForMessageAsync("subscribe_entities"));
        Assert.Equal([King, Home], subscribe.RootElement.GetProperty("entity_ids").EnumerateArray().Select(id => id.GetString())); // sorted, no duplicates
        var snapshot = Assert.IsType<HaSnapshot>(items[0]);
        Assert.Equal([King, Home], snapshot.Entities.Select(entity => entity.EntityId));
    }

    [Fact]
    public async Task A_changed_watch_list_unsubscribes_and_subscribes_again_and_the_new_snapshot_replaces_the_old_one()
    {
        await using var rig = await ConnectionRig.StartAsync(Seed, start: false);
        rig.Connection.SetWatchList([King]);
        await rig.Connection.StartAsync(CancellationToken.None);
        var session = await rig.NextSessionAsync();
        await rig.WaitForItemsAsync(1);
        await rig.WaitForStateAsync(HaConnectionState.Connected);

        rig.Connection.SetWatchList([Home, King]);
        rig.Connection.SetWatchList([King, Home]); // the same set again changes nothing
        var items = await rig.WaitForItemsAsync(2);

        Assert.Equal(["auth", "supported_features", "subscribe_entities", "unsubscribe_events", "subscribe_entities"], session.ReceivedTypes);
        using var unsubscribe = Parse(session.Received[3]);
        using var second = Parse(session.Received[4]);
        Assert.Equal(2, unsubscribe.RootElement.GetProperty("subscription").GetInt32()); // the first subscription's command id
        Assert.NotEqual(2, second.RootElement.GetProperty("id").GetInt32());
        Assert.Equal([King, Home], second.RootElement.GetProperty("entity_ids").EnumerateArray().Select(id => id.GetString()));
        Assert.Equal([King, Home], Assert.IsType<HaSnapshot>(items[1]).Entities.Select(entity => entity.EntityId));

        // A late event of the cancelled subscription is ignored; one of the new subscription is applied (it would be missing if the removal had been).
        await session.SendTextAsync("{\"id\":2,\"type\":\"event\",\"event\":{\"r\":[\"zone.home\"]}}");
        await session.SendEventAsync("""{"c":{"zone.home":{"+":{"s":"1"}}}}""");
        var after = await rig.WaitForItemsAsync(3);
        var change = Assert.IsType<HaStateChanged>(after[2]);
        Assert.Equal(Home, change.EntityId);
        Assert.Equal("0", change.Previous?.State);
        Assert.Equal("1", change.New?.State);
    }

    // --- frames -------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Diffs_become_state_changes_in_the_order_they_arrive()
    {
        await using var rig = await ConnectionRig.StartAsync(Seed);
        var session = await rig.NextSessionAsync();
        await rig.WaitForItemsAsync(1);
        rig.Time.Advance(TimeSpan.FromSeconds(5));

        await session.SendEventAsync("""{"c":{"device_tracker.life360_king":{"+":{"s":"not_home","a":{"latitude":38.9},"lu":1759200065.0},"-":{"a":["gps_accuracy"]}}}}""");
        await session.SendEventAsync("""{"r":["sensor.wagon_fuel"]}""");
        var items = await rig.WaitForItemsAsync(3);

        var moved = Assert.IsType<HaStateChanged>(items[1]);
        Assert.Equal(King, moved.EntityId);
        Assert.Equal("home", moved.Previous?.State);
        Assert.Equal("not_home", moved.New?.State);
        Assert.Equal(38.9, moved.New?.Attributes["latitude"].GetDouble());
        Assert.False(moved.New?.Attributes.ContainsKey("gps_accuracy"));
        Assert.Equal(rig.Time.GetUtcNow(), moved.ReceivedAt);
        var removed = Assert.IsType<HaStateChanged>(items[2]);
        Assert.Equal(Fuel, removed.EntityId);
        Assert.Null(removed.New);
        Assert.Equal("62", removed.Previous?.State);
        Assert.Equal(2, rig.Connection.EntityCount);
    }

    [Fact]
    public async Task An_array_frame_is_read_message_by_message_in_order()
    {
        await using var rig = await ConnectionRig.StartAsync(Seed);
        var session = await rig.NextSessionAsync();
        await rig.WaitForItemsAsync(1);

        await session.SendCoalescedAsync(
            """{"c":{"device_tracker.life360_king":{"+":{"s":"work"}}}}""",
            """{"c":{"device_tracker.life360_king":{"+":{"s":"home"}}}}""",
            """{"a":{"zone.work":{"s":"0","lc":1759200000.0}}}""");
        var items = await rig.WaitForItemsAsync(4);

        Assert.Equal(["work", "home", "0"], items.Skip(1).Cast<HaStateChanged>().Select(change => change.New?.State));
        Assert.Equal(["home", "work", null], items.Skip(1).Cast<HaStateChanged>().Select(change => change.Previous?.State));
    }

    [Fact]
    public async Task A_subscription_result_and_the_snapshot_in_one_array_frame_complete_the_handshake()
    {
        await using var rig = await ConnectionRig.StartAsync(server =>
        {
            Seed(server);
            server.CoalesceInitialReply = true;
        });

        var items = await rig.WaitForItemsAsync(1);
        await rig.WaitForStateAsync(HaConnectionState.Connected);

        Assert.Equal(3, Assert.IsType<HaSnapshot>(items[0]).Entities.Count);
    }

    [Fact]
    public async Task A_message_that_arrives_in_fragments_is_assembled_before_it_is_parsed()
    {
        await using var rig = await ConnectionRig.StartAsync(server =>
        {
            Seed(server);
            server.SnapshotFragmentBytes = 7;
        });
        var session = await rig.NextSessionAsync();
        var snapshot = Assert.IsType<HaSnapshot>((await rig.WaitForItemsAsync(1))[0]);

        await session.SendFragmentedAsync(
            "{\"id\":2,\"type\":\"event\",\"event\":{\"c\":{\"device_tracker.life360_king\":{\"+\":{\"s\":\"work\",\"a\":{\"address\":\"Castle Road\"}}}}}}",
            5);
        var items = await rig.WaitForItemsAsync(2);

        Assert.Equal(3, snapshot.Entities.Count);
        Assert.Equal(38.89, snapshot.Entities.Single(entity => entity.EntityId == King).Attributes["latitude"].GetDouble());
        Assert.Equal("Castle Road", Assert.IsType<HaStateChanged>(items[1]).New?.Attributes["address"].GetString());
    }

    [Fact]
    public async Task A_message_larger_than_the_limit_closes_the_socket_and_reconnects()
    {
        await using var rig = await ConnectionRig.StartAsync(
            server =>
            {
                server.SetEntity("sensor.big", "x", new Dictionary<string, object?> { ["blob"] = new string('a', 2000) });
            },
            configure: options => options with { MaxMessageBytes = 512 });
        var session = await rig.NextSessionAsync();

        await rig.WaitForStateAsync(HaConnectionState.Reconnecting);
        await rig.Guard(session.Ended, "the socket to be closed");

        Assert.Contains("larger than 512 bytes", string.Join('\n', rig.Log.Messages(LogLevel.Information)));
    }

    [Fact]
    public async Task A_message_nested_deeper_than_the_limit_closes_the_socket_and_reconnects()
    {
        await using var rig = await ConnectionRig.StartAsync(Seed, configure: options => options with { MaxJsonDepth = 8 });
        var session = await rig.NextSessionAsync();
        await rig.WaitForItemsAsync(1);
        await rig.WaitForStateAsync(HaConnectionState.Connected);

        await session.SendEventAsync("""{"a":{"sensor.deep":{"s":"1","a":{"x":[[[[[[[[[[1]]]]]]]]]]}}}}""");
        await rig.WaitForStateAsync(HaConnectionState.Reconnecting);

        await rig.Guard(session.Ended, "the socket to be closed");
        Assert.Contains("not valid JSON within the limits", string.Join('\n', rig.Log.Messages(LogLevel.Information)));
    }

    // --- tokens -------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_missing_token_leaves_the_connection_NotConfigured_and_it_never_connects()
    {
        await using var rig = await ConnectionRig.StartAsync(Seed, token: null);

        var status = await rig.WaitForStateAsync(HaConnectionState.NotConfigured);

        Assert.Equal(0, rig.Server.AttemptCount);
        Assert.Equal(ConnectionState.Unavailable, status.ToConnectionState(rig.Time.GetUtcNow()));
        Assert.Equal([HaConnectionState.NotConfigured], rig.StateHistory);
    }

    [Fact]
    public async Task A_refused_token_is_retried_every_60_seconds_never_in_a_tight_loop_and_logged_once()
    {
        const string wrong = "wrong-token-9876";
        await using var rig = await ConnectionRig.StartAsync(Seed, token: wrong);
        var sixty = TimeSpan.FromSeconds(60);

        await rig.WaitForStateAsync(HaConnectionState.AuthFailed);
        Assert.Equal(1, rig.Server.AttemptCount);
        Assert.Equal(ConnectionState.Unavailable, rig.Connection.Status.ToConnectionState(rig.Time.GetUtcNow()));
        for (var attempt = 2; attempt <= 3; attempt++)
        {
            await rig.TimerAsync(sixty); // the wait is a minute, not the 1, 2, 5 s of a lost connection
            Assert.Equal(attempt - 1, rig.Server.AttemptCount);
            rig.Time.Advance(sixty);
            await rig.Server.WaitForAttemptsAsync(attempt);
        }

        await rig.TimerAsync(sixty);
        Assert.Equal(3, rig.Server.AttemptCount);
        Assert.Equal([HaConnectionState.Connecting, HaConnectionState.Authenticating, HaConnectionState.AuthFailed], rig.StateHistory); // no flicker between retries
        Assert.Single(rig.Log.Messages(LogLevel.Warning));

        rig.Server.ExpectedToken = wrong; // the owner fixes the token
        rig.Time.Advance(sixty);
        await rig.WaitForStateAsync(HaConnectionState.Connected);

        Assert.Equal(4, rig.Server.AttemptCount);
        Assert.DoesNotContain(wrong, rig.Log.Text);
        Assert.DoesNotContain(ConnectionRig.Token, rig.Log.Text);
    }

    // --- reconnecting -------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_wait_between_attempts_is_1_2_5_10_then_30_seconds_on_fake_time()
    {
        TimeSpan[] waits = [OneSecond, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), ThirtySeconds, ThirtySeconds];
        await using var rig = await ConnectionRig.StartAsync(server => server.ScriptNext(Enumerable.Repeat(FakeHaAttempt.Reject(502), 6).ToArray()));

        for (var i = 0; i < waits.Length; i++)
        {
            await rig.TimerAsync(waits[i]);
            Assert.Equal(i + 1, rig.Server.AttemptCount); // the next attempt waits for this timer
            rig.Time.Advance(waits[i]);
        }

        await rig.WaitForStateAsync(HaConnectionState.Connected);
        Assert.Equal(7, rig.Server.AttemptCount);
        Assert.Equal([HaConnectionState.Connecting, HaConnectionState.Reconnecting, HaConnectionState.Authenticating, HaConnectionState.Connected], rig.StateHistory);
    }

    [Fact]
    public async Task The_jitter_factor_scales_the_wait()
    {
        await using var rig = await ConnectionRig.StartAsync(
            server => server.ScriptNext(FakeHaAttempt.Reject(502)),
            configure: options => options with { Jitter = () => 0.5 });

        await rig.TimerAsync(TimeSpan.FromSeconds(0.5));
    }

    [Fact]
    public void Printing_the_options_never_shows_the_token()
    {
        var text = new HaWebSocketOptions { Token = ConnectionRig.Token }.ToString();

        Assert.DoesNotContain(ConnectionRig.Token, text);
        Assert.Contains("Endpoint", text);
    }

    [Fact]
    public void The_default_jitter_stays_within_20_percent()
    {
        var jitter = new HaWebSocketOptions().Jitter;

        for (var i = 0; i < 200; i++)
        {
            var factor = jitter();
            Assert.InRange(factor, 0.8, 1.2);
        }
    }

    [Fact]
    public async Task A_502_before_the_upgrade_is_one_Information_line_per_outage_and_the_attempts_inside_it_log_at_Debug()
    {
        await using var rig = await ConnectionRig.StartAsync(server =>
        {
            Seed(server);
            server.ScriptNext(FakeHaAttempt.Reject(502), FakeHaAttempt.Reject(502));
        });

        await rig.TimerAsync(OneSecond);
        rig.Time.Advance(OneSecond);
        await rig.TimerAsync(TimeSpan.FromSeconds(2));
        rig.Time.Advance(TimeSpan.FromSeconds(2));
        await rig.WaitForStateAsync(HaConnectionState.Connected);

        Assert.Equal(["Cannot reach Home Assistant (HTTP 502); retrying", "Connected to Home Assistant"], rig.Log.Messages(LogLevel.Information));
        Assert.Contains("Connection attempt 2 failed (HTTP 502)", rig.Log.Messages(LogLevel.Debug));
        Assert.Empty(rig.Log.Messages(LogLevel.Warning));
        Assert.Empty(rig.Log.Messages(LogLevel.Error));
    }

    [Fact]
    public async Task The_connection_state_follows_an_outage_through_Reconnecting_and_Unavailable_to_Connected_on_fake_time()
    {
        await using var rig = await ConnectionRig.StartAsync(server =>
        {
            Seed(server);
            server.ScriptNext(Enumerable.Repeat(FakeHaAttempt.Reject(502), 4).ToArray());
        });
        ConnectionVm Now() => rig.Connection.Status.ToConnectionVm(rig.Time.GetUtcNow());

        await rig.TimerAsync(OneSecond); // attempt 1 failed at t = 0
        Assert.Equal(ConnectionState.Reconnecting, Now().State);
        Assert.Null(Now().LastSyncUtc);
        rig.Time.Advance(OneSecond); // attempt 2 at t = 1
        await rig.TimerAsync(TimeSpan.FromSeconds(2));
        rig.Time.Advance(TimeSpan.FromSeconds(2)); // attempt 3 at t = 3
        await rig.TimerAsync(TimeSpan.FromSeconds(5));
        rig.Time.Advance(TimeSpan.FromSeconds(5)); // attempt 4 at t = 8
        await rig.TimerAsync(TimeSpan.FromSeconds(10)); // attempt 5 waits for t = 18

        rig.Time.Advance(TimeSpan.FromSeconds(7)); // t = 15: still the first 15 s of the outage
        Assert.Equal(ConnectionState.Reconnecting, Now().State);
        rig.Time.Advance(OneSecond); // t = 16
        Assert.Equal(ConnectionState.Unavailable, Now().State);

        rig.Time.Advance(TimeSpan.FromSeconds(2)); // t = 18: attempt 5 connects
        await rig.WaitForStateAsync(HaConnectionState.Connected);
        Assert.Equal(ConnectionState.Connected, Now().State);
        Assert.Equal(rig.Time.GetUtcNow(), Now().LastSyncUtc);
    }

    // --- liveness -----------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_ping_goes_out_every_30_seconds_and_its_pong_keeps_the_connection_alive()
    {
        await using var rig = await ConnectionRig.StartAsync(Seed);
        var session = await rig.NextSessionAsync();
        await rig.WaitForStateAsync(HaConnectionState.Connected);

        await rig.TimerAsync(ThirtySeconds);
        rig.Time.Advance(ThirtySeconds);
        using var ping = Parse(await rig.Guard(session.WaitForMessageAsync("ping"), "the first ping"));
        await rig.TimerAsync(ThirtySeconds); // the pong arrived and the next ping is scheduled

        Assert.Equal(3, ping.RootElement.GetProperty("id").GetInt32()); // after 1 (supported_features) and 2 (subscribe_entities)
        Assert.Equal(HaConnectionState.Connected, rig.Connection.Status.State);
        Assert.Equal(rig.Time.GetUtcNow(), rig.Connection.Status.LastActivityUtc); // a pong counts as activity for the 90 s rule
        Assert.Equal(0, rig.Connection.ReconnectCount);

        rig.Time.Advance(ThirtySeconds);
        using var second = Parse(await rig.Guard(session.WaitForMessageAsync("ping", 1), "the second ping"));
        Assert.Equal(4, second.RootElement.GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task A_ping_that_gets_no_pong_within_10_seconds_forces_a_reconnect()
    {
        await using var rig = await ConnectionRig.StartAsync(Seed);
        var first = await rig.NextSessionAsync();
        await rig.WaitForItemsAsync(1);
        await rig.WaitForStateAsync(HaConnectionState.Connected);
        first.AnswerPings = false;

        await rig.TimerAsync(ThirtySeconds);
        rig.Time.Advance(ThirtySeconds);
        await rig.Guard(first.WaitForMessageAsync("ping"), "the ping");
        await rig.TimerAsync(TimeSpan.FromSeconds(10)); // the wait for the pong
        Assert.Equal(HaConnectionState.Connected, rig.Connection.Status.State);
        rig.Time.Advance(TimeSpan.FromSeconds(10));

        await rig.WaitForStateAsync(HaConnectionState.Reconnecting);
        await rig.Guard(first.Ended, "the silent connection to be dropped");
        Assert.Equal(1, rig.Connection.ReconnectCount);
        Assert.Contains("no pong within 10 s", string.Join('\n', rig.Log.Messages(LogLevel.Information)));

        await rig.TimerAsync(OneSecond);
        rig.Time.Advance(OneSecond);
        var second = await rig.NextSessionAsync();
        await rig.WaitForStateAsync(HaConnectionState.Connected);
        Assert.Equal(["auth", "supported_features", "subscribe_entities"], second.ReceivedTypes);
        Assert.Equal(2, (await rig.WaitForItemsAsync(2)).Count);
    }

    // --- losing the connection ----------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_dropped_connection_reconnects_and_the_fresh_snapshot_replaces_what_was_known()
    {
        await using var rig = await ConnectionRig.StartAsync(Seed);
        var first = await rig.NextSessionAsync();
        Assert.Equal(3, Assert.IsType<HaSnapshot>((await rig.WaitForItemsAsync(1))[0]).Entities.Count);
        await rig.WaitForStateAsync(HaConnectionState.Connected);

        first.Abort(); // a scripted drop: no close handshake
        rig.Server.RemoveEntity(Fuel); // gone while the connection was down
        await rig.WaitForStateAsync(HaConnectionState.Reconnecting);
        await rig.TimerAsync(OneSecond);
        rig.Time.Advance(OneSecond);
        var second = await rig.NextSessionAsync();
        var items = await rig.WaitForItemsAsync(2);
        await rig.WaitForStateAsync(HaConnectionState.Connected);

        var replaced = Assert.IsType<HaSnapshot>(items[1]);
        Assert.Equal([King, Home], replaced.Entities.Select(entity => entity.EntityId));
        Assert.Equal(2, rig.Connection.EntityCount);
        Assert.Equal(1, rig.Connection.ReconnectCount);
        using var subscribe = Parse(await second.WaitForMessageAsync("subscribe_entities"));
        Assert.Equal(2, subscribe.RootElement.GetProperty("id").GetInt32()); // the ids count per connection
        Assert.Contains("Lost the connection to Home Assistant", string.Join('\n', rig.Log.Messages(LogLevel.Information)));
        Assert.DoesNotContain(ConnectionRig.Token, rig.Log.Text);
    }

    [Fact]
    public async Task A_close_from_Home_Assistant_reconnects_the_same_way()
    {
        await using var rig = await ConnectionRig.StartAsync(Seed);
        var first = await rig.NextSessionAsync();
        await rig.WaitForStateAsync(HaConnectionState.Connected);

        await first.CloseAsync();
        await rig.WaitForStateAsync(HaConnectionState.Reconnecting);

        Assert.Contains("closed by Home Assistant", string.Join('\n', rig.Log.Messages(LogLevel.Information)));
        await rig.TimerAsync(OneSecond);
    }

    [Fact]
    public async Task The_back_off_starts_over_only_after_a_connection_that_lasted_a_minute()
    {
        await using var rig = await ConnectionRig.StartAsync(Seed);
        var two = TimeSpan.FromSeconds(2);

        var first = await rig.NextSessionAsync();
        await rig.WaitForStateAsync(HaConnectionState.Connected);
        first.Abort();
        await rig.WaitForStateAsync(HaConnectionState.Reconnecting);
        await rig.TimerAsync(OneSecond); // a first loss waits 1 s
        rig.Time.Advance(OneSecond);

        var second = await rig.NextSessionAsync();
        await rig.WaitForStateAsync(HaConnectionState.Connected);
        second.Abort();
        await rig.WaitForStateAsync(HaConnectionState.Reconnecting);
        await rig.TimerAsync(two); // a connection that lasted no time does not reset the back-off
        rig.Time.Advance(two);

        var third = await rig.NextSessionAsync();
        await rig.WaitForStateAsync(HaConnectionState.Connected);
        for (var ping = 0; ping < 2; ping++) // 60 s of connection
        {
            await rig.TimerAsync(ThirtySeconds);
            rig.Time.Advance(ThirtySeconds);
            await rig.Guard(third.WaitForMessageAsync("ping", ping), "a ping");
        }

        await rig.TimerAsync(ThirtySeconds); // the second pong arrived
        third.Abort();
        await rig.WaitForStateAsync(HaConnectionState.Reconnecting);

        await rig.TimerAsync(OneSecond); // a minute of connection starts the back-off over
    }

    [Fact]
    public async Task A_handshake_that_gets_no_answer_times_out_after_10_seconds_and_is_retried()
    {
        var budget = new HaWebSocketOptions().HandshakeTimeout;
        Assert.Equal(TimeSpan.FromSeconds(10), budget);
        await using var rig = await ConnectionRig.StartAsync(
            server =>
            {
                Seed(server);
                server.ScriptNext(FakeHaAttempt.Silent());
            },
            configure: options => options with { HandshakeTimeout = budget });

        await rig.NextSessionAsync(); // the silent session: the websocket is open and HA says nothing
        await rig.WaitForStateAsync(HaConnectionState.Authenticating);
        await rig.TimerAsync(budget);
        rig.Time.Advance(budget);
        await rig.WaitForStateAsync(HaConnectionState.Reconnecting);

        Assert.Contains("timed out", string.Join('\n', rig.Log.Messages(LogLevel.Information)));
        await rig.TimerAsync(OneSecond);
        rig.Time.Advance(OneSecond);
        await rig.WaitForStateAsync(HaConnectionState.Connected);
        Assert.Equal(2, rig.Server.AttemptCount);
    }

    // --- stopping -----------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Stopping_closes_the_socket_with_a_normal_close()
    {
        await using var rig = await ConnectionRig.StartAsync(Seed);
        var session = await rig.NextSessionAsync();
        await rig.WaitForStateAsync(HaConnectionState.Connected);

        await rig.Connection.StopAsync(CancellationToken.None);

        await rig.Guard(session.Ended, "the connection to end");
        Assert.Equal(WebSocketCloseStatus.NormalClosure, session.ClientCloseStatus);
        Assert.Equal(0, rig.Connection.ReconnectCount); // stopping is not a loss
    }

    [Fact]
    public async Task Stopping_while_waiting_for_the_next_attempt_returns_at_once()
    {
        await using var rig = await ConnectionRig.StartAsync(server => server.ScriptNext(FakeHaAttempt.Reject(502)));
        await rig.TimerAsync(OneSecond);

        await rig.Connection.StopAsync(CancellationToken.None);

        Assert.Equal(1, rig.Server.AttemptCount);
        Assert.False(rig.Connection.Health.IsFaulted);
    }
}
