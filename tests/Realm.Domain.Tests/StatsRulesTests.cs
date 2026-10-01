using Xunit;

namespace Realm.Domain.Tests;

// The weekly statistics rules of 02 section 6.1 to 6.8: what is counted, null versus 0 (6.3), the like-for-like
// comparator and the trend (6.4), coverage (6.5), the report fields (6.6), the driver week (6.7) and top speed
// ties (6.8). The clock is the fixture clock of 6.1, Wednesday 2026-09-30 21:25 CDT, with weeks starting Monday
// in America/Chicago, unless a test says otherwise. All members and numbers are fictional.
public class StatsRulesTests
{
    private static readonly TimeZoneInfo Chicago = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");

    // Dates before 2026-11-01 are CDT, UTC-5.
    private static DateTimeOffset Local(int month, int day, int hour = 0, int minute = 0, int second = 0) =>
        new(2026, month, day, hour, minute, second, TimeSpan.FromHours(-5));

    private static readonly DateTimeOffset Now = Local(9, 30, 21, 25);
    private static readonly DateTimeOffset LongAgo = Local(8, 1);

    private static StatsMember Alden(DateTimeOffset? since = null) => new("alden", "Alden", true, since ?? LongAgo);

    private static StatsMember Bree(DateTimeOffset? since = null) => new("bree", "Bree", false, since ?? LongAgo);

    private static StatsMember Cade(DateTimeOffset? since = null) => new("cade", "Cade", true, since ?? LongAgo);

    private static StatsTrip Dense(
        string member,
        DateTimeOffset start,
        double meters = 10_000,
        double? top = 20,
        int? speeding = 0,
        int? phone = null,
        DateTimeOffset? topAt = null,
        string? topStreet = null,
        DistanceBasis basis = DistanceBasis.Gps,
        string? fromPlace = null,
        string? toPlace = null,
        string? fromStreet = null,
        string? toStreet = null) =>
        new(
            member,
            start,
            start.AddMinutes(20),
            meters,
            TripQuality.Dense,
            basis,
            top,
            top is null ? null : topAt ?? start.AddMinutes(10),
            topStreet,
            speeding,
            phone,
            fromPlace,
            toPlace,
            fromStreet,
            toStreet);

    private static StatsTrip Coarse(string member, DateTimeOffset start, double meters = 5_000) =>
        new(member, start, start.AddMinutes(10), meters, TripQuality.Coarse, DistanceBasis.Gps, null, null, null, null, null, null, null, null, null);

    private static WeekReportVm Report(int week, IReadOnlyList<StatsMember> members, params StatsTrip[] trips) =>
        StatsRules.WeekReport(Now, DayOfWeek.Monday, Chicago, week, members, trips);

    private static DriverSummary Summary(WeekReportVm report, string memberId) => Assert.Single(report.Drivers, d => d.MemberId == memberId);

    // ---- 6.1 and 6.2: what is counted --------------------------------------------------------------------------

    [Fact]
    public void The_report_carries_the_week_bounds_and_the_four_event_kinds()
    {
        var current = Report(0, [Alden()]);
        var previous = Report(1, [Alden()]);

        Assert.Equal(Local(9, 28), current.Start);
        Assert.Equal(Local(10, 4, 23, 59, 59), current.End);
        Assert.True(current.IsCurrent);
        Assert.False(previous.IsCurrent);
        Assert.Equal(Local(9, 21), previous.Start);
        Assert.Equal(new[] { "speeding", "phone", "accel", "braking" }, current.Events.Keys);
        Assert.Equal(DistanceBasis.Gps, current.DistanceBasis);
    }

    [Fact]
    public void A_trip_belongs_to_the_week_of_its_local_start()
    {
        var trips = new[]
        {
            Dense("alden", Local(9, 28, 0, 0, 0)),     // the first second of week 0
            Dense("alden", Local(10, 4, 23, 59, 59)),  // the last second of week 0, ending in the next week
            Dense("alden", Local(9, 27, 23, 59, 59)),  // the last second of week 1
            Dense("alden", Local(10, 5, 0, 0, 0)),     // the first second of the week after the current one
        };

        Assert.Equal(2, Summary(Report(0, [Alden()], trips), "alden").Drives);
        Assert.Equal(1, Summary(Report(1, [Alden()], trips), "alden").Drives);
    }

    [Fact]
    public void T19_a_trip_that_starts_on_saturday_night_belongs_to_that_local_week_not_the_utc_one()
    {
        // Sat 2026-10-31 22:00 CDT is 2026-11-01 03:00Z, a Sunday in UTC. With Sunday weeks it is in the week of Oct 25.
        var start = new DateTimeOffset(2026, 11, 1, 3, 0, 0, TimeSpan.Zero);
        var trip = Dense("alden", start) with { EndUtc = start.AddHours(6.5) };
        var wednesday = new DateTimeOffset(2026, 11, 4, 18, 0, 0, TimeSpan.Zero);

        var thisWeek = StatsRules.WeekReport(wednesday, DayOfWeek.Sunday, Chicago, 0, [Alden()], [trip]);
        var lastWeek = StatsRules.WeekReport(wednesday, DayOfWeek.Sunday, Chicago, 1, [Alden()], [trip]);

        Assert.Equal(0, Summary(thisWeek, "alden").Drives);
        Assert.Equal(1, Summary(lastWeek, "alden").Drives);
    }

