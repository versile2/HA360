using System.Text.Json;
using Realm.Domain;
using Realm.Infrastructure.Ha;
using Xunit;

namespace Realm.Ingestion.Tests;

// The compressed subscribe_entities events of research ha-addon-ingress section 4.3 and the store rules of 03 section 2.5: "a" adds, "c" changes with
// "+" (merge) and "-" (attribute deletion), "r" removes, "lc" and "lu" are float epoch seconds, frames may be arrays, and a new subscription's first
// event replaces everything. Entity ids are fictional.
public class HaEntityStoreTests
{
    private const string King = "device_tracker.life360_king";
    private const string Fuel = "sensor.wagon_fuel";

    private static readonly DateTimeOffset Now = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);

    // lc 1759200000.123 and lu 1759200050.456
    private static readonly DateTimeOffset KingChanged = new(2025, 9, 30, 2, 40, 0, 123, TimeSpan.Zero);
    private static readonly DateTimeOffset KingUpdated = new(2025, 9, 30, 2, 40, 50, 456, TimeSpan.Zero);

    private const string AddKing =
        """{"a":{"device_tracker.life360_king":{"s":"home","a":{"latitude":38.89,"longitude":-77.03,"gps_accuracy":15.2,"battery_level":80,"speed":0},"c":"01JCTX","lc":1759200000.123,"lu":1759200050.456}}}""";

    private static IReadOnlyList<HaStateChanged> Apply(HaEntityStore store, string json)
    {
        // The document is disposed at once: the store must not keep a reference into it.
        using var document = JsonDocument.Parse(json);
        return store.Apply(document.RootElement, Now);
    }

    private static IReadOnlyList<HaFeedItem> ApplyFrame(HaEntityStore store, string json)
    {
        using var document = JsonDocument.Parse(json);
        return store.ApplyFrame(document.RootElement, Now);
    }

    private static HaEntitySnapshot Get(HaEntityStore store, string entityId)
    {
        Assert.True(store.TryGet(entityId, out var snapshot));
        return snapshot;
    }

    private static HaEntityStore KingStore()
    {
        var store = new HaEntityStore();
        Apply(store, AddKing);
        return store;
    }

    [Fact]
    public void An_add_creates_the_entity_with_state_attributes_and_float_epoch_instants()
    {
        var store = new HaEntityStore();

        var changes = Apply(store, AddKing);

        var change = Assert.Single(changes);
        Assert.Equal(King, change.EntityId);
        Assert.Null(change.Previous);
        Assert.Equal(Now, change.ReceivedAt);
        var king = Get(store, King);
        Assert.Equal(king, change.New);
        Assert.Equal("home", king.State);
        Assert.Equal(KingChanged, king.LastChangedUtc);
        Assert.Equal(KingUpdated, king.LastUpdatedUtc);
        Assert.Equal(5, king.Attributes.Count);
        Assert.Equal(38.89, king.Attributes["latitude"].GetDouble());
        Assert.Equal(15.2, king.Attributes["gps_accuracy"].GetDouble());
        Assert.Equal(1, store.Count);
    }

    [Fact]
    public void An_add_without_lu_reads_lu_as_the_last_change_and_without_a_state_as_unknown()
    {
        var store = new HaEntityStore();

        Apply(store, """{"a":{"sensor.wagon_fuel":{"a":{},"lc":1759200000.123},"zone.home":{"s":"0","lc":1759200001.5}}}""");

        var fuel = Get(store, Fuel);
        Assert.Equal("unknown", fuel.State);
        Assert.Equal(KingChanged, fuel.LastChangedUtc);
        Assert.Equal(KingChanged, fuel.LastUpdatedUtc);
        Assert.Empty(fuel.Attributes);
        Assert.Equal(new DateTimeOffset(2025, 9, 30, 2, 40, 1, 500, TimeSpan.Zero), Get(store, "zone.home").LastChangedUtc);
    }

    [Fact]
    public void Epoch_values_that_are_not_usable_instants_read_as_unknown()
    {
        var store = new HaEntityStore();

        Apply(store, """{"a":{"sensor.wagon_fuel":{"s":"62","lc":"yesterday","lu":1e30}}}""");

        var fuel = Get(store, Fuel);
        Assert.Null(fuel.LastChangedUtc);
        Assert.Null(fuel.LastUpdatedUtc);
    }

    [Fact]
    public void An_add_for_a_known_entity_replaces_it_and_reports_the_previous_state()
    {
        var store = KingStore();

        var changes = Apply(store, """{"a":{"device_tracker.life360_king":{"s":"work","a":{"latitude":1.5},"lc":1759200100.0}}}""");

        var change = Assert.Single(changes);
        Assert.Equal("home", change.Previous?.State);
        Assert.Equal("work", change.New?.State);
        Assert.Equal(["latitude"], Get(store, King).Attributes.Keys);
    }

    [Fact]
    public void A_change_with_plus_merges_state_attributes_and_lu_and_keeps_the_rest()
    {
        var store = KingStore();

        var changes = Apply(store, """{"c":{"device_tracker.life360_king":{"+":{"s":"not_home","a":{"latitude":38.9,"address":"Castle Road"},"lu":1759200065.0}}}}""");

        var change = Assert.Single(changes);
        Assert.Equal("home", change.Previous?.State);
        var king = Get(store, King);
        Assert.Equal(king, change.New);
        Assert.Equal("not_home", king.State);
        Assert.Equal(38.9, king.Attributes["latitude"].GetDouble());          // merged
        Assert.Equal(-77.03, king.Attributes["longitude"].GetDouble());       // kept
        Assert.Equal("Castle Road", king.Attributes["address"].GetString()); // new key
        Assert.Equal(6, king.Attributes.Count);
        Assert.Equal(KingChanged, king.LastChangedUtc);
        Assert.Equal(new DateTimeOffset(2025, 9, 30, 2, 41, 5, TimeSpan.Zero), king.LastUpdatedUtc);
    }

    [Fact]
    public void A_change_with_lc_sets_both_instants_because_HA_sends_lc_instead_of_lu()
    {
        var store = KingStore();

        Apply(store, """{"c":{"device_tracker.life360_king":{"+":{"s":"work","lc":1759200200.25}}}}""");

        var king = Get(store, King);
        var expected = new DateTimeOffset(2025, 9, 30, 2, 43, 20, 250, TimeSpan.Zero);
        Assert.Equal(expected, king.LastChangedUtc);
        Assert.Equal(expected, king.LastUpdatedUtc);
    }

    [Fact]
    public void A_change_with_minus_deletes_the_listed_attribute_keys()
    {
        var store = KingStore();

        Apply(store, """{"c":{"device_tracker.life360_king":{"-":{"a":["speed","gps_accuracy","no_such_key"]}}}}""");

        var king = Get(store, King);
        Assert.Equal(["battery_level", "latitude", "longitude"], king.Attributes.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("home", king.State);
        Assert.Equal(KingUpdated, king.LastUpdatedUtc);
    }

    [Fact]
    public void A_diff_with_plus_and_minus_merges_and_deletes_in_one_step()
    {
        var store = KingStore();

        Apply(store, """{"c":{"device_tracker.life360_king":{"+":{"a":{"latitude":40.0,"speed":12}},"-":{"a":["battery_level"]}}}}""");

        var king = Get(store, King);
        Assert.Equal(40.0, king.Attributes["latitude"].GetDouble());
        Assert.Equal(12, king.Attributes["speed"].GetInt32());
        Assert.False(king.Attributes.ContainsKey("battery_level"));
    }

    [Fact]
    public void A_change_for_an_entity_the_store_never_saw_is_ignored()
    {
        var store = new HaEntityStore();

        var changes = Apply(store, """{"c":{"sensor.wagon_fuel":{"+":{"s":"61"}}}}""");

        Assert.Empty(changes);
        Assert.Equal(0, store.Count);
    }

    [Fact]
    public void A_removal_deletes_the_entity_and_reports_the_previous_state()
    {
        var store = KingStore();
        Apply(store, """{"a":{"sensor.wagon_fuel":{"s":"62","lc":1759200000.0}}}""");

        var changes = Apply(store, """{"r":["sensor.wagon_fuel","never.seen"]}""");

        var change = Assert.Single(changes);
        Assert.Equal(Fuel, change.EntityId);
        Assert.Null(change.New);
        Assert.Equal("62", change.Previous?.State);
        Assert.False(store.TryGet(Fuel, out _));
        Assert.Equal(1, store.Count);
    }

    [Fact]
    public void Adds_changes_and_removals_in_one_event_apply_in_that_order()
    {
        var store = KingStore();
        Apply(store, """{"a":{"sensor.wagon_fuel":{"s":"62","lc":1759200000.0}}}""");

        var changes = Apply(
            store,
            """{"r":["sensor.wagon_fuel"],"c":{"device_tracker.life360_king":{"+":{"s":"work"}}},"a":{"zone.home":{"s":"0","lc":1759200000.0}}}""");

        Assert.Equal(["zone.home", King, Fuel], changes.Select(change => change.EntityId));
    }

    [Fact]
    public void A_snapshot_handed_out_earlier_does_not_change_when_a_later_diff_arrives()
    {
        var store = KingStore();
        var before = Get(store, King);

        Apply(store, """{"c":{"device_tracker.life360_king":{"+":{"s":"work","a":{"latitude":1.0}},"-":{"a":["speed"]}}}}""");

        Assert.Equal("home", before.State);
        Assert.Equal(38.89, before.Attributes["latitude"].GetDouble());
        Assert.True(before.Attributes.ContainsKey("speed"));
        Assert.Equal(5, before.Attributes.Count);
    }

    [Fact]
    public void An_array_frame_applies_every_message_in_order_and_skips_what_is_not_an_event()
    {
        var store = KingStore();
        store.BeginSubscription(2);
        ApplyFrame(store, """{"id":2,"type":"event","event":{"a":{"device_tracker.life360_king":{"s":"home","a":{},"lc":1759200000.0}}}}"""); // the snapshot

        var items = ApplyFrame(
            store,
            """
            [
              {"id":2,"type":"event","event":{"c":{"device_tracker.life360_king":{"+":{"s":"work"}}}}},
              {"id":9,"type":"result","success":true,"result":null},
              {"id":2,"type":"event","event":{"c":{"device_tracker.life360_king":{"+":{"s":"home"}}}}},
              "not a message",
              {"id":2,"type":"event","event":{"r":["device_tracker.life360_king"]}}
            ]
            """);

        var changes = items.Cast<HaStateChanged>().ToArray();
        Assert.Equal(["work", "home", null], changes.Select(change => change.New?.State));
        Assert.Equal(["home", "work", "home"], changes.Select(change => change.Previous?.State));
        Assert.Equal(0, store.Count);
    }

    [Fact]
    public void A_frame_that_is_a_single_object_is_read_like_a_one_element_array()
    {
        var store = new HaEntityStore();
        store.BeginSubscription(7);

        var items = ApplyFrame(store, """{"id":7,"type":"event","event":{"a":{"device_tracker.life360_king":{"s":"home","a":{},"lc":1759200000.0}}}}""");

        var snapshot = Assert.IsType<HaSnapshot>(Assert.Single(items));
        Assert.Equal([King], snapshot.Entities.Select(entity => entity.EntityId));
    }

    [Fact]
    public void Events_of_another_subscription_and_events_before_any_subscription_are_ignored()
    {
        var store = new HaEntityStore();
        const string frame = """{"id":2,"type":"event","event":{"a":{"zone.home":{"s":"0","lc":1759200000.0}}}}""";

        Assert.Empty(ApplyFrame(store, frame)); // no subscription yet

        store.BeginSubscription(5);
        Assert.Empty(ApplyFrame(store, frame)); // an older subscription

        Assert.Equal(0, store.Count);
    }

    [Fact]
    public void The_first_event_of_a_subscription_replaces_everything_and_later_events_are_diffs()
    {
        var store = KingStore();
        Apply(store, """{"a":{"sensor.wagon_fuel":{"s":"62","a":{"unit_of_measurement":"%"},"lc":1759200000.0}}}""");
        store.BeginSubscription(4);

        var items = ApplyFrame(
            store,
            """
            [
              {"id":4,"type":"event","event":{"a":{"sensor.wagon_fuel":{"s":"58","lc":1759200300.0},"zone.home":{"s":"0","lc":1759200000.0}}}},
              {"id":4,"type":"event","event":{"c":{"zone.home":{"+":{"s":"1"}}}}}
            ]
            """);

        Assert.Equal(2, items.Count);
        var snapshot = Assert.IsType<HaSnapshot>(items[0]);
        Assert.Equal([Fuel, "zone.home"], snapshot.Entities.Select(entity => entity.EntityId));
        Assert.Equal("58", snapshot.Entities[0].State);
        Assert.Empty(snapshot.Entities[0].Attributes);           // nothing of the old attributes survives
        Assert.False(store.TryGet(King, out _));                  // an entity missing from the snapshot is gone
        var change = Assert.IsType<HaStateChanged>(items[1]);
        Assert.Equal("0", change.Previous?.State);
        Assert.Equal("1", change.New?.State);
        Assert.Equal(2, store.Count);
    }

    [Fact]
    public void Replace_loads_one_event_body_as_the_whole_state()
    {
        var store = KingStore();
        using var document = JsonDocument.Parse("""{"a":{"zone.home":{"s":"0","lc":1759200000.0}}}""");

        var snapshot = store.Replace(document.RootElement, Now);

        Assert.Equal(["zone.home"], snapshot.Entities.Select(entity => entity.EntityId));
        Assert.Equal(Now, snapshot.ReceivedAt);
        Assert.Equal(1, store.Count);
    }

    [Fact]
    public void Clear_empties_the_store()
    {
        var store = KingStore();

        store.Clear();

        Assert.Equal(0, store.Count);
        Assert.False(store.TryGet(King, out _));
    }

    [Fact]
    public void Frame_messages_are_the_object_itself_or_the_objects_of_an_array()
    {
        using var single = JsonDocument.Parse("""{"type":"pong"}""");
        using var several = JsonDocument.Parse("""[{"type":"a","id":1},3,{"type":"b"}]""");
        using var scalar = JsonDocument.Parse("5");

        Assert.Equal(["pong"], HaFrame.Messages(single.RootElement).Select(HaFrame.TypeOf));
        Assert.Equal(["a", "b"], HaFrame.Messages(several.RootElement).Select(HaFrame.TypeOf));
        Assert.Equal([1, null], HaFrame.Messages(several.RootElement).Select(HaFrame.IdOf));
        Assert.Empty(HaFrame.Messages(scalar.RootElement));
    }
}
