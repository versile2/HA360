using Xunit;
using static Realm.Domain.Tests.HistoryTestData;

namespace Realm.Domain.Tests;

// Stays (place visits) of Location History (0.3.0, D123): consecutive fixes in one zone, or within about 100 m of each other when there is no zone, for at least five minutes.
public class StayDeriverTests
{
    private static IReadOnlyList<DerivedStay> Derive(IEnumerable<RawFix> fixes, IEnumerable<StatsTrip>? trips = null, DateTimeOffset? now = null) =>
        StayDeriver.Derive([.. fixes], [.. trips ?? []], Zones, now);

    [Fact]
    public void Fixes_inside_a_zone_are_one_stay_at_that_zone_from_the_first_fix_to_the_last()
    {
        var stays = Derive(Hold(At(8, 5), At(17, 42), HomeLat, HomeLon));

        var stay = Assert.Single(stays);
        Assert.Equal("home", stay.PlaceId);
        Assert.Equal(HomeLat, stay.Lat);
        Assert.Equal(HomeLon, stay.Lon);
        Assert.Equal(At(8, 5), stay.StartUtc);
        Assert.Equal(At(17, 35), stay.EndUtc);
        Assert.False(stay.IsOngoing);
    }

    [Fact]
    public void Fixes_at_no_zone_that_stay_within_a_hundred_metres_are_a_stay_with_no_place()
    {
        var stays = Derive(Hold(At(10), At(11, 30), 31.2000, -85.2000, address: "Eastgate Avenue, Pinebrook, AL"));

        var stay = Assert.Single(stays);
        Assert.Null(stay.PlaceId);
        Assert.InRange(stay.Lat, 31.2000, 31.2001);
        Assert.Equal(At(10), stay.StartUtc);
        Assert.Equal(At(11, 30), stay.EndUtc);
        Assert.Equal("Eastgate Avenue, Pinebrook, AL", stay.Address);
    }

    [Fact]
    public void A_stop_shorter_than_five_minutes_is_ignored_and_five_minutes_is_a_stay()
    {
        var shortStop = Hold(At(12), At(12, 4), 31.2000, -85.2000, stepMinutes: 2);
        var fiveMinutes = Hold(At(14), At(14, 5), 31.2000, -85.2000, stepMinutes: 1);

        Assert.Empty(Derive(shortStop));
        Assert.Single(Derive(fiveMinutes));
    }

    [Fact]
    public void Fixes_that_keep_moving_are_not_a_stay()
    {
        var walking = Enumerable.Range(0, 20).Select(i => Fix(At(9).AddMinutes(i), North(31.2000, i * 400), -85.2000)).ToList();

        Assert.Empty(Derive(walking));
    }

    [Fact]
    public void A_fix_with_a_poor_accuracy_does_not_make_or_break_a_stay()
    {
        var fixes = Hold(At(9), At(11), HomeLat, HomeLon);
        fixes.Add(Fix(At(10, 10), 31.5, -85.9, accuracy: 900));

        var stay = Assert.Single(Derive(fixes));
        Assert.Equal("home", stay.PlaceId);
        Assert.Equal(At(9), stay.StartUtc);
        Assert.Equal(At(11), stay.EndUtc);
    }

    [Fact]
    public void Two_places_with_a_drive_between_them_are_two_stays_and_the_drive_is_in_neither()
    {
        var (trip, driveFixes) = Drive(At(7, 35), At(7, 55), HomeLat, HomeLon, WorkLat, WorkLon, "home", "work");
        var fixes = Hold(At(6), At(7, 30), HomeLat, HomeLon).Concat(driveFixes).Concat(Hold(At(8), At(12), WorkLat, WorkLon)).ToList();

        var stays = Derive(fixes, [trip]);

        Assert.Equal(["home", "work"], stays.Select(stay => stay.PlaceId));
        Assert.True(stays[0].EndUtc <= trip.StartUtc.AddMinutes(1));
        Assert.True(stays[1].StartUtc >= trip.EndUtc.AddMinutes(-1));
        Assert.All(stays, stay => Assert.True(stay.EndUtc <= trip.StartUtc || stay.StartUtc >= trip.EndUtc));
    }

    [Fact]
    public void With_no_fixes_while_standing_still_the_trips_bridge_the_visit_to_the_drive_that_arrived_and_left()
    {
        // A companion app that reports only while moving: one fix on arrival, one on leaving, nothing between (a seven hour gap).
        var (arrive, arriveFixes) = Drive(At(7, 35), At(7, 55), HomeLat, HomeLon, WorkLat, WorkLon, "home", "work");
        var (leave, leaveFixes) = Drive(At(15), At(15, 20), WorkLat, WorkLon, HomeLat, HomeLon, "work", "home");
        var fixes = arriveFixes.Concat([Fix(At(7, 56), WorkLat, WorkLon)]).Concat([Fix(At(14, 59), WorkLat, WorkLon)]).Concat(leaveFixes).ToList();

        var stays = Derive(fixes, [arrive, leave]);

        var work = Assert.Single(stays, stay => stay.PlaceId == "work");
        Assert.Equal(arrive.EndUtc, work.StartUtc);
        Assert.Equal(leave.StartUtc, work.EndUtc);
    }

    [Fact]
    public void The_last_visit_is_ongoing_and_runs_to_the_clock_when_the_data_is_fresh_and_not_when_it_is_old()
    {
        var fixes = Hold(At(17), At(18), HomeLat, HomeLon);

        var fresh = Assert.Single(Derive(fixes, now: At(20)));
        var old = Assert.Single(Derive(fixes, now: At(23)));

        Assert.True(fresh.IsOngoing);
        Assert.Equal(At(20), fresh.EndUtc);
        Assert.False(old.IsOngoing);
        Assert.Equal(At(18), old.EndUtc);
    }

    [Fact]
    public void Fixes_given_in_any_order_give_the_same_stays()
    {
        var fixes = Hold(At(9), At(12), HomeLat, HomeLon);

        var forward = Derive(fixes);
        var backward = Derive(Enumerable.Reverse(fixes));

        Assert.Equal(forward, backward);
    }
}