    [Fact]
    public void Trips_of_other_members_and_of_other_weeks_are_ignored()
    {
        var report = Report(
            0,
            [Alden()],
            Dense("alden", Local(9, 29, 8)),
            Dense("stranger", Local(9, 29, 9)),
            Dense("alden", Local(9, 10, 8)));

        Assert.Equal(1, Summary(report, "alden").Drives);
        Assert.Equal(1, report.Totals.Drives);
    }

    [Fact]
    public void Meters_are_summed_exactly_and_not_rounded()
    {
        // Three trips of 0.14 mi (225.30816 m): each would show as 0.1 mi, together they are 0.42 mi.
        var trip = 0.14 * 1609.344;
        var report = Report(
            0,
            [Alden(), Bree()],
            Dense("alden", Local(9, 28, 8), meters: trip),
            Dense("alden", Local(9, 29, 8), meters: trip),
            Dense("alden", Local(9, 30, 8), meters: trip),
            Dense("bree", Local(9, 29, 9), meters: 1000.5));

        Assert.Equal(0.42 * 1609.344, Summary(report, "alden").Meters ?? double.NaN, 1e-9);
        Assert.Equal((0.42 * 1609.344) + 1000.5, report.Totals.Meters, 1e-9);
        Assert.Equal(4, report.Totals.Drives);
    }

    [Fact]
    public void Drivers_are_in_card_order_drives_then_meters_then_name()
    {
        var report = Report(
            0,
            [Cade(), Bree(), Alden()],
            Dense("cade", Local(9, 28, 8), meters: 9000),
            Dense("bree", Local(9, 28, 9), meters: 9000),
            Dense("alden", Local(9, 28, 10), meters: 9000),
            Dense("alden", Local(9, 29, 10), meters: 100),
            Dense("bree", Local(9, 29, 9), meters: 50));

        // Alden and Bree: 2 drives each; Alden has more metres. Cade: 1 drive.
        Assert.Equal(new[] { "alden", "bree", "cade" }, report.Drivers.Select(d => d.MemberId));
    }

    [Fact]
    public void Equal_drives_and_meters_are_ordered_by_display_name()
    {
        var report = Report(
            0,
            [Cade(), Bree(), Alden()],
            Dense("cade", Local(9, 28, 8), meters: 9000),
            Dense("bree", Local(9, 28, 9), meters: 9000),
            Dense("alden", Local(9, 28, 10), meters: 9000));

        Assert.Equal(new[] { "alden", "bree", "cade" }, report.Drivers.Select(d => d.MemberId));
    }

    [Fact]
    public void A_driver_row_of_the_6_6_example_comes_out_of_its_trips()
    {
        // 22 drives, 151 922.07 m, speeding 6, phone 60: events total 66, no coarse trips so no pill asterisk.
        var trips = new List<StatsTrip>();
        for (var i = 0; i < 22; i++)
        {
            trips.Add(Dense(
                "alden",
                Local(9, 28, 1).AddHours(i * 7),
                meters: i < 21 ? 6900.00 : 7022.07,
                speeding: i < 6 ? 1 : 0,
                phone: i < 6 ? 10 : 0));
        }

        var row = Summary(Report(0, [Alden()], [.. trips]), "alden");

        Assert.Equal(22, row.Drives);
        Assert.Equal(151_922.07, row.Meters ?? double.NaN, 1e-6);
        Assert.Equal(6, row.Events["speeding"]);
        Assert.Equal(60, row.Events["phone"]);
        Assert.Null(row.Events["accel"]);
        Assert.Null(row.Events["braking"]);
        Assert.Equal(66, row.EventsTotal);
        Assert.False(row.EventsPartial);
        Assert.True(row.Covered);
        Assert.Null(row.CoverageStartUtc);
        Assert.Equal(0, row.CoarseTrips);
        Assert.True(row.PhoneCapable);
        Assert.Equal(DistanceBasis.Gps, row.DistanceBasis);
    }

    // ---- 6.3 null versus 0 (table-driven) ---------------------------------------------------------------------------------

    // The trips of one member in week 0 for each scenario of the table below.
    private static StatsTrip[] ScenarioTrips(string scenario) => scenario switch
    {
        "none" => [],
        "dense" => [Dense("m", Local(9, 28, 8), speeding: 1, phone: 2), Dense("m", Local(9, 29, 8), speeding: 0, phone: 0)],
        "phone-predates-sensor" => [Dense("m", Local(9, 28, 8), speeding: 1, phone: null), Dense("m", Local(9, 29, 8), speeding: 0, phone: 2)],
        "phone-zero-and-null" => [Dense("m", Local(9, 28, 8), speeding: 0, phone: 0), Dense("m", Local(9, 29, 8), speeding: 0, phone: null)],
        "coarse-only" => [Coarse("m", Local(9, 28, 8)), Coarse("m", Local(9, 29, 8))],
        "coarse-and-dense" => [Coarse("m", Local(9, 28, 8)), Dense("m", Local(9, 29, 8), speeding: 2, phone: 3)],
        "speeding-unknown-on-one-trip" => [Dense("m", Local(9, 28, 8), speeding: null, phone: 1), Dense("m", Local(9, 29, 8), speeding: 4, phone: 1)],
        "speeding-unknown-on-all" => [Dense("m", Local(9, 28, 8), speeding: null, phone: 1)],
        _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
    };

