using Xunit;

namespace Realm.Domain.Tests;

// The days of Location History as pure rules (0.3.0, D123): a local calendar day in HA's zone with half-open instants, 23 or 25 hours when the clocks change, the retained range and the route's date.
public class HistoryDayMathTests
{
    private static readonly TimeZoneInfo Chicago = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");

    [Fact]
    public void An_ordinary_day_runs_from_local_midnight_to_the_next_local_midnight()
    {
        var bounds = HistoryDayMath.Bounds(new DateOnly(2026, 9, 30), Chicago);

        Assert.Equal(new DateTimeOffset(2026, 9, 30, 5, 0, 0, TimeSpan.Zero), bounds.StartUtc);
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 5, 0, 0, TimeSpan.Zero), bounds.EndUtc);
        Assert.Equal(TimeSpan.FromHours(24), bounds.EndUtc - bounds.StartUtc);
    }

    [Fact]
    public void The_day_the_clocks_go_forward_has_23_hours_and_the_day_they_go_back_has_25()
    {
        var spring = HistoryDayMath.Bounds(new DateOnly(2026, 3, 8), Chicago);
        var autumn = HistoryDayMath.Bounds(new DateOnly(2026, 11, 1), Chicago);

        Assert.Equal(TimeSpan.FromHours(23), spring.EndUtc - spring.StartUtc);
        Assert.Equal(TimeSpan.FromHours(25), autumn.EndUtc - autumn.StartUtc);
    }

    [Fact]
    public void A_zone_whose_midnight_does_not_exist_starts_the_day_at_the_first_instant_after_the_gap()
    {
        // Sao Paulo changed its clocks at midnight until 2019: 2018-11-04 00:00 did not exist.
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

        var bounds = HistoryDayMath.Bounds(new DateOnly(2018, 11, 4), zone);

        Assert.Equal(new DateOnly(2018, 11, 4), HistoryDayMath.DayOf(bounds.StartUtc, zone));
        Assert.Equal(new DateOnly(2018, 11, 3), HistoryDayMath.DayOf(bounds.StartUtc.AddMinutes(-1), zone));
    }

    [Fact]
    public void Consecutive_days_share_their_edge_so_no_instant_is_in_two_days_or_none()
    {
        var first = HistoryDayMath.Bounds(new DateOnly(2026, 11, 1), Chicago);
        var second = HistoryDayMath.Bounds(new DateOnly(2026, 11, 2), Chicago);

        Assert.Equal(first.EndUtc, second.StartUtc);
    }

    [Theory]
    [InlineData(2026, 9, 30, 4, 59, 2026, 9, 29)]
    [InlineData(2026, 9, 30, 5, 0, 2026, 9, 30)]
    [InlineData(2026, 10, 1, 4, 59, 2026, 9, 30)]
    public void The_day_of_an_instant_is_the_local_day_not_the_utc_day(int year, int month, int day, int hour, int minute, int expectedYear, int expectedMonth, int expectedDay)
    {
        var instant = new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.Zero);

        Assert.Equal(new DateOnly(expectedYear, expectedMonth, expectedDay), HistoryDayMath.DayOf(instant, Chicago));
    }

    [Fact]
    public void The_retained_range_is_today_back_to_the_retention_and_never_later_than_today()
    {
        var now = new DateTimeOffset(2026, 10, 1, 3, 0, 0, TimeSpan.Zero);   // 22:00 on Sep 30 in Chicago

        var (oldest, newest) = HistoryDayMath.Range(now, Chicago, 100);

        Assert.Equal(new DateOnly(2026, 9, 30), newest);
        Assert.Equal(new DateOnly(2026, 6, 22), oldest);
        Assert.Equal(PeriodMath.OldestKept(now, Chicago, 100), oldest);
    }

    [Fact]
    public void A_retention_of_zero_reads_as_the_longest_the_add_on_allows()
    {
        var now = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

        var (oldest, newest) = HistoryDayMath.Range(now, Chicago, 0);

        Assert.Equal(400, newest.DayNumber - oldest.DayNumber);
    }

    [Fact]
    public void A_day_outside_the_range_is_clamped_to_it()
    {
        var oldest = new DateOnly(2026, 6, 22);
        var newest = new DateOnly(2026, 9, 30);

        Assert.Equal(newest, HistoryDayMath.Clamp(new DateOnly(2027, 1, 1), oldest, newest));
        Assert.Equal(oldest, HistoryDayMath.Clamp(new DateOnly(2020, 1, 1), oldest, newest));
        Assert.Equal(new DateOnly(2026, 8, 1), HistoryDayMath.Clamp(new DateOnly(2026, 8, 1), oldest, newest));
    }

    [Theory]
    [InlineData("2026-09-30", true)]
    [InlineData("2026-9-30", false)]
    [InlineData("30/09/2026", false)]
    [InlineData("2026-02-30", false)]
    [InlineData("today", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void The_date_of_the_route_is_exactly_year_month_day(string? text, bool valid)
    {
        var parsed = HistoryDayMath.Parse(text);

        Assert.Equal(valid, parsed is not null);
        if (valid)
        {
            Assert.Equal(new DateOnly(2026, 9, 30), parsed);
            Assert.Equal(text, HistoryDayMath.Iso(parsed!.Value));
        }
    }
}
