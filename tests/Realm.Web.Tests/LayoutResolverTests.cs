using Realm.Web.Layout;
using Xunit;

namespace Realm.Web.Tests;

/// <summary>
/// <see cref="LayoutResolver"/> (01 section 3.1, 03 section 3.6): the bottom sheet below 840 px wide <b>or</b> below 560 px high, the left panel from 840 x 560 up,
/// the user's override over both, and the panel's <c>CurrentSize</c> (the percent of the width that makes 400 px). Pure: no browser, no component.
/// </summary>
public sealed class LayoutResolverTests
{
    [Fact]
    public void TheThresholds_AreThoseOfTheSpec()
    {
        Assert.Equal(840, LayoutResolver.ExpandedMinWidthPx);
        Assert.Equal(560, LayoutResolver.ExpandedMinHeightPx);
        Assert.Equal(400, LayoutResolver.PanelWidthPx);
    }

    // The four Playwright viewports first, then the rows of the plan's test list (700x900, 915x412, 839, 840, 1440x900, 900x500, 884x916) and the edges.
    [Theory]
    [InlineData(412.0, 915.0, LayoutMode.Compact)]       // phone
    [InlineData(412.0, 800.0, LayoutMode.Compact)]       // phone-short
    [InlineData(884.0, 916.0, LayoutMode.Expanded)]      // unfolded
    [InlineData(884.0, 1104.0, LayoutMode.Expanded)]     // unfolded-tall
    [InlineData(700.0, 900.0, LayoutMode.Compact)]       // too narrow, plenty high
    [InlineData(915.0, 412.0, LayoutMode.Compact)]       // a landscape phone: wide enough, too low (by height)
    [InlineData(839.0, 900.0, LayoutMode.Compact)]       // one pixel short of the width
    [InlineData(840.0, 900.0, LayoutMode.Expanded)]      // the width threshold itself
    [InlineData(1440.0, 900.0, LayoutMode.Expanded)]     // desktop
    [InlineData(900.0, 500.0, LayoutMode.Compact)]       // wide enough, low: Compact by height
    [InlineData(900.0, 559.0, LayoutMode.Compact)]       // one pixel short of the height
    [InlineData(900.0, 560.0, LayoutMode.Expanded)]      // the height threshold itself
    [InlineData(840.0, 560.0, LayoutMode.Expanded)]      // both thresholds at once
    [InlineData(839.0, 559.0, LayoutMode.Compact)]       // neither
    [InlineData(839.5, 900.0, LayoutMode.Compact)]       // a fractional width is not rounded up
    public void Auto_IsCompactBelow840WideOrBelow560High(double width, double height, LayoutMode expected) =>
        Assert.Equal(expected, LayoutResolver.Resolve(width, height));

    [Theory(DisplayName = "[AC-10] the Compact sheet is a bottom sheet at 700 x 900 and at 915 x 412 (a landscape phone is not a panel)")]
    [InlineData(700.0, 900.0)]
    [InlineData(915.0, 412.0)]
    [InlineData(412.0, 915.0)]
    [InlineData(412.0, 800.0)]
    public void AC10_TheCompactSheetIsABottomSheet(double width, double height) =>
        Assert.Equal(LayoutMode.Compact, LayoutResolver.Resolve(width, height));

    [Theory(DisplayName = "[AC-11] 840 wide is Expanded, 839 wide is Compact, 1440 x 900 is Expanded with the panel still 400 wide (CurrentSize 28)")]
    [InlineData(840.0, 900.0, LayoutMode.Expanded, 48)]
    [InlineData(839.0, 900.0, LayoutMode.Compact, 48)]
    [InlineData(1440.0, 900.0, LayoutMode.Expanded, 28)]
    [InlineData(884.0, 916.0, LayoutMode.Expanded, 45)]
    [InlineData(884.0, 1104.0, LayoutMode.Expanded, 45)]
    [InlineData(915.0, 412.0, LayoutMode.Compact, 44)]
    public void AC11_ThePanelStartsAt840x560_AndIsAlways400Wide(double width, double height, LayoutMode expected, int percentOfTheWidth)
    {
        Assert.Equal(expected, LayoutResolver.Resolve(width, height));
        Assert.Equal(percentOfTheWidth, LayoutResolver.PanelSizePercent(width));
        Assert.InRange(width * percentOfTheWidth / 100.0, 400 - width * 0.005, 400 + width * 0.005);   // CurrentSize is a whole percent: 400 px to within half a percent of the width
    }