    [Theory]
    [InlineData("none", true, 0, 0, 0)]                       // covered, no trips: real zeros, phone 0 only if capable
    [InlineData("none", false, 0, null, 0)]
    [InlineData("dense", true, 1, 2, 3)]
    [InlineData("dense", false, 1, null, 1)]                   // not phone-capable: phone null for every week
    [InlineData("phone-predates-sensor", true, 1, null, 1)]    // strict phone: one trip without a count makes the week null
    [InlineData("phone-zero-and-null", true, 0, null, 0)]      // a 0 does not hide the null
    [InlineData("coarse-only", true, null, null, null)]        // coarse trips have no speeding or phone data
    [InlineData("coarse-only", false, null, null, null)]
    [InlineData("coarse-and-dense", true, 2, 3, 5)]            // coarse trips are skipped, not counted as 0
    [InlineData("coarse-and-dense", false, 2, null, 2)]
    [InlineData("speeding-unknown-on-one-trip", true, 4, 2, 6)]  // lenient speeding: the sum of the counts that exist
    [InlineData("speeding-unknown-on-all", true, null, 1, 1)]
    public void Null_versus_0_for_one_driver(string scenario, bool phoneCapable, int? speeding, int? phone, int? eventsTotal)
    {
        var member = new StatsMember("m", "M", phoneCapable, LongAgo);

        var summary = Summary(Report(0, [member], ScenarioTrips(scenario)), "m");

        Assert.True(summary.Covered);
        Assert.Equal(speeding, summary.Events["speeding"]);
        Assert.Equal(phone, summary.Events["phone"]);
        Assert.Null(summary.Events["accel"]);
        Assert.Null(summary.Events["braking"]);
        Assert.Equal(eventsTotal, summary.EventsTotal);
        Assert.Equal(phoneCapable, summary.PhoneCapable);
    }

    [Fact]
    public void A_covered_driver_with_no_trips_has_zero_drives_zero_metres_and_no_top_speed()
    {
        var report = Report(0, [Alden()]);

        var summary = Summary(report, "alden");

        Assert.Equal(0, summary.Drives);
        Assert.Equal(0.0, summary.Meters);
        Assert.Equal(0, summary.CoarseTrips);
        Assert.False(summary.EventsPartial);
        Assert.Null(report.TopSpeed);
        Assert.Equal(0, report.Totals.Drives);
        Assert.Equal(0.0, report.Totals.Meters);
    }

    [Fact]
    public void Coarse_trips_count_in_drives_and_metres_but_not_in_speeding_or_top_speed()
    {
        var report = Report(
            0,
            [Alden()],
            Dense("alden", Local(9, 28, 8), meters: 8000, top: 25, speeding: 1, phone: 0),
            Coarse("alden", Local(9, 29, 8), meters: 3000),
            Coarse("alden", Local(9, 30, 8), meters: 2000));

        var summary = Summary(report, "alden");

        Assert.Equal(3, summary.Drives);
        Assert.Equal(13_000.0, summary.Meters);
        Assert.Equal(2, summary.CoarseTrips);
        Assert.True(summary.EventsPartial);
        Assert.Equal(1, summary.Events["speeding"]);
        Assert.Equal(25.0, report.TopSpeed?.SpeedMps);
    }

    [Fact]
    public void A_coarse_trip_never_supplies_a_top_speed_even_if_its_row_has_one()
    {
        var report = Report(
            0,
            [Alden()],
            Coarse("alden", Local(9, 28, 8)) with { TopSpeedMps = 99, TopSpeedAtUtc = Local(9, 28, 8) });

        Assert.Null(report.TopSpeed);
    }

    [Fact]
    public void Accel_and_braking_are_null_for_everyone_with_no_source()
    {
        var report = Report(0, [Alden(), Bree()], Dense("alden", Local(9, 28, 8), phone: 1), Dense("bree", Local(9, 28, 9)));

        foreach (var key in new[] { "accel", "braking" })
        {
            var stat = report.Events[key];
            Assert.Null(stat.Total);
            Assert.Null(stat.ComparatorTotal);
            Assert.Null(stat.TrendDelta);
            Assert.Equal(StatSource.Derived, stat.Source);
            Assert.Equal(EventAvailability.None, stat.Availability);
            Assert.False(stat.Partial);
            Assert.Null(stat.Note);
            Assert.All(stat.Drivers, d => Assert.Null(d.Count));
            Assert.Equal(2, stat.Drivers.Count);
        }
    }

    [Fact]
    public void The_speeding_note_says_it_is_sampled_and_nothing_else_has_a_note()
    {
        var report = Report(0, [Alden()]);

        Assert.Equal("Counted from ~42 s samples; short bursts are missed", report.Events["speeding"].Note);
        Assert.Equal(StatsRules.SpeedingNote, report.Events["speeding"].Note);
        Assert.Null(report.Events["phone"].Note);
    }

