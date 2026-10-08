using System.Globalization;
using Xunit;

namespace Realm.Domain.Tests;

// Expected instants were worked out by hand from the calendar and the America/Chicago rules (CDT -05:00 until Sun 2026-11-01 02:00), never with the code under test.
// The fixture clock is Wed 2026-09-30 21:25 CDT.
public class PeriodMathTests
{
    private static readonly TimeZoneInfo Chicago = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");

    private static readonly DateTimeOffset Now = At("2026-09-30T21:25:00-05:00");

    private static DateTimeOffset At(string iso) => DateTimeOffset.Parse(iso, CultureInfo.InvariantCulture);

    private static string Utc(DateTimeOffset value) => value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    private static ReportWindow Resolve(PeriodKind kind, DateTimeOffset? now = null) =>
        PeriodMath.Resolve(new ReportPeriod(kind), now ?? Now, DayOfWeek.Monday, Chicago);

    [Fact]
    public void Last_month_is_the_previous_calendar_month()
    {
        var window = Resolve(PeriodKind.LastMonth);

        Assert.Equal("2026-08-01T05:00:00Z", Utc(window.StartUtc));
        Assert.Equal("2026-09-01T05:00:00Z", Utc(window.EndUtc));
        Assert.Equal("2026-08-31T23:59:59-05:00", window.End.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture));
        Assert.False(window.IsCurrent);
        // The month before it is July, 31 days.
        Assert.Equal("2026-07-01T05:00:00Z", Utc(window.Comparator.StartUtc));
        Assert.Equal("2026-08-01T05:00:00Z", Utc(window.Comparator.EndUtc));
    }

    [Fact]
    public void Last_month_in_January_is_December_of_the_year_before()
    {
        var window = Resolve(PeriodKind.LastMonth, At("2027-01-05T09:00:00-06:00"));

        Assert.Equal("2026-12-01T06:00:00Z", Utc(window.StartUtc));
        Assert.Equal("2027-01-01T06:00:00Z", Utc(window.EndUtc));
    }

    [Fact]
    public void Last_month_in_March_of_a_leap_year_is_a_29_day_february_compared_with_january()
    {
        var window = Resolve(PeriodKind.LastMonth, At("2028-03-15T12:00:00-05:00"));

        Assert.Equal("2028-02-01T06:00:00Z", Utc(window.StartUtc));
        Assert.Equal("2028-03-01T06:00:00Z", Utc(window.EndUtc));
        Assert.Equal("2028-01-01T06:00:00Z", Utc(window.Comparator.StartUtc));
    }

    [Fact]
    public void The_last_day_of_a_month_still_reports_the_previous_month_not_this_one()
    {
        var window = Resolve(PeriodKind.LastMonth, At("2026-08-31T23:59:00-05:00"));

        Assert.Equal("2026-07-01T05:00:00Z", Utc(window.StartUtc));
        Assert.Equal("2026-08-01T05:00:00Z", Utc(window.EndUtc));
    }

    [Theory]
    [InlineData(PeriodKind.Last3Months, "2026-07-01T05:00:00Z", 92)]
    [InlineData(PeriodKind.Last6Months, "2026-03-31T05:00:00Z", 184)]
    [InlineData(PeriodKind.LastYear, "2025-10-01T05:00:00Z", 365)]
    public void Rolling_windows_end_with_today_and_start_after_today_minus_the_months(PeriodKind kind, string startUtc, int days)
    {
        var window = Resolve(kind);

        Assert.Equal(startUtc, Utc(window.StartUtc));
        Assert.Equal("2026-10-01T05:00:00Z", Utc(window.EndUtc));
        Assert.Equal(days, (window.EndUtc - window.StartUtc).TotalDays);
        // The comparator is the period of the same length just before.
        Assert.Equal(window.StartUtc, window.Comparator.EndUtc);
        Assert.Equal(days, (window.Comparator.EndUtc - window.Comparator.StartUtc).TotalDays);
    }

    [Fact]
    public void A_rolling_year_that_includes_a_leap_day_is_366_days()
    {
        var window = Resolve(PeriodKind.LastYear, At("2028-12-31T12:00:00-06:00"));

        Assert.Equal("2028-01-01T06:00:00Z", Utc(window.StartUtc));
        Assert.Equal(366, Math.Round((window.EndUtc - window.StartUtc).TotalDays));
    }

    [Fact]
    public void A_rolling_window_started_on_the_31st_clamps_to_the_shorter_month()
    {
        // 2026-05-31 minus 3 months is 2026-02-28: the window starts on 2026-03-01.
        var window = Resolve(PeriodKind.Last3Months, At("2026-05-31T10:00:00-05:00"));

        Assert.Equal("2026-03-01T06:00:00Z", Utc(window.StartUtc));
    }

    [Fact]
    public void A_custom_range_covers_both_end_days_and_a_dst_end_day_is_25_hours()
    {
        var window = PeriodMath.Resolve(ReportPeriod.OfRange(new DateOnly(2026, 10, 31), new DateOnly(2026, 11, 1)), Now, DayOfWeek.Monday, Chicago);

        Assert.Equal("2026-10-31T05:00:00Z", Utc(window.StartUtc));
        Assert.Equal("2026-11-02T06:00:00Z", Utc(window.EndUtc));
        Assert.Equal(49, (window.EndUtc - window.StartUtc).TotalHours);
        Assert.Equal("2026-10-29T05:00:00Z", Utc(window.Comparator.StartUtc));
    }

    [Fact]
    public void A_reversed_custom_range_is_read_oldest_first()
    {
        var window = PeriodMath.Resolve(new ReportPeriod(PeriodKind.Custom, 0, new DateOnly(2026, 9, 10), new DateOnly(2026, 9, 3)), Now, DayOfWeek.Monday, Chicago);

        Assert.Equal("2026-09-03T05:00:00Z", Utc(window.StartUtc));
        Assert.Equal("2026-09-11T05:00:00Z", Utc(window.EndUtc));
    }

    [Fact]
    public void A_week_chip_resolves_to_the_week_bounds_and_the_like_for_like_comparator()
    {
        var window = PeriodMath.Resolve(ReportPeriod.ThisWeek, Now, DayOfWeek.Monday, Chicago);

        Assert.Equal("2026-09-28T05:00:00Z", Utc(window.StartUtc));
        Assert.Equal("2026-10-05T05:00:00Z", Utc(window.EndUtc));
        Assert.True(window.IsCurrent);
        Assert.Equal(StatsRules.ComparatorWindow(Now, DayOfWeek.Monday, Chicago, 0), window.Comparator);

        var sunday = PeriodMath.Resolve(ReportPeriod.OfWeek(1), Now, DayOfWeek.Sunday, Chicago);
        Assert.Equal("2026-09-20T05:00:00Z", Utc(sunday.StartUtc));
        Assert.False(sunday.IsCurrent);
    }

    [Theory]
    [InlineData(PeriodKind.LastMonth, 100, true)]
    [InlineData(PeriodKind.Last3Months, 91, false)]
    [InlineData(PeriodKind.Last3Months, 92, true)]
    [InlineData(PeriodKind.Last3Months, 100, true)]
    [InlineData(PeriodKind.Last6Months, 184, false)]
    [InlineData(PeriodKind.Last6Months, 185, true)]
    [InlineData(PeriodKind.LastYear, 365, false)]
    [InlineData(PeriodKind.LastYear, 366, true)]
    [InlineData(PeriodKind.LastYear, 400, true)]
    public void Long_periods_are_offered_only_when_the_retention_covers_them(PeriodKind kind, int retentionDays, bool offered)
    {
        Assert.Equal(offered, PeriodMath.IsAvailable(kind, retentionDays));
    }

    [Fact]
    public void The_menu_lists_the_periods_the_retention_covers_in_order()
    {
        Assert.Equal([PeriodKind.LastMonth, PeriodKind.Last3Months], PeriodMath.MenuKinds(100));
        Assert.Equal([PeriodKind.LastMonth, PeriodKind.Last3Months, PeriodKind.Last6Months], PeriodMath.MenuKinds(200));
        Assert.Equal([PeriodKind.LastMonth, PeriodKind.Last3Months, PeriodKind.Last6Months, PeriodKind.LastYear], PeriodMath.MenuKinds(400));
    }

    [Fact]
    public void A_custom_range_is_checked_against_order_today_and_the_kept_history()
    {
        // Today is 2026-09-30; 100 days kept: the oldest kept day is 2026-06-22.
        RangeCheck Check(string? from, string? to) =>
            PeriodMath.ValidateCustom(from is null ? null : DateOnly.Parse(from, CultureInfo.InvariantCulture), to is null ? null : DateOnly.Parse(to, CultureInfo.InvariantCulture), Now, Chicago, 100);

        Assert.Equal(RangeError.None, Check("2026-09-01", "2026-09-30").Error);
        Assert.Equal(RangeError.None, Check("2026-06-22", "2026-06-22").Error);
        Assert.Equal(RangeError.Missing, Check(null, "2026-09-30").Error);
        Assert.Equal(RangeError.EndBeforeStart, Check("2026-09-10", "2026-09-09").Error);
        Assert.Equal(RangeError.InFuture, Check("2026-09-20", "2026-10-01").Error);
        var tooOld = Check("2026-06-21", "2026-09-01");
        Assert.Equal(RangeError.BeforeHistory, tooOld.Error);
        Assert.Equal(new DateOnly(2026, 6, 22), tooOld.OldestKept);
        Assert.False(tooOld.IsValid);
    }

    [Fact]
    public void Today_is_read_in_the_zone_not_in_utc()
    {
        // 2026-10-01 02:00Z is still Sep 30 evening in Chicago.
        Assert.Equal(new DateOnly(2026, 9, 30), PeriodMath.Today(At("2026-10-01T02:00:00Z"), Chicago));
    }

    [Theory]
    [InlineData(null, null, null, null, PeriodKind.Week, 0)]
    [InlineData(null, null, null, "2", PeriodKind.Week, 2)]
    [InlineData(null, null, null, "4", PeriodKind.Week, 0)]
    [InlineData(null, null, null, "-1", PeriodKind.Week, 0)]
    [InlineData("last-month", null, null, "2", PeriodKind.LastMonth, 0)]
    [InlineData("3m", null, null, null, PeriodKind.Last3Months, 0)]
    [InlineData("6m", null, null, null, PeriodKind.Last6Months, 0)]
    [InlineData("1y", null, null, null, PeriodKind.LastYear, 0)]
    [InlineData("custom", "2026-08-01", "2026-08-15", null, PeriodKind.Custom, 0)]
    [InlineData("custom", "2026-08-01", null, "1", PeriodKind.Week, 1)]
    [InlineData("custom", "garbage", "2026-08-15", null, PeriodKind.Week, 0)]
    [InlineData("nonsense", null, null, "3", PeriodKind.Week, 3)]
    public void The_address_is_read_into_a_period_and_old_week_links_keep_working(string? period, string? from, string? to, string? week, PeriodKind kind, int offset)
    {
        var parsed = ReportPeriod.Parse(period, from, to, week);

        Assert.Equal(kind, parsed.Kind);
        Assert.Equal(offset, parsed.WeekOffset);
    }

    [Fact]
    public void A_reversed_custom_address_is_swapped_and_the_query_round_trips()
    {
        var parsed = ReportPeriod.Parse("custom", "2026-08-15", "2026-08-01", null);

        Assert.Equal(new DateOnly(2026, 8, 1), parsed.From);
        Assert.Equal(new DateOnly(2026, 8, 15), parsed.To);
        Assert.Equal("period=custom&from=2026-08-01&to=2026-08-15", parsed.Query());
        Assert.Equal(parsed, ReportPeriod.Parse("custom", "2026-08-01", "2026-08-15", null));
        Assert.Equal(string.Empty, ReportPeriod.ThisWeek.Query());
        Assert.Equal("week=2", ReportPeriod.OfWeek(2).Query());
        Assert.Equal("period=3m", new ReportPeriod(PeriodKind.Last3Months).Query());
        Assert.Equal("period=1y", new ReportPeriod(PeriodKind.LastYear).Query());
        Assert.Equal("period=last-month", new ReportPeriod(PeriodKind.LastMonth).Query());
    }
}
