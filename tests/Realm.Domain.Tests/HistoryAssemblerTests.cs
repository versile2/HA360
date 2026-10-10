using Xunit;
using static Realm.Domain.Tests.HistoryTestData;

namespace Realm.Domain.Tests;

// A member's day (0.3.0, D123): the visits clipped to the local day, the drives that started in it, names that are never "unknown", the trail with its gaps, and the day's ends.
public class HistoryAssemblerTests
{
    private static readonly DateOnly Day = new(2026, 9, 29);

    private static HistoryDayVm Build(IEnumerable<RawFix> fixes, IEnumerable<StatsTrip>? trips = null, DateOnly? day = null, TimeZoneInfo? zone = null, DateTimeOffset? now = null, bool trail = true) =>
        HistoryAssembler.Build(new HistoryInput("alden", day ?? Day, zone ?? TimeZoneInfo.Utc, [.. fixes], [.. trips ?? []], Zones, now ?? At(23, 59).AddDays(5), trail));

    [Fact]
    public void A_day_is_the_visits_and_drives_in_time_order_with_the_zone_names()
    {
        var (toWork, toWorkFixes) = Drive(At(7, 35), At(7, 55), HomeLat, HomeLon, WorkLat, WorkLon, "home", "work");
        var (toHome, toHomeFixes) = Drive(At(17, 20), At(17, 42), WorkLat, WorkLon, HomeLat, HomeLon, "work", "home");
        var fixes = Hold(At(0, 1), At(7, 30), HomeLat, HomeLon)
            .Concat(toWorkFixes).Concat(Hold(At(8), At(17, 15), WorkLat, WorkLon)).Concat(toHomeFixes).Concat(Hold(At(17, 45), At(23, 45), HomeLat, HomeLon)).ToList();

        var day = Build(fixes, [toWork, toHome]);

        Assert.Equal(["stay", "drive", "stay", "drive", "stay"], day.Entries.Select(entry => entry is HistoryStay ? "stay" : "drive"));
        var stays = day.Stays.ToList();
        Assert.Equal(["Hearth Haven", "Work", "Hearth Haven"], stays.Select(stay => stay.Label));
        var drives = day.Drives.ToList();
        Assert.Equal(["Hearth Haven", "Work"], drives.Select(drive => drive.FromLabel));
        Assert.Equal(["Work", "Hearth Haven"], drives.Select(drive => drive.ToLabel));
        Assert.True(day.Recorded);
        Assert.Equal(2, day.DriveCount);
        Assert.Equal(3, day.StayCount);
        Assert.Equal(day.Entries.OrderBy(entry => entry.StartUtc).Select(entry => entry.Id), day.Entries.Select(entry => entry.Id));
        Assert.Equal(toWork.Meters + toHome.Meters, day.TotalMeters, 3);
    }

    [Fact]
    public void A_visit_that_spans_midnight_is_on_both_days_clipped_to_each()
    {
        var fixes = Hold(At(22), At(8).AddDays(1), HomeLat, HomeLon);

        var today = Build(fixes);
        var tomorrow = Build(fixes, day: Day.AddDays(1));

        var evening = Assert.Single(today.Stays);
        Assert.Equal(At(22), evening.StartUtc);
        Assert.Equal(At(0).AddDays(1), evening.EndUtc);
        Assert.False(evening.ContinuesFromPreviousDay);
        Assert.True(evening.ContinuesIntoNextDay);
        Assert.Equal(TimeSpan.FromHours(2), evening.Duration);

        var morning = Assert.Single(tomorrow.Stays);
        Assert.Equal(At(0).AddDays(1), morning.StartUtc);
        Assert.Equal(At(8).AddDays(1), morning.EndUtc);
        Assert.True(morning.ContinuesFromPreviousDay);
        Assert.False(morning.ContinuesIntoNextDay);
        Assert.Equal(evening.Id, morning.Id);
    }

    [Fact]
    public void A_drive_belongs_to_the_day_it_started_even_when_it_ends_after_midnight()
    {
        var (late, lateFixes) = Drive(At(23, 50), At(0, 20).AddDays(1), HomeLat, HomeLon, WorkLat, WorkLon, "home", "work");

        var today = Build(lateFixes, [late]);
        var tomorrow = Build(lateFixes, [late], day: Day.AddDays(1));

        Assert.Single(today.Drives);
        Assert.Empty(tomorrow.Drives);
    }

    [Fact]
    public void The_day_is_a_local_day_in_the_zone_given_not_a_utc_day()
    {
        var chicago = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");
        // 23:30 local on the 29th (04:30 UTC on the 30th) to 01:00 local on the 30th: a visit that spans the local midnight.
        var fixes = Hold(new DateTimeOffset(2026, 9, 30, 4, 30, 0, TimeSpan.Zero), new DateTimeOffset(2026, 9, 30, 6, 0, 0, TimeSpan.Zero), HomeLat, HomeLon);

        var the29th = Build(fixes, zone: chicago);
        var the30th = Build(fixes, day: Day.AddDays(1), zone: chicago);

        var before = Assert.Single(the29th.Stays);
        Assert.Equal(TimeSpan.FromMinutes(30), before.Duration);
        Assert.True(before.ContinuesIntoNextDay);
        var after = Assert.Single(the30th.Stays);
        Assert.Equal(TimeSpan.FromHours(1), after.Duration);
        Assert.True(after.ContinuesFromPreviousDay);
    }