    // ---- 6.2 and 6.3: availability, the chip asterisk and the pill asterisk -------------------------------------

    [Fact]
    public void The_phone_chip_has_an_asterisk_when_only_some_drivers_have_a_count()
    {
        var report = Report(
            0,
            [Alden(), Bree()],
            Dense("alden", Local(9, 28, 8), speeding: 1, phone: 4),
            Dense("bree", Local(9, 28, 9), speeding: 2, phone: null));

        var phone = report.Events["phone"];
        Assert.Equal(4, phone.Total);                       // the sum of the non-null counts
        Assert.Equal(EventAvailability.Some, phone.Availability);
        Assert.True(phone.Partial);
        Assert.Equal(new[] { "alden", "bree" }, phone.Drivers.Select(d => d.MemberId));
        Assert.Equal(4, phone.Drivers[0].Count);
        Assert.Null(phone.Drivers[1].Count);

        var speeding = report.Events["speeding"];
        Assert.Equal(3, speeding.Total);
        Assert.Equal(EventAvailability.All, speeding.Availability);
        Assert.False(speeding.Partial);
    }

    [Fact]
    public void The_phone_chip_has_no_asterisk_when_every_driver_can_record_it()
    {
        var report = Report(
            0,
            [Alden(), Cade()],
            Dense("alden", Local(9, 28, 8), phone: 4),
            Dense("cade", Local(9, 28, 9), phone: 0));

        Assert.False(report.Events["phone"].Partial);
        Assert.Equal(4, report.Events["phone"].Total);
    }

    [Fact]
    public void A_driver_with_only_coarse_trips_makes_the_speeding_chip_partial()
    {
        var report = Report(
            0,
            [Alden(), Bree()],
            Dense("alden", Local(9, 28, 8), speeding: 2),
            Coarse("bree", Local(9, 28, 9)));

        Assert.True(report.Events["speeding"].Partial);
        Assert.Equal(2, report.Events["speeding"].Total);
    }

    [Fact]
    public void A_key_that_no_driver_has_a_count_for_has_no_total_and_no_asterisk()
    {
        var report = Report(0, [Bree()], Dense("bree", Local(9, 28, 9), phone: null));

        var phone = report.Events["phone"];
        Assert.Null(phone.Total);
        Assert.False(phone.Partial);
    }

    [Fact]
    public void The_pill_asterisk_follows_coarse_trips_only_and_a_null_phone_count_never_sets_it()
    {
        var report = Report(
            0,
            [Alden(), Bree(), Cade()],
            Dense("alden", Local(9, 28, 8), speeding: 1, phone: null),    // phone null: absence, not partial
            Dense("bree", Local(9, 28, 9)),
            Dense("cade", Local(9, 28, 10), speeding: 1, phone: 1),
            Coarse("cade", Local(9, 29, 10)));

        Assert.False(Summary(report, "alden").EventsPartial);
        Assert.False(Summary(report, "bree").EventsPartial);
        Assert.True(Summary(report, "cade").EventsPartial);
        Assert.Equal(1, Summary(report, "cade").CoarseTrips);
    }

    [Fact]
    public void The_events_total_is_the_sum_of_the_counts_that_exist_and_null_when_there_are_none()
    {
        var report = Report(
            0,
            [Alden(), Bree(), Cade()],
            Dense("alden", Local(9, 28, 8), speeding: 6, phone: 60),
            Dense("bree", Local(9, 28, 9), speeding: 3, phone: null),
            Coarse("cade", Local(9, 28, 10)));

        Assert.Equal(66, Summary(report, "alden").EventsTotal);
        Assert.Equal(3, Summary(report, "bree").EventsTotal);
        Assert.Null(Summary(report, "cade").EventsTotal);
    }

    [Fact]
    public void The_distance_basis_is_mixed_when_the_trips_differ()
    {
        var gps = Report(0, [Alden(), Bree()], Dense("alden", Local(9, 28, 8)), Dense("bree", Local(9, 28, 9)));
        var mixed = Report(
            0,
            [Alden(), Bree()],
            Dense("alden", Local(9, 28, 8), basis: DistanceBasis.Odometer),
            Dense("alden", Local(9, 29, 8)),
            Dense("bree", Local(9, 28, 9), basis: DistanceBasis.Odometer));

        Assert.Equal(DistanceBasis.Gps, gps.DistanceBasis);
        Assert.Equal(DistanceBasis.Mixed, mixed.DistanceBasis);
        Assert.Equal(DistanceBasis.Mixed, Summary(mixed, "alden").DistanceBasis);
        Assert.Equal(DistanceBasis.Odometer, Summary(mixed, "bree").DistanceBasis);
    }

    // ---- 6.4 comparator and trend ----------------------------------------------------------------------------------------

    [Fact]
    public void An_older_week_is_compared_with_the_whole_week_before_it()
    {
        var window = StatsRules.ComparatorWindow(Now, DayOfWeek.Monday, Chicago, 1);

        Assert.Equal(Local(9, 14), window.StartUtc);
        Assert.Equal(Local(9, 21), window.EndUtc);
        var oldest = StatsRules.ComparatorWindow(Now, DayOfWeek.Monday, Chicago, 3);
        Assert.Equal(Local(8, 31), oldest.StartUtc);
        Assert.Equal(Local(9, 7), oldest.EndUtc);
    }

