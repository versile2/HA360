using Xunit;

namespace Realm.Domain.Tests;

// StatsRules.PeriodReport is the weekly report generalised to any window: [start, end) by the trip's start, and a comparator window of its own.
public class PeriodReportTests
{
    private static readonly TimeZoneInfo Chicago = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 21, 25, 0, TimeSpan.FromHours(-5));
    private static readonly DateTimeOffset Recorded = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly StatsMember[] Members =
    [
        new("alden", "Alden", PhoneCapable: true, Recorded),
        new("bree", "Bree", PhoneCapable: false, Recorded),
    ];

    private static StatsTrip Trip(string member, DateTimeOffset start, double meters = 1609.344, double top = 20, int speeding = 0, int? phone = 0) =>
        new(member, start, start.AddMinutes(10), meters, TripQuality.Dense, DistanceBasis.Gps, top, start.AddMinutes(5), null, speeding, phone, null, null, null, null);

    private static DateTimeOffset Local(int month, int day, int hour) => new(2026, month, day, hour, 0, 0, TimeSpan.FromHours(-5));

    [Fact]
    public void Last_month_counts_the_trips_that_start_inside_it_and_compares_with_the_month_before()
    {
        var window = PeriodMath.Resolve(new ReportPeriod(PeriodKind.LastMonth), Now, DayOfWeek.Monday, Chicago);
        var trips = new[]
        {
            Trip("alden", Local(7, 31, 23), speeding: 9),                      // July: the comparator
            Trip("alden", Local(8, 1, 0), meters: 3218.688, top: 30, speeding: 2, phone: 3),
            Trip("alden", Local(8, 31, 23), meters: 1609.344, top: 41, speeding: 1, phone: 1),
            Trip("bree", Local(8, 15, 12), meters: 1609.344, top: 55, speeding: 4, phone: null),
            Trip("alden", Local(9, 1, 0), top: 99, speeding: 50),              // September: outside
        };

        var report = StatsRules.PeriodReport(window, Members, trips);

        Assert.Equal(new ReportPeriod(PeriodKind.LastMonth), report.Period);
        Assert.False(report.IsCurrent);
        Assert.Equal(3, report.Totals.Drives);
        Assert.Equal(4 * 1609.344, report.Totals.Meters, 3);
        Assert.Equal("bree", report.TopSpeed!.MemberId);
        Assert.Equal(7, report.Events[EventKeys.Speeding].Total);
        Assert.Equal(9, report.Events[EventKeys.Speeding].ComparatorTotal);
        Assert.Equal(7 - 9, report.Events[EventKeys.Speeding].TrendDelta);
        Assert.Equal(2, report.Drivers[0].Drives);
        Assert.Equal("alden", report.Drivers[0].MemberId);
    }

    [Fact]
    public void A_period_that_starts_before_recording_began_has_no_trend_and_is_partial()
    {
        var late = new StatsMember("alden", "Alden", true, Local(8, 10, 0));
        var window = PeriodMath.Resolve(new ReportPeriod(PeriodKind.LastMonth), Now, DayOfWeek.Monday, Chicago);

        var report = StatsRules.PeriodReport(window, [late], [Trip("alden", Local(8, 20, 9), speeding: 3)]);

        Assert.Equal(WeekCoverage.Partial, report.Coverage);
        Assert.Null(report.Events[EventKeys.Speeding].TrendDelta);
        Assert.Equal(3, report.Events[EventKeys.Speeding].Total);
    }

    [Fact]
    public void The_driver_period_lists_every_trip_of_the_window_newest_first_with_named_ends()
    {
        var window = PeriodMath.Resolve(new ReportPeriod(PeriodKind.Last3Months), Now, DayOfWeek.Monday, Chicago);
        var trips = Enumerable.Range(1, 40).Select(i => Trip("alden", Local(8, 1, 0).AddHours(i * 5))).ToArray();
        var labels = new PlaceLabeler([new LabelZone("home", "Hearth Haven", 31.1, -85.3)]);

        var week = StatsRules.DriverPeriodOf(window, "alden", Members, trips, labels);

        Assert.NotNull(week);
        Assert.Equal(40, week.Trips.Count);
        Assert.True(week.Trips[0].StartUtc > week.Trips[39].StartUtc);
        Assert.Equal(PlaceLabeler.Fallback, week.Trips[0].FromLabel);
        Assert.Null(StatsRules.DriverPeriodOf(window, "nobody", Members, trips, labels));
    }
}