    [Fact]
    public void A_stay_at_no_zone_is_named_by_its_town_and_never_unknown()
    {
        var withAddress = Build(Hold(At(10), At(12), 31.2000, -85.2000, address: "Eastgate Avenue, Pinebrook, AL"));
        var withNothing = Build(Hold(At(10), At(12), 31.2000, -85.2000));

        Assert.Equal("near Pinebrook", Assert.Single(withAddress.Stays).Label);
        var label = Assert.Single(withNothing.Stays).Label;
        Assert.StartsWith("near ", label);
        Assert.DoesNotContain("nknown", label);
    }

    [Fact]
    public void A_day_with_no_fixes_is_not_recorded_and_a_day_of_fixes_without_a_stay_is_recorded_and_empty()
    {
        var nothing = Build([]);
        var passing = Build([Fix(At(10), 31.5, -85.9), Fix(At(10, 2), 31.52, -85.92)]);

        Assert.False(nothing.Recorded);
        Assert.Empty(nothing.Entries);
        Assert.Null(nothing.Start);
        Assert.True(passing.Recorded);
        Assert.Empty(passing.Entries);
    }

    [Fact]
    public void The_trail_is_solid_where_fixes_are_close_dashed_where_they_are_far_and_has_a_gap_where_there_is_no_data()
    {
        var start = At(9);
        var fixes = new List<RawFix>
        {
            Fix(start, North(HomeLat, 0), HomeLon), Fix(start.AddSeconds(30), North(HomeLat, 300), HomeLon), Fix(start.AddSeconds(60), North(HomeLat, 600), HomeLon),
            Fix(start.AddMinutes(10), North(HomeLat, 6_000), HomeLon),    // ten minutes later: a dashed guess
            Fix(start.AddMinutes(10).AddSeconds(30), North(HomeLat, 6_300), HomeLon),
            Fix(start.AddMinutes(100), North(HomeLat, 12_000), HomeLon),   // an hour and a half of nothing: a gap
            Fix(start.AddMinutes(100).AddSeconds(30), North(HomeLat, 12_300), HomeLon),
        };
        var trip = new StatsTrip("alden", start, start.AddMinutes(101), 12_300, TripQuality.Dense, DistanceBasis.Gps, 20, null, null, 0, 0, null, null, null, null, HomeLat, HomeLon, North(HomeLat, 12_300), HomeLon);

        var day = Build(fixes, [trip]);

        var drive = Assert.Single(day.Drives);
        Assert.All(day.Trail, segment => Assert.Equal(drive.Id, segment.EntryId));
        Assert.Equal([false, true, false, false], day.Trail.Select(segment => segment.Dashed));
        Assert.Equal(3, day.Trail[0].Points.Count);
        Assert.All(day.Trail, segment => Assert.True(segment.Points.Count >= 2));
        // The hour and a half is not joined at all: the last point of the third piece is not the first of the fourth.
        Assert.NotEqual(day.Trail[2].Points[^1], day.Trail[3].Points[0]);
    }

    [Fact]
    public void A_coarse_trip_without_a_path_is_two_ends_joined_by_a_dashed_line()
    {
        var trip = new StatsTrip("alden", At(9), At(9, 30), 9_000, TripQuality.Coarse, DistanceBasis.Gps, null, null, null, null, null, null, null, null, null, HomeLat, HomeLon, WorkLat, WorkLon);

        var day = Build([], [trip]);

        var segment = Assert.Single(day.Trail);
        Assert.True(segment.Dashed);
        Assert.Equal([HomeLon, HomeLat], segment.Points[0]);
        Assert.Equal([WorkLon, WorkLat], segment.Points[1]);
        Assert.True(Assert.Single(day.Drives).Coarse);
        Assert.Null(day.Drives.Single().TopSpeedMps);
    }

    [Fact]
    public void Without_the_trail_a_day_is_just_the_entries()
    {
        var (trip, driveFixes) = Drive(At(8), At(8, 20), HomeLat, HomeLon, WorkLat, WorkLon, "home", "work");

        var day = Build(driveFixes, [trip], trail: false);

        Assert.Single(day.Drives);
        Assert.Empty(day.Trail);
    }

    [Fact]
    public void The_two_ends_of_the_day_are_where_it_began_and_where_it_ended()
    {
        var (trip, driveFixes) = Drive(At(8), At(8, 20), HomeLat, HomeLon, WorkLat, WorkLon, "home", "work");
        var fixes = Hold(At(0, 5), At(7, 55), HomeLat, HomeLon).Concat(driveFixes).Concat(Hold(At(8, 25), At(18), WorkLat, WorkLon)).ToList();

        var day = Build(fixes, [trip]);

        Assert.Equal(HomeLat, day.Start!.Lat);
        Assert.Equal(WorkLat, day.End!.Lat);
        Assert.Equal(day.Entries[^1].EndUtc, day.End.Time);
    }
}