    [Fact]
    public void The_current_week_is_compared_like_for_like_with_the_same_elapsed_local_time_of_the_week_before()
    {
        // Now is Wednesday 21:25, 2 days 21 h 25 min after Monday 00:00: the window is Sep 21 00:00 to Sep 23 21:25 CDT.
        var window = StatsRules.ComparatorWindow(Now, DayOfWeek.Monday, Chicago, 0);

        Assert.Equal(new DateTimeOffset(2026, 9, 21, 5, 0, 0, TimeSpan.Zero), window.StartUtc);
        Assert.Equal(new DateTimeOffset(2026, 9, 24, 2, 25, 0, TimeSpan.Zero), window.EndUtc);
    }

    [Fact]
    public void The_like_for_like_window_counts_wall_clock_across_the_fall_back_change()
    {
        // Sun 2026-11-01 12:00 CST is 6 days 12 h after Monday 00:00 on the wall clock, but 6 days 13 h of elapsed
        // time (the clock went back at 02:00 CDT). The window ends at Sunday 12:00 CDT of the week before.
        var now = new DateTimeOffset(2026, 11, 1, 18, 0, 0, TimeSpan.Zero);

        var window = StatsRules.ComparatorWindow(now, DayOfWeek.Monday, Chicago, 0);

        Assert.Equal(new DateTimeOffset(2026, 10, 19, 5, 0, 0, TimeSpan.Zero), window.StartUtc);
        Assert.Equal(new DateTimeOffset(2026, 10, 25, 17, 0, 0, TimeSpan.Zero), window.EndUtc);
    }

    [Fact]
    public void The_like_for_like_window_counts_wall_clock_across_the_spring_forward_change()
    {
        // Sun 2027-03-14 12:00 CDT is 6 days 12 h after Monday 00:00 CST on the wall clock, but only 6 days 11 h have
        // elapsed (the clock went forward at 02:00). The window ends at Sunday 12:00 CST of the week before.
        var now = new DateTimeOffset(2027, 3, 14, 17, 0, 0, TimeSpan.Zero);

        var window = StatsRules.ComparatorWindow(now, DayOfWeek.Monday, Chicago, 0);

        Assert.Equal(new DateTimeOffset(2027, 3, 1, 6, 0, 0, TimeSpan.Zero), window.StartUtc);
        Assert.Equal(new DateTimeOffset(2027, 3, 7, 18, 0, 0, TimeSpan.Zero), window.EndUtc);
    }

    [Fact]
    public void The_trend_of_the_current_week_uses_only_the_like_for_like_part_of_the_week_before()
    {
        var report = Report(
            0,
            [Alden(), Bree()],
            Dense("alden", Local(9, 29, 8), speeding: 3, phone: 4),
            Dense("bree", Local(9, 29, 9), speeding: 1),
            Dense("alden", Local(9, 22, 8), speeding: 2, phone: 6),     // week 1, before Wed 21:25: in the window
            Dense("bree", Local(9, 22, 9), speeding: 4),
            Dense("alden", Local(9, 23, 22), speeding: 5, phone: 9),    // Wed 22:00: after the window ends
            Dense("alden", Local(9, 24, 8), speeding: 7, phone: 7));    // Thursday: outside as well

        var speeding = report.Events["speeding"];
        Assert.Equal(4, speeding.Total);
        Assert.Equal(6, speeding.ComparatorTotal);     // 2 + 4, not 2 + 5 + 7 + 4
        Assert.Equal(-2, speeding.TrendDelta);
        Assert.Equal(new EventDriverCount("alden", 3, 2), speeding.Drivers[0]);
        Assert.Equal(new EventDriverCount("bree", 1, 4), speeding.Drivers[1]);

        // Phone: only Alden can record it; the comparator total is over the same drivers as the total.
        var phone = report.Events["phone"];
        Assert.Equal(4, phone.Total);
        Assert.Equal(6, phone.ComparatorTotal);
        Assert.Equal(-2, phone.TrendDelta);
    }

    [Fact]
    public void The_trend_of_an_older_week_uses_the_whole_week_before_it()
    {
        var report = Report(
            1,
            [Alden()],
            Dense("alden", Local(9, 22, 8), speeding: 5),     // week 1
            Dense("alden", Local(9, 15, 8), speeding: 2),     // week 2
            Dense("alden", Local(9, 20, 23), speeding: 3),    // week 2, the last evening
            Dense("alden", Local(9, 29, 8), speeding: 9));    // week 0: not part of this comparison

        var speeding = report.Events["speeding"];
        Assert.Equal(5, speeding.Total);
        Assert.Equal(5, speeding.ComparatorTotal);
        Assert.Equal(0, speeding.TrendDelta);        // equal totals are flat, not null
    }

