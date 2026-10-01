using System.Globalization;
using Xunit;

namespace Realm.Domain.Tests;

// Expected values are the fixture's week tables (the fixture clock is Wed 2026-09-30 21:25 CDT) and the
// bounds and week lengths computed with Python's zoneinfo, never with the code under test.
public class WeekMathTests
{
    private static readonly TimeZoneInfo Chicago = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");

    private const string FixtureNow = "2026-09-30T21:25:00-05:00";

    private static DateTimeOffset At(string iso) => DateTimeOffset.Parse(iso, CultureInfo.InvariantCulture);

    private static string Local(DateTimeOffset value) => value.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);

    // Zones built by hand, so these cases do not depend on the machine's tz database. DST starts on the second
    // <startDay> of March at the given standard time and ends on the first Sunday of November at 01:00 daylight
    // time (the clocks go back to 00:00).
    private static TimeZoneInfo CustomZone(DayOfWeek startDay, int startHour, int startMinute)
    {
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
            new DateTime(2020, 1, 1),
            new DateTime(2035, 12, 31),
            TimeSpan.FromHours(1),
            TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, startHour, startMinute, 0), 3, 2, startDay),
            TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 1, 0, 0), 11, 1, DayOfWeek.Sunday));
        return TimeZoneInfo.CreateCustomTimeZone("Test/Custom", TimeSpan.FromHours(-5), "Test", "Test standard", "Test daylight", [rule]);
    }

    // DST starts at local midnight on the second Sunday of March (00:00 to 01:00 does not exist) and ends at
    // 01:00 on the first Sunday of November (00:00 to 01:00 happens twice): the rules of a real Caribbean zone.
    private static TimeZoneInfo MidnightDstZone() => CustomZone(DayOfWeek.Sunday, 0, 0);

    // DST starts on the second Saturday of March at 23:30: the gap is 23:30 to 00:30, so midnight is inside it.
    private static TimeZoneInfo MidnightInsideGapZone() => CustomZone(DayOfWeek.Saturday, 23, 30);

    // Monday weeks of the fixture: week 0 = Sep 28 - Oct 4, week 1 = Sep 21-27, week 2 = Sep 14-20, week 3 = Sep 7-13.
    [Theory]
    [InlineData(0, "2026-09-28T05:00:00Z", "2026-10-05T05:00:00Z", "2026-09-28T00:00:00-05:00", "2026-10-04T23:59:59-05:00")]
    [InlineData(1, "2026-09-21T05:00:00Z", "2026-09-28T05:00:00Z", "2026-09-21T00:00:00-05:00", "2026-09-27T23:59:59-05:00")]
    [InlineData(2, "2026-09-14T05:00:00Z", "2026-09-21T05:00:00Z", "2026-09-14T00:00:00-05:00", "2026-09-20T23:59:59-05:00")]
    [InlineData(3, "2026-09-07T05:00:00Z", "2026-09-14T05:00:00Z", "2026-09-07T00:00:00-05:00", "2026-09-13T23:59:59-05:00")]
    public void Monday_weeks_of_the_fixture(int offset, string startUtc, string endUtc, string localStart, string localEnd)
    {
        var now = At(FixtureNow);

        Assert.Equal(At(startUtc), WeekMath.StartUtc(now, DayOfWeek.Monday, Chicago, offset));
        Assert.Equal(At(endUtc), WeekMath.EndUtc(now, DayOfWeek.Monday, Chicago, offset));

        var week = WeekMath.Week(now, DayOfWeek.Monday, Chicago, offset);
        Assert.Equal(offset, week.Offset);
        Assert.Equal(localStart, Local(week.Start));
        Assert.Equal(localEnd, Local(week.End));
    }

    // Sunday start: week 0 = Sep 27 - Oct 3.
    [Theory]
    [InlineData(0, "2026-09-27T05:00:00Z", "2026-10-04T05:00:00Z", "2026-09-27T00:00:00-05:00", "2026-10-03T23:59:59-05:00")]
    [InlineData(1, "2026-09-20T05:00:00Z", "2026-09-27T05:00:00Z", "2026-09-20T00:00:00-05:00", "2026-09-26T23:59:59-05:00")]
    [InlineData(2, "2026-09-13T05:00:00Z", "2026-09-20T05:00:00Z", "2026-09-13T00:00:00-05:00", "2026-09-19T23:59:59-05:00")]
    [InlineData(3, "2026-09-06T05:00:00Z", "2026-09-13T05:00:00Z", "2026-09-06T00:00:00-05:00", "2026-09-12T23:59:59-05:00")]
    public void Sunday_weeks_of_the_fixture(int offset, string startUtc, string endUtc, string localStart, string localEnd)
    {
        var now = At(FixtureNow);

        Assert.Equal(At(startUtc), WeekMath.StartUtc(now, DayOfWeek.Sunday, Chicago, offset));
        Assert.Equal(At(endUtc), WeekMath.EndUtc(now, DayOfWeek.Sunday, Chicago, offset));

        var week = WeekMath.Week(now, DayOfWeek.Sunday, Chicago, offset);
        Assert.Equal(localStart, Local(week.Start));
        Assert.Equal(localEnd, Local(week.End));
    }

    [Fact]
    public void Chips_are_the_four_fixture_weeks_this_week_first()
    {
        var chips = WeekMath.Chips(At(FixtureNow), DayOfWeek.Monday, Chicago);

        Assert.Equal(WeekMath.ChipCount, chips.Count);
        Assert.Equal(new[] { 0, 1, 2, 3 }, chips.Select(c => c.Offset).ToArray());
        Assert.Equal(
            new[] { "2026-09-28", "2026-09-21", "2026-09-14", "2026-09-07" },
            chips.Select(c => Local(c.Start)[..10]).ToArray());
        Assert.Equal(
            new[] { "2026-10-04", "2026-09-27", "2026-09-20", "2026-09-13" },
            chips.Select(c => Local(c.End)[..10]).ToArray());
    }

    // The day a week starts on and the local clock decide the week, not the UTC date.
    [Theory]
    [InlineData("2026-09-27T23:59:59-05:00", DayOfWeek.Monday, "2026-09-21T00:00:00-05:00")]
    [InlineData("2026-09-28T00:00:00-05:00", DayOfWeek.Monday, "2026-09-28T00:00:00-05:00")]
    [InlineData("2026-09-28T04:59:59Z", DayOfWeek.Monday, "2026-09-21T00:00:00-05:00")]
    [InlineData("2026-09-28T05:00:00Z", DayOfWeek.Monday, "2026-09-28T00:00:00-05:00")]
    [InlineData("2026-09-27T12:00:00-05:00", DayOfWeek.Sunday, "2026-09-27T00:00:00-05:00")]
    [InlineData("2026-09-26T23:59:59-05:00", DayOfWeek.Sunday, "2026-09-20T00:00:00-05:00")]
    public void The_current_week_follows_the_local_clock(string now, DayOfWeek weekStart, string expectedLocalStart)
    {
        var week = WeekMath.Week(At(now), weekStart, Chicago, 0);

        Assert.Equal(expectedLocalStart, Local(week.Start));
    }

    // Fall back (Sun 2026-11-01) makes the Monday week of Oct 26 169 hours long.
    [Fact]
    public void Monday_week_with_the_fall_back_change_is_169_hours()
    {
        var now = At("2026-10-28T12:00:00-05:00");

        var start = WeekMath.StartUtc(now, DayOfWeek.Monday, Chicago, 0);
        var end = WeekMath.EndUtc(now, DayOfWeek.Monday, Chicago, 0);

        Assert.Equal(At("2026-10-26T05:00:00Z"), start);
        Assert.Equal(At("2026-11-02T06:00:00Z"), end);
        Assert.Equal(TimeSpan.FromHours(169), end - start);

        var week = WeekMath.Week(now, DayOfWeek.Monday, Chicago, 0);
        Assert.Equal("2026-10-26T00:00:00-05:00", Local(week.Start));
        Assert.Equal("2026-11-01T23:59:59-06:00", Local(week.End));
    }

    // Spring forward (Sun 2027-03-14) makes the Monday week of Mar 8 167 hours long.
    [Fact]
    public void Monday_week_with_the_spring_forward_change_is_167_hours()
    {
        var now = At("2027-03-10T12:00:00-06:00");

        var start = WeekMath.StartUtc(now, DayOfWeek.Monday, Chicago, 0);
        var end = WeekMath.EndUtc(now, DayOfWeek.Monday, Chicago, 0);

        Assert.Equal(At("2027-03-08T06:00:00Z"), start);
        Assert.Equal(At("2027-03-15T05:00:00Z"), end);
        Assert.Equal(TimeSpan.FromHours(167), end - start);

        var week = WeekMath.Week(now, DayOfWeek.Monday, Chicago, 0);
        Assert.Equal("2027-03-08T00:00:00-06:00", Local(week.Start));
        Assert.Equal("2027-03-14T23:59:59-05:00", Local(week.End));
    }

    // The Sunday-start weeks that contain the same two changes: the transition is on their first day.
    [Theory]
    [InlineData("2026-11-04T12:00:00-06:00", "2026-11-01T05:00:00Z", "2026-11-08T06:00:00Z", 169)]
    [InlineData("2027-03-17T12:00:00-05:00", "2027-03-14T06:00:00Z", "2027-03-21T05:00:00Z", 167)]
    public void Sunday_weeks_with_a_DST_change(string now, string startUtc, string endUtc, int hours)
    {
        var start = WeekMath.StartUtc(At(now), DayOfWeek.Sunday, Chicago, 0);
        var end = WeekMath.EndUtc(At(now), DayOfWeek.Sunday, Chicago, 0);

        Assert.Equal(At(startUtc), start);
        Assert.Equal(At(endUtc), end);
        Assert.Equal(TimeSpan.FromHours(hours), end - start);
    }

    // Every chip starts at local midnight, also the ones before a DST change: Oct 26 is still CDT.
    [Fact]
    public void Chips_across_the_fall_back_change_keep_local_midnight()
    {
        var now = At("2026-11-04T12:00:00-06:00");
        var chips = WeekMath.Chips(now, DayOfWeek.Monday, Chicago);

        Assert.Equal(
            new[] { "2026-11-02T00:00:00-06:00", "2026-10-26T00:00:00-05:00", "2026-10-19T00:00:00-05:00", "2026-10-12T00:00:00-05:00" },
            chips.Select(c => Local(c.Start)).ToArray());
        Assert.Equal(
            new[] { "2026-11-08T23:59:59-06:00", "2026-11-01T23:59:59-06:00", "2026-10-25T23:59:59-05:00", "2026-10-18T23:59:59-05:00" },
            chips.Select(c => Local(c.End)).ToArray());

        var weekHours = new[] { 168, 169, 168, 168 };
        for (var offset = 0; offset < weekHours.Length; offset++)
        {
            var length = WeekMath.EndUtc(now, DayOfWeek.Monday, Chicago, offset) - WeekMath.StartUtc(now, DayOfWeek.Monday, Chicago, offset);
            Assert.Equal(TimeSpan.FromHours(weekHours[offset]), length);
        }
    }

    // A trip belongs to the week of the local date it started on (fixture clock, Monday weeks).
    [Theory]
    [InlineData("2026-09-30T21:20:00-05:00", 0)]
    [InlineData("2026-09-28T00:00:00-05:00", 0)]
    [InlineData("2026-09-28T05:00:00Z", 0)]
    [InlineData("2026-09-28T04:59:59Z", 1)]
    [InlineData("2026-09-27T23:59:59-05:00", 1)]
    [InlineData("2026-09-21T00:00:00-05:00", 1)]
    [InlineData("2026-09-14T00:00:00-05:00", 2)]
    [InlineData("2026-09-07T00:00:00-05:00", 3)]
    [InlineData("2026-09-06T23:59:59-05:00", 4)]
    [InlineData("2026-10-05T00:00:00-05:00", -1)]
    public void Monday_week_assignment_uses_the_local_start(string tripStart, int expectedOffset)
    {
        Assert.Equal(expectedOffset, WeekMath.WeekOffsetOf(At(tripStart), At(FixtureNow), DayOfWeek.Monday, Chicago));
    }

    // A trip that starts on Sunday 23:50 and ends on Monday belongs to the week it started in.
    [Fact]
    public void A_trip_that_crosses_midnight_into_the_next_week_stays_in_the_week_it_started_in()
    {
        var start = At("2026-09-27T23:50:00-05:00");
        var end = At("2026-09-28T00:10:00-05:00");

        Assert.Equal(1, WeekMath.WeekOffsetOf(start, At(FixtureNow), DayOfWeek.Monday, Chicago));
        Assert.Equal(0, WeekMath.WeekOffsetOf(end, At(FixtureNow), DayOfWeek.Monday, Chicago));
    }

    [Theory]
    [InlineData("2026-09-27T00:00:00-05:00", 0)]
    [InlineData("2026-09-26T23:59:59-05:00", 1)]
    [InlineData("2026-09-20T00:00:00-05:00", 1)]
    [InlineData("2026-09-06T00:00:00-05:00", 3)]
    [InlineData("2026-09-05T23:59:59-05:00", 4)]
    public void Sunday_week_assignment_uses_the_local_start(string tripStart, int expectedOffset)
    {
        Assert.Equal(expectedOffset, WeekMath.WeekOffsetOf(At(tripStart), At(FixtureNow), DayOfWeek.Sunday, Chicago));
    }

    // Around the DST change the 169-hour week is still one week: the instants either side of local midnight differ.
    [Theory]
    [InlineData("2026-11-02T05:59:59Z", 1)]
    [InlineData("2026-11-02T06:00:00Z", 0)]
    [InlineData("2026-11-01T23:30:00-06:00", 1)]
    [InlineData("2026-10-26T05:00:00Z", 1)]
    [InlineData("2026-10-26T04:59:59Z", 2)]
    public void Week_assignment_across_the_fall_back_change(string tripStart, int expectedOffset)
    {
        var now = At("2026-11-04T12:00:00-06:00");

        Assert.Equal(expectedOffset, WeekMath.WeekOffsetOf(At(tripStart), now, DayOfWeek.Monday, Chicago));
    }

    // Bounds and assignment agree: the first instant of a week is in it, the last instant before the end is in it,
    // and the end itself is in the following week.
    [Theory]
    [InlineData(FixtureNow, DayOfWeek.Monday)]
    [InlineData(FixtureNow, DayOfWeek.Sunday)]
    [InlineData("2026-11-04T12:00:00-06:00", DayOfWeek.Monday)]
    [InlineData("2027-03-10T12:00:00-06:00", DayOfWeek.Monday)]
    [InlineData("2027-03-17T12:00:00-05:00", DayOfWeek.Sunday)]
    public void Bounds_and_assignment_agree(string nowIso, DayOfWeek weekStart)
    {
        var now = At(nowIso);

        for (var offset = 0; offset < WeekMath.ChipCount; offset++)
        {
            var start = WeekMath.StartUtc(now, weekStart, Chicago, offset);
            var end = WeekMath.EndUtc(now, weekStart, Chicago, offset);

            Assert.Equal(offset, WeekMath.WeekOffsetOf(start, now, weekStart, Chicago));
            Assert.Equal(offset, WeekMath.WeekOffsetOf(end.AddTicks(-1), now, weekStart, Chicago));
            Assert.Equal(offset - 1, WeekMath.WeekOffsetOf(end, now, weekStart, Chicago));
        }
    }

    // If local midnight does not exist the week starts at the next valid instant (01:00 local).
    [Fact]
    public void A_week_whose_first_midnight_falls_in_a_DST_gap_starts_at_the_next_valid_instant()
    {
        var zone = MidnightDstZone();
        var now = At("2027-03-17T12:00:00-04:00");

        var week = WeekMath.Week(now, DayOfWeek.Sunday, zone, 0);

        Assert.Equal(At("2027-03-14T05:00:00Z"), WeekMath.StartUtc(now, DayOfWeek.Sunday, zone, 0));
        Assert.Equal("2027-03-14T01:00:00-04:00", Local(week.Start));
        Assert.Equal("2027-03-20T23:59:59-04:00", Local(week.End));
        Assert.Equal(TimeSpan.FromHours(167), WeekMath.EndUtc(now, DayOfWeek.Sunday, zone, 0) - WeekMath.StartUtc(now, DayOfWeek.Sunday, zone, 0));

        // The previous week ends where this one starts, and nothing falls between them.
        Assert.Equal(At("2027-03-07T05:00:00Z"), WeekMath.StartUtc(now, DayOfWeek.Sunday, zone, 1));
        Assert.Equal(At("2027-03-14T05:00:00Z"), WeekMath.EndUtc(now, DayOfWeek.Sunday, zone, 1));
        Assert.Equal(1, WeekMath.WeekOffsetOf(At("2027-03-14T04:59:59Z"), now, DayOfWeek.Sunday, zone));
        Assert.Equal(0, WeekMath.WeekOffsetOf(At("2027-03-14T05:00:00Z"), now, DayOfWeek.Sunday, zone));
    }

    // The gap need not start at midnight: here it runs 23:30 to 00:30, and the first valid instant of Sunday is 00:30.
    [Fact]
    public void A_week_whose_first_midnight_is_inside_a_DST_gap_starts_when_the_gap_ends()
    {
        var zone = MidnightInsideGapZone();
        var now = At("2027-03-17T12:00:00-04:00");

        var week = WeekMath.Week(now, DayOfWeek.Sunday, zone, 0);

        Assert.Equal(At("2027-03-14T04:30:00Z"), WeekMath.StartUtc(now, DayOfWeek.Sunday, zone, 0));
        Assert.Equal("2027-03-14T00:30:00-04:00", Local(week.Start));
        Assert.Equal("2027-03-20T23:59:59-04:00", Local(week.End));
        Assert.Equal(TimeSpan.FromHours(167.5), WeekMath.EndUtc(now, DayOfWeek.Sunday, zone, 0) - WeekMath.StartUtc(now, DayOfWeek.Sunday, zone, 0));
        Assert.Equal(1, WeekMath.WeekOffsetOf(At("2027-03-14T04:29:59Z"), now, DayOfWeek.Sunday, zone));
        Assert.Equal(0, WeekMath.WeekOffsetOf(At("2027-03-14T04:30:00Z"), now, DayOfWeek.Sunday, zone));
    }

    // If local midnight happens twice the week starts at the first one, so the whole first local hour is in the week.
    [Fact]
    public void A_week_whose_first_midnight_happens_twice_starts_at_the_first_one()
    {
        var zone = MidnightDstZone();
        var now = At("2027-11-10T12:00:00-05:00");

        var week = WeekMath.Week(now, DayOfWeek.Sunday, zone, 0);

        Assert.Equal(At("2027-11-07T04:00:00Z"), WeekMath.StartUtc(now, DayOfWeek.Sunday, zone, 0));
        Assert.Equal("2027-11-07T00:00:00-04:00", Local(week.Start));
        Assert.Equal("2027-11-13T23:59:59-05:00", Local(week.End));
        Assert.Equal(TimeSpan.FromHours(169), WeekMath.EndUtc(now, DayOfWeek.Sunday, zone, 0) - WeekMath.StartUtc(now, DayOfWeek.Sunday, zone, 0));

        // 00:30 local, first and second time round, are both in this week; 23:59:59 the evening before is in the last one.
        Assert.Equal(0, WeekMath.WeekOffsetOf(At("2027-11-07T04:30:00Z"), now, DayOfWeek.Sunday, zone));
        Assert.Equal(0, WeekMath.WeekOffsetOf(At("2027-11-07T05:30:00Z"), now, DayOfWeek.Sunday, zone));
        Assert.Equal(1, WeekMath.WeekOffsetOf(At("2027-11-07T03:59:59Z"), now, DayOfWeek.Sunday, zone));
    }
}
