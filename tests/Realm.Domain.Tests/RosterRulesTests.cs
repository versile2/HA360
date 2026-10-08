using System.Globalization;
using Xunit;

namespace Realm.Domain.Tests;

// The roster lifecycle of D113 (02 section 2.7): the first start, a new tracker, the 7-day retirement, the notification ids and the owner's edits.
public class RosterRulesTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-08T12:00:00Z", CultureInfo.InvariantCulture);

    private static readonly IReadOnlySet<string> NoTrackers = new HashSet<string>();

    private static RosterCandidate Person(string objectId, string name = "Alden", bool active = true) =>
        new($"person.{objectId}", RosterKind.Person, name, "Home Assistant", active);

    private static RosterCandidate Tracker(string objectId, string name = "Pickup", bool active = true) =>
        new($"device_tracker.{objectId}", RosterKind.Tracker, name, "Home Assistant", active);

    private static RosterEntry Entry(string entityId, RosterGroup group, int order = 0, TimeSpan? idle = null, RosterKind? kind = null) =>
        new(entityId, kind ?? (entityId.StartsWith("person.", StringComparison.Ordinal) ? RosterKind.Person : RosterKind.Tracker), group, entityId, null, "#E8BC4E", order, "Home Assistant", Now.AddDays(-90), Now - (idle ?? TimeSpan.Zero), null);

    // ---- the first start ------------------------------------------------------------------------------------

    [Fact]
    public void First_start_puts_every_person_and_every_recent_tracker_in_People_with_one_summary()
    {
        var result = RosterRules.Reconcile(
            [],
            [Person("alden"), Person("briar", "Briar", active: false), Tracker("pickup"), Tracker("old_van", "Old van")],
            new HashSet<string> { "device_tracker.pickup" },
            Now);

        Assert.Equal(["device_tracker.pickup", "person.alden", "person.briar"], result.Entries.Where(e => e.Group == RosterGroup.People).Select(e => e.EntityId).Order(StringComparer.Ordinal));
        var summary = Assert.Single(result.Notices);
        Assert.Equal(RosterRules.SummaryNotificationId, summary.NotificationId);
        Assert.StartsWith("HA Cartographer: ", summary.Title, StringComparison.Ordinal);
        Assert.Contains("HA Cartographer → Settings → Who's on the map", summary.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void First_start_leaves_a_tracker_with_no_position_in_30_days_in_Not_tracked_without_a_notice_of_its_own()
    {
        var result = RosterRules.Reconcile([], [Person("alden"), Tracker("old_van")], NoTrackers, Now);

        var van = Assert.Single(result.Entries, e => e.EntityId == "device_tracker.old_van");
        Assert.Equal(RosterGroup.NotTracked, van.Group);
        Assert.Null(van.AutoMovedUtc);
        Assert.DoesNotContain(result.Notices, n => n.NotificationId == RosterRules.NotificationIdOf("device_tracker.old_van"));
        Assert.Single(result.Notices);
    }

    [Fact]
    public void First_start_with_nothing_found_sends_nothing()
    {
        var result = RosterRules.Reconcile([], [], NoTrackers, Now);

        Assert.Empty(result.Entries);
        Assert.Empty(result.Notices);
        Assert.False(result.VisibleChange);
    }

    [Fact]
    public void First_start_window_is_thirty_days()
    {
        Assert.Equal(TimeSpan.FromDays(30), RosterRules.FirstStartWindow);
        Assert.Equal(TimeSpan.FromDays(7), RosterRules.InactiveAfter);
    }

    [Fact]
    public void First_start_gives_each_entry_a_name_a_palette_colour_and_a_sort_order()
    {
        var result = RosterRules.Reconcile([], [Person("alden", "Alden"), Person("briar", "Briar")], NoTrackers, Now);

        var people = result.Entries.Where(e => e.Group == RosterGroup.People).ToList();
        Assert.Equal(["Alden", "Briar"], people.Select(e => e.DisplayName));
        Assert.Equal([0, 1], people.Select(e => e.SortOrder));
        Assert.All(people, e => Assert.Contains(e.Color, RosterRules.Palette));
        Assert.NotEqual(people[0].Color, people[1].Color);
    }

    // ---- a later pass ---------------------------------------------------------------------------------------

    [Fact]
    public void A_new_tracker_joins_People_with_a_notification_named_after_its_entity()
    {
        var current = new[] { Entry("person.alden", RosterGroup.People) };

        var result = RosterRules.Reconcile(current, [Person("alden"), Tracker("pixel_8", "Pixel 8")], NoTrackers, Now);

        var added = Assert.Single(result.Entries, e => e.EntityId == "device_tracker.pixel_8");
        Assert.Equal(RosterGroup.People, added.Group);
        Assert.Equal(1, added.SortOrder);
        var notice = Assert.Single(result.Notices);
        Assert.Equal("ha_cartographer_device_tracker.pixel_8", notice.NotificationId);
        Assert.StartsWith("HA Cartographer: ", notice.Title, StringComparison.Ordinal);
        Assert.Contains("HA Cartographer → Settings → Who's on the map", notice.Message, StringComparison.Ordinal);
        Assert.True(result.VisibleChange);
    }

    [Fact]
    public void A_later_pass_does_not_apply_the_30_day_rule()
    {
        var current = new[] { Entry("person.alden", RosterGroup.People) };

        var result = RosterRules.Reconcile(current, [Person("alden"), Tracker("rarely_used")], NoTrackers, Now);

        Assert.Equal(RosterGroup.People, Assert.Single(result.Entries, e => e.EntityId == "device_tracker.rarely_used").Group);
    }

    [Fact]
    public void A_pass_that_finds_nothing_new_changes_nothing_but_the_last_active_time()
    {
        var current = new[] { Entry("person.alden", RosterGroup.People, idle: TimeSpan.FromHours(2)) };

        var result = RosterRules.Reconcile(current, [Person("alden")], NoTrackers, Now);

        Assert.Empty(result.Notices);
        Assert.False(result.VisibleChange);
        Assert.Equal(Now, Assert.Single(result.Entries).LastActiveUtc);
        Assert.Equal(RosterGroup.People, Assert.Single(result.Entries).Group);
    }

    // ---- the 7-day retirement -------------------------------------------------------------------------------

    [Theory]
    [InlineData(6, false)]
    [InlineData(7, true)]
    [InlineData(30, true)]
    public void An_unavailable_tracker_moves_to_Not_tracked_after_seven_days_with_a_notification(int idleDays, bool moved)
    {
        var current = new[] { Entry("device_tracker.pickup", RosterGroup.Vehicles, idle: TimeSpan.FromDays(idleDays)) };

        var result = RosterRules.Reconcile(current, [Tracker("pickup", active: false)], NoTrackers, Now);

        var entry = Assert.Single(result.Entries);
        Assert.Equal(moved ? RosterGroup.NotTracked : RosterGroup.Vehicles, entry.Group);
        Assert.Equal(moved ? Now : (DateTimeOffset?)null, entry.AutoMovedUtc);
        if (moved)
        {
            var notice = Assert.Single(result.Notices);
            Assert.Equal("ha_cartographer_device_tracker.pickup", notice.NotificationId);
            Assert.StartsWith("HA Cartographer: ", notice.Title, StringComparison.Ordinal);
        }
        else
        {
            Assert.Empty(result.Notices);
        }
    }

    [Fact]
    public void A_tracker_removed_from_Home_Assistant_retires_the_same_way()
    {
        var current = new[] { Entry("device_tracker.gone", RosterGroup.People, idle: TimeSpan.FromDays(8)) };

        var result = RosterRules.Reconcile(current, [], NoTrackers, Now);

        Assert.Equal(RosterGroup.NotTracked, Assert.Single(result.Entries).Group);
        Assert.Single(result.Notices);
    }

    [Fact]
    public void An_entry_the_owner_put_in_Not_tracked_stays_there_and_is_not_announced_again()
    {
        var current = new[] { Entry("device_tracker.pickup", RosterGroup.NotTracked, idle: TimeSpan.FromDays(40)) };

        var result = RosterRules.Reconcile(current, [Tracker("pickup")], NoTrackers, Now);

        Assert.Equal(RosterGroup.NotTracked, Assert.Single(result.Entries).Group);
        Assert.Empty(result.Notices);
    }

    [Fact]
    public void A_retired_entry_that_is_active_again_stays_in_Not_tracked_until_the_owner_moves_it()
    {
        var retired = Entry("person.alden", RosterGroup.NotTracked) with { AutoMovedUtc = Now.AddDays(-1) };

        var result = RosterRules.Reconcile([retired], [Person("alden")], NoTrackers, Now);

        Assert.Equal(RosterGroup.NotTracked, Assert.Single(result.Entries).Group);
        Assert.Empty(result.Notices);
    }

    // ---- ids ------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("person.alden", "ha_cartographer_person.alden")]
    [InlineData("device_tracker.pixel_8", "ha_cartographer_device_tracker.pixel_8")]
    public void The_notification_id_is_stable_per_entity(string entityId, string expected)
    {
        Assert.Equal(expected, RosterRules.NotificationIdOf(entityId));
    }

    [Theory]
    [InlineData("person.alden", "alden")]
    [InlineData("device_tracker.pixel_8", "tracker_pixel_8")]
    public void The_member_id_comes_from_the_entity_id(string entityId, string expected)
    {
        Assert.Equal(expected, RosterEntry.IdOf(entityId));
    }

    // ---- the owner's edits ----------------------------------------------------------------------------------

    [Fact]
    public void Move_puts_the_entry_at_the_index_and_renumbers_both_groups()
    {
        var roster = new[]
        {
            Entry("person.a", RosterGroup.People, 0),
            Entry("person.b", RosterGroup.People, 1),
            Entry("person.c", RosterGroup.People, 2),
            Entry("device_tracker.v", RosterGroup.Vehicles, 0),
        };

        var moved = RosterRules.Move(roster, "person.c", RosterGroup.Vehicles, 0);

        Assert.Equal(["person.a", "person.b"], moved.Where(e => e.Group == RosterGroup.People).Select(e => e.EntityId));
        Assert.Equal(["person.c", "device_tracker.v"], moved.Where(e => e.Group == RosterGroup.Vehicles).Select(e => e.EntityId));
        Assert.Equal([0, 1], moved.Where(e => e.Group == RosterGroup.Vehicles).Select(e => e.SortOrder));
    }

    [Fact]
    public void Move_inside_a_group_reorders_it()
    {
        var roster = new[] { Entry("person.a", RosterGroup.People, 0), Entry("person.b", RosterGroup.People, 1), Entry("person.c", RosterGroup.People, 2) };

        var moved = RosterRules.Move(roster, "person.a", RosterGroup.People, 2);

        Assert.Equal(["person.b", "person.c", "person.a"], moved.Select(e => e.EntityId));
    }

    [Fact]
    public void Moving_out_of_Not_tracked_clears_the_automatic_mark_and_without_an_index_goes_last()
    {
        var roster = new[]
        {
            Entry("person.a", RosterGroup.People, 0),
            Entry("person.b", RosterGroup.NotTracked, 0) with { AutoMovedUtc = Now },
        };

        var moved = RosterRules.Move(roster, "person.b", RosterGroup.People, null);

        var b = Assert.Single(moved, e => e.EntityId == "person.b");
        Assert.Null(b.AutoMovedUtc);
        Assert.Equal(["person.a", "person.b"], moved.Select(e => e.EntityId));
    }

    [Fact]
    public void Move_of_an_unknown_entity_changes_nothing()
    {
        var roster = new[] { Entry("person.a", RosterGroup.People, 0) };

        Assert.Same(roster, RosterRules.Move(roster, "person.zzz", RosterGroup.Vehicles, 0));
    }

    [Fact]
    public void Update_trims_and_limits_the_name_and_title_and_checks_the_colour()
    {
        var roster = new[] { Entry("person.a", RosterGroup.People) };

        var updated = Assert.Single(RosterRules.Update(roster, "person.a", "  " + new string('x', 40) + " ", "  The King  ", "#aabbcc"));

        Assert.Equal(RosterRules.MaxNameLength, updated.DisplayName.Length);
        Assert.Equal("The King", updated.LoreTitle);
        Assert.Equal("#AABBCC", updated.Color);

        var kept = Assert.Single(RosterRules.Update(roster, "person.a", "   ", "   ", "red"));
        Assert.Equal("person.a", kept.DisplayName);
        Assert.Null(kept.LoreTitle);
        Assert.Equal("#E8BC4E", kept.Color);
    }
}