    [Fact]
    public void The_example_of_6_6_phone_total_60_comparator_71_trend_minus_11_with_an_asterisk()
    {
        var report = Report(
            0,
            [Alden(), Bree()],
            Dense("alden", Local(9, 28, 8), phone: 20),
            Dense("alden", Local(9, 29, 8), phone: 40),
            Dense("bree", Local(9, 29, 9), phone: null),
            Dense("alden", Local(9, 21, 8), phone: 31),
            Dense("alden", Local(9, 22, 8), phone: 40));

        var phone = report.Events["phone"];
        Assert.Equal(60, phone.Total);
        Assert.Equal(71, phone.ComparatorTotal);
        Assert.Equal(-11, phone.TrendDelta);
        Assert.True(phone.Partial);
        Assert.Equal(EventAvailability.Some, phone.Availability);
    }

    [Fact]
    public void A_driver_whose_comparator_window_starts_before_recording_began_is_left_out_of_the_trend()
    {
        // Cade began recording on Sep 22, after the comparator window of week 0 started (Sep 21): no comparator.
        var cade = Cade(since: Local(9, 22, 12));
        var report = Report(
            0,
            [Alden(), cade],
            Dense("alden", Local(9, 29, 8), speeding: 5),
            Dense("alden", Local(9, 22, 8), speeding: 3),
            Dense("cade", Local(9, 29, 9), speeding: 10),
            Dense("cade", Local(9, 22, 13), speeding: 9));    // after Cade's recording start, but the window began earlier

        var speeding = report.Events["speeding"];
        Assert.Equal(15, speeding.Total);
        Assert.Null(speeding.ComparatorTotal);        // not every driver of the total has comparator data
        Assert.Equal(2, speeding.TrendDelta);         // Alden alone: 5 - 3, never 15 - 3
        Assert.Equal(new EventDriverCount("alden", 5, 3), speeding.Drivers.Single(d => d.MemberId == "alden"));
        Assert.Equal(new EventDriverCount("cade", 10, null), speeding.Drivers.Single(d => d.MemberId == "cade"));
    }

    [Fact]
    public void The_trend_is_null_when_no_driver_has_a_value_in_both_periods()
    {
        var report = Report(0, [Cade(since: Local(9, 25))], Dense("cade", Local(9, 29, 9), speeding: 4));

        var speeding = report.Events["speeding"];
        Assert.Equal(4, speeding.Total);
        Assert.Null(speeding.ComparatorTotal);
        Assert.Null(speeding.TrendDelta);
    }

    [Fact]
    public void A_driver_recorded_for_the_whole_comparator_window_with_no_trips_has_a_comparator_of_zero()
    {
        var report = Report(0, [Alden()], Dense("alden", Local(9, 29, 8), speeding: 4));

        var speeding = report.Events["speeding"];
        Assert.Equal(0, speeding.ComparatorTotal);
        Assert.Equal(4, speeding.TrendDelta);
    }

    // ---- 6.5 coverage and partial weeks -----------------------------------------------------------------------------------

    [Fact]
    public void A_driver_is_covered_when_recording_began_before_the_week_ended()
    {
        // Week 1 ends at Mon Sep 28 00:00 CDT, exclusive.
        var justBefore = Report(1, [Alden(since: Local(9, 27, 23, 59, 59))]);
        var atTheEnd = Report(1, [Alden(since: Local(9, 28))]);

        Assert.True(Summary(justBefore, "alden").Covered);
        Assert.False(Summary(atTheEnd, "alden").Covered);
        Assert.Equal(WeekCoverage.NoRecord, atTheEnd.Coverage);
    }

    [Fact]
    public void A_week_nobody_was_recorded_in_is_no_record_with_nothing_counted()
    {
        var report = Report(
            2,
            [Alden(since: Local(9, 25)), Bree(since: null) with { RecordingStart = null }],
            Dense("alden", Local(9, 29, 8)));

        Assert.Equal(WeekCoverage.NoRecord, report.Coverage);
        Assert.All(report.Drivers, d => Assert.False(d.Covered));
        Assert.All(report.Drivers, d => Assert.Null(d.Drives));
        Assert.All(report.Drivers, d => Assert.Null(d.Meters));
        Assert.All(report.Drivers, d => Assert.All(d.Events.Values, v => Assert.Null(v)));
        Assert.All(report.Drivers, d => Assert.Null(d.EventsTotal));
        Assert.Equal(0, report.Totals.Drives);
        Assert.Equal(0.0, report.Totals.Meters);
        Assert.Null(report.TopSpeed);
        Assert.All(report.Events.Values, e => Assert.Null(e.Total));
    }

    [Fact]
    public void A_week_recorded_only_in_part_is_partial_and_the_driver_says_when_recording_began()
    {
        var since = Local(9, 30, 9);   // Wednesday morning of week 0
        var report = Report(
            0,
            [Alden(), Bree(since)],
            Dense("alden", Local(9, 28, 8)),
            Dense("bree", Local(9, 30, 10)));

        Assert.Equal(WeekCoverage.Partial, report.Coverage);
        Assert.Equal(since, Summary(report, "bree").CoverageStartUtc);
        Assert.Null(Summary(report, "alden").CoverageStartUtc);
        Assert.True(Summary(report, "bree").Covered);
        Assert.Equal(1, Summary(report, "bree").Drives);
    }

    [Fact]
    public void A_week_recorded_throughout_is_full()
    {
        var report = Report(0, [Alden(), Bree(Local(9, 28))]);

        Assert.Equal(WeekCoverage.Full, report.Coverage);
    }

