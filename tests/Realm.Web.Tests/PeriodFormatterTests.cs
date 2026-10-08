using Realm.Domain;
using Realm.Web.Formatting;
using Xunit;

namespace Realm.Web.Tests;

/// <summary>The words of the period control and the printed report (01 sections 6.2 and 6.10), and the pager rules (6.6). All pure.</summary>
public sealed class PeriodFormatterTests
{
    private static readonly TimeZoneInfo Chicago = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 21, 25, 0, TimeSpan.FromHours(-5));

    private static ReportWindow Window(ReportPeriod period) => PeriodMath.Resolve(period, Now, DayOfWeek.Monday, Chicago);

    [Theory]
    [InlineData(PeriodKind.LastMonth, "Last month")]
    [InlineData(PeriodKind.Last3Months, "Last 3 months")]
    [InlineData(PeriodKind.Last6Months, "Last 6 months")]
    [InlineData(PeriodKind.LastYear, "Last year")]
    [InlineData(PeriodKind.Custom, "Custom range…")]
    public void TheMenu_NamesEachPeriod(PeriodKind kind, string text)
    {
        Assert.Equal(text, PeriodFormatter.MenuText(kind));
    }

    [Fact]
    public void TheSplitButton_ReadsTheLastLongPeriod_AndACustomRangeReadsAsItsDates()
    {
        Assert.Equal("Last month", PeriodFormatter.SplitText(new ReportPeriod(PeriodKind.LastMonth)));
        Assert.Equal("Last 6 months", PeriodFormatter.SplitText(new ReportPeriod(PeriodKind.Last6Months)));
        Assert.Equal("Aug 1 – Aug 15", PeriodFormatter.SplitText(ReportPeriod.OfRange(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 15))));
        Assert.Equal("Dec 29, 2025 – Jan 4, 2026", PeriodFormatter.SplitText(ReportPeriod.OfRange(new DateOnly(2025, 12, 29), new DateOnly(2026, 1, 4))));
        Assert.Equal("Last month", PeriodFormatter.SplitText(ReportPeriod.ThisWeek));
    }

    [Fact]
    public void TheHeading_NamesTheWeekChipsAsBefore_AndTheLongPeriodsWithTheirRange()
    {
        Assert.Equal("This week · Sep 28 – Oct 4", PeriodFormatter.Heading(Window(ReportPeriod.ThisWeek)));
        Assert.Equal("Last week · Sep 21 – Sep 27", PeriodFormatter.Heading(Window(ReportPeriod.OfWeek(1))));
        Assert.Equal("Sep 14 – Sep 20", PeriodFormatter.Heading(Window(ReportPeriod.OfWeek(2))));
        Assert.Equal("Last month · Aug 1 – Aug 31", PeriodFormatter.Heading(Window(new ReportPeriod(PeriodKind.LastMonth))));
        Assert.Equal("Last 3 months · Jul 1 – Sep 30", PeriodFormatter.Heading(Window(new ReportPeriod(PeriodKind.Last3Months))));
        Assert.Equal("Last year · Oct 1, 2025 – Sep 30, 2026", PeriodFormatter.Heading(Window(new ReportPeriod(PeriodKind.LastYear))));
        Assert.Equal("Aug 1 – Aug 15", PeriodFormatter.Heading(Window(ReportPeriod.OfRange(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 15)))));
    }

    [Fact]
    public void TheTitle_IsWeeklyForAWeek_AndPlainForALongPeriod()
    {
        Assert.Equal("Weekly Driving Report", PeriodFormatter.Title(ReportPeriod.ThisWeek));
        Assert.Equal("Driving Report", PeriodFormatter.Title(new ReportPeriod(PeriodKind.Last3Months)));
    }

    [Fact]
    public void ThePrintHeader_IsTheTitle_ThePeriod_AndThePrintingDate()
    {
        var date = new DateOnly(2026, 10, 8);

        Assert.Equal("Driving Report · Last month · Aug 1 – Aug 31 · printed Oct 8, 2026", PeriodFormatter.PrintHeader(Window(new ReportPeriod(PeriodKind.LastMonth)), date));
        Assert.Equal("Driving Report · This week · Sep 28 – Oct 4 · printed Oct 8, 2026", PeriodFormatter.PrintHeader(Window(ReportPeriod.ThisWeek), date));
        Assert.Equal("Driving Report · Alden · This week · Sep 28 – Oct 4 · printed Oct 8, 2026", PeriodFormatter.PrintHeader(Window(ReportPeriod.ThisWeek), date, "Alden"));
    }

    [Fact]
    public void ThePrintFooter_HoldsTheThresholds_OrTheDefaults()
    {
        var footer = PeriodFormatter.PrintFooter(new DrivingThresholds(75.5, 20, 12));

        Assert.Contains("above 75.5 mph for at least 20 s", footer, StringComparison.Ordinal);
        Assert.Contains("screen use of at least 12 s", footer, StringComparison.Ordinal);
        Assert.Contains("Distances are GPS-estimated", footer, StringComparison.Ordinal);
        Assert.Contains("above 80 mph for at least 30 s", PeriodFormatter.PrintFooter(null), StringComparison.Ordinal);
    }

    [Fact]
    public void TheRangeMessages_SayWhichRuleFailed_AndAnExceededHistoryNamesTheOldestDay()
    {
        var oldest = new DateOnly(2026, 6, 22);

        Assert.Equal(string.Empty, PeriodFormatter.RangeMessage(new RangeCheck(RangeError.None, oldest), 100));
        Assert.Equal("Choose a start date and an end date.", PeriodFormatter.RangeMessage(new RangeCheck(RangeError.Missing, oldest), 100));
        Assert.Equal("The start date must be on or before the end date.", PeriodFormatter.RangeMessage(new RangeCheck(RangeError.EndBeforeStart, oldest), 100));
        Assert.Equal("The end date can't be after today.", PeriodFormatter.RangeMessage(new RangeCheck(RangeError.InFuture, oldest), 100));
        Assert.Equal(
            "The Realm keeps 100 days of history, back to Jun 22, 2026. Choose a start on or after that day.",
            PeriodFormatter.RangeMessage(new RangeCheck(RangeError.BeforeHistory, oldest), 100));
    }

    [Fact]
    public void TheDriverLinks_CarryTheWeekAsBefore_OrThePeriodWithItsDates()
    {
        Assert.Equal("driving/jester?week=2", DrivingFormatter.DriverHref("jester", ReportPeriod.OfWeek(2)));
        Assert.Equal("driving/jester?period=6m", DrivingFormatter.DriverHref("jester", new ReportPeriod(PeriodKind.Last6Months)));
        Assert.Equal("driving/jester?period=custom&from=2026-08-01&to=2026-08-15", DrivingFormatter.DriverHref("jester", ReportPeriod.OfRange(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 15))));
        Assert.Equal("driving?week=1", DrivingFormatter.BackHref(ReportPeriod.OfWeek(1)));
        Assert.Equal("driving?period=1y", DrivingFormatter.BackHref(new ReportPeriod(PeriodKind.LastYear)));
    }

    [Fact]
    public void ALongPeriod_UsesPeriodWordsInsteadOfWeekWords()
    {
        Assert.Equal("in this period", StatNameFormatter.Period(StatNameFormatter.LongPeriod));
        Assert.Equal("the period before", StatNameFormatter.Previous(StatNameFormatter.LongPeriod));
        Assert.Equal("7 more than the period before", StatNameFormatter.Difference(7, StatNameFormatter.LongPeriod));
        Assert.Equal("this week", StatNameFormatter.Period(0));
        Assert.Equal("last week", StatNameFormatter.Previous(0));
        Assert.Equal("No drives in this period · resting in the castle", DrivingFormatter.DriverLine(Driver(0), longPeriod: true));
        Assert.Equal("No drives this week · resting in the castle", DrivingFormatter.DriverLine(Driver(0)));
    }

    [Fact]
    public void NoPlace_IsEverNamedUnknown()
    {
        Assert.DoesNotContain("Unknown", DrivingFormatter.UnnamedPlace, StringComparison.Ordinal);
        Assert.Equal("Somewhere in the Realm", DrivingFormatter.UnnamedPlace);
    }

    // ---- the pager rules ---------------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(0, 25, false, false)]
    [InlineData(1, 25, false, true)]
    [InlineData(25, 25, false, true)]
    [InlineData(26, 25, true, true)]
    [InlineData(100, 100, false, true)]
    [InlineData(101, 100, true, true)]
    [InlineData(60, 50, true, true)]
    [InlineData(60, 7, true, true)]    // an unknown size reads as the default of 25: 60 rows need the top pager
    public void TheTopPager_ShowsOnlyWhenTheRowsDoNotFitOnOnePage_AndTheBottomWheneverThereIsARow(int total, int size, bool top, bool bottom)
    {
        Assert.Equal(top, PagerMath.ShowTop(total, size));
        Assert.Equal(bottom, PagerMath.ShowBottom(total));
    }

    [Fact]
    public void ThePager_OffersTwentyFiveFiftyAndAHundredRows_TwentyFiveByDefault()
    {
        Assert.Equal([25, 50, 100], PagerMath.PageSizes);
        Assert.Equal(25, PagerMath.DefaultPageSize);
        Assert.Equal(25, PagerMath.NormalizeSize(0));
        Assert.Equal(50, PagerMath.NormalizeSize(50));
    }

    [Theory]
    [InlineData(0, 25, 1)]
    [InlineData(25, 25, 1)]
    [InlineData(26, 25, 2)]
    [InlineData(120, 50, 3)]
    [InlineData(1000, 100, 10)]
    public void ThePageCount_RoundsUp_AndIsNeverZero(int total, int size, int pages)
    {
        Assert.Equal(pages, PagerMath.PageCount(total, size));
    }

    [Fact]
    public void ThePageIsHeldInsideTheExistingPages_AndTheRangeTextSaysWhichRowsShow()
    {
        Assert.Equal(0, PagerMath.ClampPage(-3, 120, 25));
        Assert.Equal(4, PagerMath.ClampPage(99, 120, 25));
        Assert.Equal(100, PagerMath.Skip(4, 120, 25));
        Assert.Equal("1–25 of 120", PagerMath.Summary(120, 0, 25));
        Assert.Equal("101–120 of 120", PagerMath.Summary(120, 4, 25));
        Assert.Equal("1–18 of 18", PagerMath.Summary(18, 0, 25));
        Assert.Equal("0 of 0", PagerMath.Summary(0, 0, 25));
    }

    private static DriverSummary Driver(int drives) =>
        new("alden", drives, 0, DistanceBasis.Gps, 0, true, new Dictionary<string, int?>(), 0, false, true, null);
}