    [Theory(DisplayName = "[AC-11] realm.layout=sheet at 884 wide shows the bottom sheet; realm.layout=panel at 700 wide shows the panel")]
    [InlineData("sheet", 884.0, 916.0, LayoutMode.Compact)]
    [InlineData("panel", 700.0, 900.0, LayoutMode.Expanded)]
    [InlineData("panel", 412.0, 915.0, LayoutMode.Expanded)]
    [InlineData("auto", 884.0, 916.0, LayoutMode.Expanded)]
    [InlineData(null, 700.0, 900.0, LayoutMode.Compact)]
    public void AC11_TheOverrideBeatsTheViewport(string? setting, double width, double height, LayoutMode expected) =>
        Assert.Equal(expected, LayoutResolver.Resolve(width, height, LayoutResolver.ParseOverride(setting)));

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(double.NaN, 900.0)]
    [InlineData(900.0, double.NaN)]
    [InlineData(-1.0, -1.0)]
    [InlineData(double.PositiveInfinity, 0.0)]
    public void Auto_AViewportThatIsNotAUsableNumber_IsCompact_NeverUnknown(double width, double height) =>
        Assert.Equal(LayoutMode.Compact, LayoutResolver.Resolve(width, height));

    [Theory]
    [InlineData(1440.0, 900.0)]
    [InlineData(884.0, 916.0)]
    [InlineData(840.0, 560.0)]
    [InlineData(412.0, 915.0)]
    public void TheSheetOverride_IsCompactAtEveryViewport(double width, double height) =>
        Assert.Equal(LayoutMode.Compact, LayoutResolver.Resolve(width, height, LayoutOverride.Sheet));

    [Theory]
    [InlineData(412.0, 915.0)]
    [InlineData(915.0, 412.0)]
    [InlineData(839.0, 900.0)]
    [InlineData(1440.0, 900.0)]
    public void ThePanelOverride_IsExpandedAtEveryViewport(double width, double height) =>
        Assert.Equal(LayoutMode.Expanded, LayoutResolver.Resolve(width, height, LayoutOverride.Panel));

    [Fact]
    public void TheAutoOverride_IsTheDefault_AndChangesNothing()
    {
        Assert.Equal(LayoutResolver.Resolve(884, 916), LayoutResolver.Resolve(884, 916, LayoutOverride.Auto));
        Assert.Equal(LayoutResolver.Resolve(412, 915), LayoutResolver.Resolve(412, 915, LayoutOverride.Auto));
    }

    [Theory]
    [InlineData("auto", LayoutOverride.Auto)]
    [InlineData("sheet", LayoutOverride.Sheet)]
    [InlineData("panel", LayoutOverride.Panel)]
    [InlineData(null, LayoutOverride.Auto)]
    [InlineData("", LayoutOverride.Auto)]
    [InlineData("desktop", LayoutOverride.Auto)]
    [InlineData("Sheet", LayoutOverride.Auto)]   // the values are the lower-case words of Appendix B; anything else is ignored
    public void ParseOverride_TakesTheThreeWords_AndIgnoresAnythingElse(string? value, LayoutOverride expected) =>
        Assert.Equal(expected, LayoutResolver.ParseOverride(value));

    // 01 section 3.6: round(100 x 400 / width), 884 gives 45; the range MudXSheet accepts is 10..100.
    [Theory]
    [InlineData(884.0, 45)]
    [InlineData(840.0, 48)]
    [InlineData(915.0, 44)]
    [InlineData(1024.0, 39)]
    [InlineData(1440.0, 28)]
    [InlineData(1920.0, 21)]
    [InlineData(400.0, 100)]
    [InlineData(300.0, 100)]      // clamped: the panel is never wider than the window
    [InlineData(4000.0, 10)]
    [InlineData(10000.0, 10)]     // clamped to the smallest size MudX accepts
    public void PanelSizePercent_IsThePercentOfTheWidthThatMakes400Px(double width, int expected) =>
        Assert.Equal(expected, LayoutResolver.PanelSizePercent(width));

    [Theory]
    [InlineData(0.0)]
    [InlineData(-5.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void PanelSizePercent_AWidthThatIsNotAUsableNumber_IsTheLargestSize(double width) =>
        Assert.Equal(LayoutResolver.MaxSizePercent, LayoutResolver.PanelSizePercent(width));

    [Fact]
    public void PanelSizePercent_NeverLeavesTheRangeOfMudXSheet()
    {
        for (var width = 1.0; width <= 8000.0; width += 7.0)
        {
            var percent = LayoutResolver.PanelSizePercent(width);
            Assert.InRange(percent, LayoutResolver.MinSizePercent, LayoutResolver.MaxSizePercent);
        }
    }
}
