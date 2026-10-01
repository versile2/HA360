using Realm.Web.Formatting;
using Xunit;

namespace Realm.Web.Tests;

/// <summary>
/// The "Here for" chip text (01 sections 4.4 and 8.3): the two largest non-zero units of days, hours and minutes, singular at 1, whole minutes,
/// and "Just arrived" under a minute. S7 extends the formatter; these rows are only the chip.
/// </summary>
public sealed class TimeFormatterChipTests
{
    [Theory]
    [InlineData(0, "Just arrived")]
    [InlineData(59, "Just arrived")]
    [InlineData(-300, "Just arrived")]
    [InlineData(60, "Here for 1 min")]
    [InlineData(119, "Here for 1 min")]
    [InlineData(120, "Here for 2 mins")]
    [InlineData(45 * 60, "Here for 45 mins")]
    [InlineData(59 * 60, "Here for 59 mins")]
    [InlineData(60 * 60, "Here for 1 hr")]
    [InlineData((61 * 60) + 59, "Here for 1 hr, 1 min")]
    [InlineData((2 * 3600) + (46 * 60), "Here for 2 hrs, 46 mins")]
    [InlineData((3 * 3600) + (33 * 60), "Here for 3 hrs, 33 mins")]
    [InlineData(3 * 3600, "Here for 3 hrs")]
    [InlineData((23 * 3600) + (59 * 60), "Here for 23 hrs, 59 mins")]
    [InlineData(24 * 3600, "Here for 1 day")]
    [InlineData((26 * 3600) + (3 * 60), "Here for 1 day, 2 hrs")]
    [InlineData((2 * 86400) + (3 * 3600) + (5 * 60), "Here for 2 days, 3 hrs")]
    [InlineData((2 * 86400) + (5 * 60), "Here for 2 days, 5 mins")]
    [InlineData(10 * 86400, "Here for 10 days")]
    public void HereForChip_WritesTheTwoLargestNonZeroUnits(int seconds, string expected)
    {
        Assert.Equal(expected, TimeFormatter.HereForChip(TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public void HereForChip_DropsTheSecondsRatherThanRoundingThemUp()
    {
        // 3 hrs, 33 mins and 59 s is still 33 minutes.
        var elapsed = new TimeSpan(3, 33, 59);

        Assert.Equal("Here for 3 hrs, 33 mins", TimeFormatter.HereForChip(elapsed));
    }
}