    [Fact]
    public void Real_zeros_are_only_reported_for_covered_drivers()
    {
        // Bree has not been recorded in week 2: her counts are null, not 0, while Alden's are zeros.
        var report = Report(2, [Alden(), Bree(Local(9, 21))]);

        Assert.Equal(0, Summary(report, "alden").Drives);
        Assert.Equal(0, Summary(report, "alden").Events["speeding"]);
        Assert.Null(Summary(report, "bree").Drives);
        Assert.Null(Summary(report, "bree").Events["speeding"]);
        Assert.False(Summary(report, "bree").Covered);
        Assert.Equal(WeekCoverage.Partial, report.Coverage);
        Assert.Equal(new[] { "alden", "bree" }, report.Drivers.Select(d => d.MemberId));
    }

    [Fact]
    public void An_uncovered_driver_is_not_in_the_totals_and_gives_the_chip_an_asterisk()
    {
        var report = Report(
            0,
            [Alden(), Cade(since: Local(10, 6))],
            Dense("alden", Local(9, 28, 8), speeding: 2, meters: 1500));

        Assert.False(Summary(report, "cade").Covered);
        Assert.Equal(1, report.Totals.Drives);
        Assert.Equal(1500.0, report.Totals.Meters);
        Assert.True(report.Events["speeding"].Partial);
        Assert.Equal(2, report.Events["speeding"].Total);
    }

    [Fact]
    public void A_member_with_nothing_recorded_yet_is_not_covered()
    {
        var report = Report(0, [Alden(), Bree() with { RecordingStart = null }]);

        Assert.False(Summary(report, "bree").Covered);
        Assert.Null(Summary(report, "bree").CoverageStartUtc);
    }

    // ---- 6.8 top speed ties and attribution ---------------------------------------------------------------------------------

    [Fact]
    public void The_top_speed_is_the_fastest_dense_trip_with_its_driver_time_and_street()
    {
        var report = Report(
            0,
            [Alden(), Bree()],
            Dense("alden", Local(9, 28, 8), top: 24.1, topAt: Local(9, 28, 8, 5), topStreet: "Eastgate Avenue"),
            Dense("alden", Local(9, 29, 8), top: 42.9, topAt: Local(9, 29, 8, 7), topStreet: "Interstate 35"),
            Dense("bree", Local(9, 29, 9), top: 30.2, topAt: Local(9, 29, 9, 3), topStreet: "Larkspur Lane"));

        var top = report.TopSpeed;

        Assert.NotNull(top);
        Assert.Equal("alden", top.MemberId);
        Assert.Equal(42.9, top.SpeedMps);
        Assert.Equal(Local(9, 29, 8, 7), top.AtUtc);
        Assert.Equal("Interstate 35", top.Street);
        Assert.Equal(new DriverTopSpeed("alden", 42.9), top.Drivers.Single(d => d.MemberId == "alden"));
        Assert.Equal(new DriverTopSpeed("bree", 30.2), top.Drivers.Single(d => d.MemberId == "bree"));
    }

    [Fact]
    public void A_tie_to_0_01_m_s_goes_to_the_earlier_time_even_if_the_other_is_a_hair_faster()
    {
        var earlier = Dense("bree", Local(9, 29, 8), top: 30.001, topAt: Local(9, 29, 8, 3), topStreet: "Larkspur Lane");
        var later = Dense("alden", Local(9, 29, 8), top: 30.004, topAt: Local(9, 29, 8, 40), topStreet: "Interstate 35");

        var top = Report(0, [Alden(), Bree()], later, earlier).TopSpeed;

        Assert.NotNull(top);
        Assert.Equal("bree", top.MemberId);
        Assert.Equal(30.001, top.SpeedMps);
        Assert.Equal("Larkspur Lane", top.Street);
    }

    [Fact]
    public void A_tie_to_0_01_m_s_between_two_trips_of_one_driver_goes_to_the_earlier_one()
    {
        var report = Report(
            0,
            [Alden(), Bree()],
            Dense("alden", Local(9, 29, 8), top: 30.004, topAt: Local(9, 29, 8, 40), topStreet: "Interstate 35"),
            Dense("alden", Local(9, 29, 8), top: 30.001, topAt: Local(9, 29, 8, 3), topStreet: "Larkspur Lane"),
            Dense("bree", Local(9, 29, 9), top: 12));

        var top = report.TopSpeed;

        Assert.NotNull(top);
        Assert.Equal("alden", top.MemberId);
        Assert.Equal(30.001, top.SpeedMps);
        Assert.Equal(Local(9, 29, 8, 3), top.AtUtc);
        Assert.Equal("Larkspur Lane", top.Street);
        Assert.Equal(new DriverTopSpeed("alden", 30.001), top.Drivers.Single(d => d.MemberId == "alden"));
    }

    [Fact]
    public void Speeds_that_differ_at_the_hundredth_are_not_a_tie()
    {
        var earlier = Dense("bree", Local(9, 29, 8), top: 30.00, topAt: Local(9, 29, 8, 3));
        var later = Dense("alden", Local(9, 29, 8), top: 30.01, topAt: Local(9, 29, 8, 40));

        var top = Report(0, [Alden(), Bree()], later, earlier).TopSpeed;

        Assert.NotNull(top);
        Assert.Equal("alden", top.MemberId);
    }

