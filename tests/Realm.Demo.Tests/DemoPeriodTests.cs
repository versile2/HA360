using System.Globalization;
using Realm.Domain;
using Xunit;

namespace Realm.Demo.Tests;

// The Demo's long periods (01 section 6.2): the fixture holds the four frozen weeks and repeats its three full weeks back in time with their own dates, so a month, a rolling
// window and a custom range have drives; the totals follow the production rules (StatsRules), so they are sums of the listed drives. Fixture clock: Wed 2026-09-30 21:25 CDT.
public class DemoPeriodTests
{
    private static readonly TimeZoneInfo Chicago = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");

    private static IRealmSession NewSession(params string[] variants) => new DemoRealmSessionFactory().Create(new DemoUrlParams(null, variants));

    private static ReportWindow Window(IRealmSession session, ReportPeriod period) =>
        PeriodMath.Resolve(period, session.Time.GetUtcNow(), session.Current.WeekStart, session.Zone);

    [Fact]
    public async Task The_demo_offers_every_period_of_the_split_button()
    {
        var session = NewSession();

        Assert.Equal(400, session.Current.RetentionFixDays);
        Assert.Equal(4, PeriodMath.MenuKinds(session.Current.RetentionFixDays).Count);
        Assert.Equal(new DrivingThresholds(80, 30, 10), session.Current.Thresholds);
        await session.DisposeAsync();
    }

    [Theory]
    [InlineData(PeriodKind.LastMonth)]
    [InlineData(PeriodKind.Last3Months)]
    [InlineData(PeriodKind.Last6Months)]
    [InlineData(PeriodKind.LastYear)]
    public async Task A_long_period_has_drives_and_its_totals_are_the_sums_of_the_drivers_lists(PeriodKind kind)
    {
        var session = NewSession();
        var window = Window(session, new ReportPeriod(kind));

        var report = await session.GetPeriodReportAsync(window, CancellationToken.None);

        Assert.Equal(new ReportPeriod(kind), report.Period);
        Assert.Equal(WeekCoverage.Full, report.Coverage);
        Assert.True(report.Totals.Drives > 0);
        var listed = 0;
        var meters = 0.0;
        foreach (var driver in report.Drivers)
        {
            var week = await session.GetDriverPeriodAsync(driver.MemberId, window, CancellationToken.None);
            Assert.NotNull(week);
            Assert.Equal(driver.Drives, week.Trips.Count);
            Assert.All(week.Trips, trip => Assert.True(trip.StartUtc >= window.StartUtc && trip.StartUtc < window.EndUtc));
            listed += week.Trips.Count;
            meters += week.Trips.Sum(trip => trip.Meters);
        }

        Assert.Equal(report.Totals.Drives, listed);
        Assert.Equal(report.Totals.Meters, meters, 3);
        await session.DisposeAsync();
    }

    [Fact]
    public async Task A_longer_period_has_more_drives_than_a_shorter_one_that_it_contains()
    {
        var session = NewSession();
        var month = await session.GetPeriodReportAsync(Window(session, new ReportPeriod(PeriodKind.Last3Months)), CancellationToken.None);
        var year = await session.GetPeriodReportAsync(Window(session, new ReportPeriod(PeriodKind.LastYear)), CancellationToken.None);

        Assert.True(year.Totals.Drives > month.Totals.Drives);
        await session.DisposeAsync();
    }

    [Fact]
    public async Task A_week_asked_for_as_a_period_is_the_frozen_week_of_the_fixture()
    {
        var session = NewSession();
        var frozen = await session.GetWeekReportAsync(1, DayOfWeek.Monday, CancellationToken.None);

        var period = await session.GetPeriodReportAsync(Window(session, ReportPeriod.OfWeek(1)), CancellationToken.None);

        Assert.Equal(frozen.Totals, period.Totals);
        Assert.Equal(frozen.Events[EventKeys.Speeding].Total, period.Events[EventKeys.Speeding].Total);
        Assert.Equal(ReportPeriod.OfWeek(1), period.Period);
        await session.DisposeAsync();
    }

    [Fact]
    public async Task A_custom_range_counts_only_the_drives_that_start_inside_it()
    {
        var session = NewSession();
        var window = Window(session, ReportPeriod.OfRange(new DateOnly(2026, 9, 2), new DateOnly(2026, 9, 3)));

        var week = await session.GetDriverPeriodAsync("jester", window, CancellationToken.None);

        Assert.NotNull(week);
        Assert.NotEmpty(week.Trips);
        Assert.All(week.Trips, trip =>
        {
            var local = TimeZoneInfo.ConvertTime(trip.StartUtc, Chicago).Date;
            Assert.True(local == new DateTime(2026, 9, 2) || local == new DateTime(2026, 9, 3));
        });
        await session.DisposeAsync();
    }

    [Fact]
    public async Task No_drive_of_any_period_has_an_unnamed_end_or_says_unknown_place()
    {
        var session = NewSession();
        var window = Window(session, new ReportPeriod(PeriodKind.LastYear));

        foreach (var member in new[] { "king", "queen", "jester", "cryptid" })
        {
            var week = await session.GetDriverPeriodAsync(member, window, CancellationToken.None);
            Assert.NotNull(week);
            Assert.All(week.Trips, trip =>
            {
                Assert.False(string.IsNullOrWhiteSpace(trip.FromLabel));
                Assert.False(string.IsNullOrWhiteSpace(trip.ToLabel));
                Assert.DoesNotContain("Unknown", trip.FromLabel!, StringComparison.Ordinal);
                Assert.DoesNotContain("Unknown", trip.ToLabel!, StringComparison.Ordinal);
            });
        }

        await session.DisposeAsync();
    }

    [Fact]
    public async Task The_static_member_and_an_unknown_id_have_no_driver_period()
    {
        var session = NewSession();
        var window = Window(session, new ReportPeriod(PeriodKind.LastMonth));

        Assert.Null(await session.GetDriverPeriodAsync("prince", window, CancellationToken.None));
        Assert.Null(await session.GetDriverPeriodAsync("nobody", window, CancellationToken.None));
        await session.DisposeAsync();
    }

    [Fact]
    public async Task On_a_fresh_install_the_long_periods_before_recording_began_are_empty()
    {
        var session = NewSession("fresh-install");
        var window = Window(session, new ReportPeriod(PeriodKind.LastMonth));   // August: before the recording began on Sep 23

        var report = await session.GetPeriodReportAsync(window, CancellationToken.None);

        Assert.Equal(WeekCoverage.NoRecord, report.Coverage);
        await session.DisposeAsync();
    }

    [Fact]
    public void The_period_text_is_culture_free()
    {
        Assert.Equal("2026-08-01", ReportPeriod.Iso(new DateOnly(2026, 8, 1)));
        Assert.Equal("period=3m", new ReportPeriod(PeriodKind.Last3Months).Query());
        Assert.Equal("2026", new DateOnly(2026, 8, 1).ToString("yyyy", CultureInfo.InvariantCulture));
    }
}
