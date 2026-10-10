using Xunit;
using static Realm.Domain.Tests.HistoryTestData;

namespace Realm.Domain.Tests;

// The range of Location History (0.3.0, D123): one read for the whole range, sliced per day, newest day first, without trails.
public class HistoryRangeTests
{
    private static readonly DateOnly Day = new(2026, 9, 29);

    [Fact]
    public void The_read_window_is_the_range_widened_by_a_day_each_side()
    {
        var window = HistoryRange.ReadWindow(Day.AddDays(-2), Day, TimeZoneInfo.Utc);

        Assert.Equal(At(0).AddDays(-3), window.StartUtc);
        Assert.Equal(At(0).AddDays(2), window.EndUtc);
    }

    [Fact]
    public void Days_come_newest_first_each_with_its_own_visits_and_no_trail()
    {
        var fixes = new List<RawFix>();
        for (var back = 0; back < 3; back++)
        {
            fixes.AddRange(Hold(At(9).AddDays(-back), At(12).AddDays(-back), HomeLat, HomeLon));
        }

        var days = HistoryRange.Build("alden", Day.AddDays(-2), Day, TimeZoneInfo.Utc, fixes, [], Zones, At(13), includeTrail: false);

        Assert.Equal([Day, Day.AddDays(-1), Day.AddDays(-2)], days.Select(day => day.Day));
        Assert.All(days, day => Assert.Single(day.Stays));
        Assert.All(days, day => Assert.Empty(day.Trail));
        Assert.All(days, day => Assert.Equal("Hearth Haven", day.Stays.Single().Label));
    }

    [Fact]
    public void A_day_nobody_was_recorded_in_is_in_the_list_and_says_so()
    {
        var days = HistoryRange.Build("alden", Day.AddDays(-1), Day, TimeZoneInfo.Utc, Hold(At(9), At(12), HomeLat, HomeLon), [], Zones, At(13), includeTrail: false);

        Assert.True(days[0].Recorded);
        Assert.False(days[1].Recorded);
        Assert.Empty(days[1].Entries);
    }
}