    [Fact]
    public void A_driver_with_no_dense_trip_has_no_top_speed_of_their_own()
    {
        var report = Report(0, [Alden(), Bree()], Dense("alden", Local(9, 28, 8), top: 30), Coarse("bree", Local(9, 28, 9)));

        Assert.Equal(new DriverTopSpeed("bree", null), report.TopSpeed?.Drivers.Single(d => d.MemberId == "bree"));
    }

    // ---- 6.7 the driver week -------------------------------------------------------------------------------------------------------

    private static readonly IReadOnlyDictionary<string, string> PlaceNames = new Dictionary<string, string>
    {
        ["home"] = "Hearth Haven",
        ["work"] = "Cobblestone Court",
    };

    private static DriverWeek? DriverWeekOf(string memberId, int week, IReadOnlyList<StatsMember> members, params StatsTrip[] trips) =>
        StatsRules.DriverWeekOf(Now, DayOfWeek.Monday, Chicago, week, memberId, members, trips, PlaceNames);

    [Fact]
    public void An_unknown_member_has_no_driver_week()
    {
        Assert.Null(DriverWeekOf("prince", 0, [Alden()], Dense("alden", Local(9, 28, 8))));
    }

    [Fact]
    public void A_driver_week_lists_the_trips_newest_first_with_labels_from_zones_or_streets()
    {
        var week = DriverWeekOf(
            "alden",
            0,
            [Alden(), Bree()],
            Dense("alden", Local(9, 28, 8), meters: 1111.5, top: 20, speeding: 0, phone: 1, fromPlace: "home", toPlace: "work", fromStreet: "48 Larkspur Lane", toStreet: "Eastgate Avenue"),
            Dense("alden", Local(9, 30, 8), meters: 2222.25, top: 27.7, speeding: 2, phone: null, fromPlace: "gone", toPlace: null, fromStreet: "Eastgate Avenue", toStreet: null),
            Dense("bree", Local(9, 29, 8)),
            Dense("alden", Local(9, 29, 8), meters: 3333.75, fromPlace: "home", toPlace: "work"));

        Assert.NotNull(week);
        Assert.Equal(3, week.Trips.Count);
        Assert.Equal(new[] { Local(9, 30, 8), Local(9, 29, 8), Local(9, 28, 8) }, week.Trips.Select(t => t.StartUtc));

        var newest = week.Trips[0];
        Assert.Equal("Eastgate Avenue", newest.FromLabel);   // the zone id no longer exists: the street is the fallback
        Assert.Null(newest.ToLabel);                          // no zone, no street
        Assert.Equal(2222.25, newest.Meters);
        Assert.Equal(27.7, newest.TopSpeedMps);
        Assert.Equal(2, newest.Events["speeding"]);
        Assert.Null(newest.Events["phone"]);
        Assert.Null(newest.Events["accel"]);
        Assert.Null(newest.Events["braking"]);

        var oldest = week.Trips[2];
        Assert.Equal("Hearth Haven", oldest.FromLabel);      // the zone wins over the street
        Assert.Equal("Cobblestone Court", oldest.ToLabel);
        Assert.Equal(1, oldest.Events["phone"]);
    }

    [Fact]
    public void A_driver_week_summary_is_the_summary_of_the_driver_card()
    {
        StatsTrip[] trips = [Dense("alden", Local(9, 28, 8), speeding: 1, phone: 2), Dense("bree", Local(9, 29, 8), speeding: 3)];
        var members = new[] { Alden(), Bree() };

        var card = Summary(Report(0, members, trips), "alden");
        var summary = DriverWeekOf("alden", 0, members, trips)?.Summary;

        Assert.NotNull(summary);
        Assert.Equal(card.Drives, summary.Drives);
        Assert.Equal(card.Meters, summary.Meters);
        Assert.Equal(card.Events["speeding"], summary.Events["speeding"]);
        Assert.Equal(card.Events["phone"], summary.Events["phone"]);
        Assert.Equal(card.EventsTotal, summary.EventsTotal);
        Assert.Equal(card.EventsPartial, summary.EventsPartial);
        Assert.Equal(card.Covered, summary.Covered);
    }

    [Fact]
    public void A_driver_who_is_not_covered_gets_a_summary_with_null_counts_and_no_trips()
    {
        var week = DriverWeekOf("bree", 2, [Alden(), Bree(Local(9, 21))], Dense("bree", Local(9, 15, 8)));

        Assert.NotNull(week);
        Assert.False(week.Summary.Covered);
        Assert.Null(week.Summary.Drives);
        Assert.Null(week.Summary.Meters);
        Assert.All(week.Summary.Events.Values, v => Assert.Null(v));
        Assert.Empty(week.Trips);
    }

    [Fact]
    public void A_coarse_drive_has_no_top_speed_in_the_trip_list()
    {
        var week = DriverWeekOf("alden", 0, [Alden()], Coarse("alden", Local(9, 28, 8)) with { TopSpeedMps = 25 });

        var drive = Assert.Single(week!.Trips);
        Assert.Null(drive.TopSpeedMps);
        Assert.Equal(5000.0, drive.Meters);
    }
}
