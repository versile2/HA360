using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Realm.Demo;
using Realm.Domain;
using Realm.Web.Components.Driving;
using Realm.Web.Pages;
using Xunit;

namespace Realm.Web.Tests;

/// <summary>
/// The drive list's pager (01 section 6.6, D113): 25, 50 or 100 rows per page (25 by default), a pager below the list always and above it only when the rows do not fit on one
/// page, and every drive in the print table whatever the page. Also the long periods of the Driving page: the address, the title, the links and the print header.
/// </summary>
public sealed class DrivePagerTests : ComponentTestBase
{
    private static readonly TimeZoneInfo Chicago = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");

    private static readonly DateTimeOffset Start = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    // ---- the pager component -----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void ThePager_OffersTheThreeSizes_ShowsTheRange_AndDisablesPreviousOnTheFirstPage()
    {
        var cut = Pager(total: 120, page: 0, size: 25);

        Assert.Equal(["25", "50", "100"], cut.FindAll("option").Select(option => option.TextContent));
        Assert.Equal("25", cut.Find("option[selected]").TextContent);
        Assert.Equal("1–25 of 120", cut.Find("[data-testid='events-pager-range-bottom']").TextContent);
        Assert.True(cut.Find("[data-testid='events-pager-prev-bottom']").HasAttribute("disabled"));
        Assert.False(cut.Find("[data-testid='events-pager-next-bottom']").HasAttribute("disabled"));
    }

    [Fact]
    public void OnTheLastPage_NextIsDisabled_AndTheRangeEndsAtTheTotal()
    {
        var cut = Pager(total: 120, page: 4, size: 25);

        Assert.Equal("101–120 of 120", cut.Find("[data-testid='events-pager-range-bottom']").TextContent);
        Assert.True(cut.Find("[data-testid='events-pager-next-bottom']").HasAttribute("disabled"));
        Assert.False(cut.Find("[data-testid='events-pager-prev-bottom']").HasAttribute("disabled"));
    }

    [Fact]
    public async Task ThePager_ReportsThePageAndTheSizeTheUserChose()
    {
        var pages = new List<int>();
        var sizes = new List<int>();
        var cut = RenderWithProviders<EventsPager>(pager => pager
            .Add(p => p.Position, "bottom")
            .Add(p => p.Total, 120)
            .Add(p => p.Page, 1)
            .Add(p => p.PageSize, 25)
            .Add(p => p.PageChanged, (int page) =>
            {
                pages.Add(page);
                return Task.CompletedTask;
            })
            .Add(p => p.PageSizeChanged, (int size) =>
            {
                sizes.Add(size);
                return Task.CompletedTask;
            }));

        await cut.Find("[data-testid='events-pager-next-bottom']").TriggerEventAsync("onclick", new MouseEventArgs());
        await cut.Find("[data-testid='events-pager-prev-bottom']").TriggerEventAsync("onclick", new MouseEventArgs());
        await cut.Find("select").TriggerEventAsync("onchange", new ChangeEventArgs { Value = "100" });

        Assert.Equal([2, 0], pages);
        Assert.Equal([100], sizes);
    }

    // ---- the list ----------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void AListThatFitsOnOnePage_HasOnlyTheBottomPager()
    {
        var cut = Detail(Week(20));

        Assert.Empty(cut.FindAll("[data-testid='events-pager-top']"));
        Assert.Single(cut.FindAll("[data-testid='events-pager-bottom']"));
        Assert.Equal(20, cut.FindAll(".realm-drive-row").Count);
    }

    [Fact]
    public void ALongerList_ShowsTwentyFiveRows_WithAPagerOnTopAndBelow()
    {
        var cut = Detail(Week(60));

        Assert.Single(cut.FindAll("[data-testid='events-pager-top']"));
        Assert.Single(cut.FindAll("[data-testid='events-pager-bottom']"));
        Assert.Equal(25, cut.FindAll(".realm-drive-row").Count);
        Assert.Equal("1–25 of 60", cut.Find("[data-testid='events-pager-range-top']").TextContent);
        Assert.Equal("drive-row-0", cut.FindAll(".realm-drive-row").First().GetAttribute("data-testid"));
    }

    [Fact]
    public async Task NextShowsTheFollowingRows_AndEitherPagerWorks()
    {
        var cut = Detail(Week(60));

        await cut.Find("[data-testid='events-pager-next-top']").TriggerEventAsync("onclick", new MouseEventArgs());

        Assert.Equal(25, cut.FindAll(".realm-drive-row").Count);
        Assert.Equal("drive-row-25", cut.FindAll(".realm-drive-row").First().GetAttribute("data-testid"));
        Assert.Equal("26–50 of 60", cut.Find("[data-testid='events-pager-range-bottom']").TextContent);

        await cut.Find("[data-testid='events-pager-next-bottom']").TriggerEventAsync("onclick", new MouseEventArgs());

        Assert.Equal(10, cut.FindAll(".realm-drive-row").Count);
        Assert.Equal("51–60 of 60", cut.Find("[data-testid='events-pager-range-top']").TextContent);
    }

    [Fact]
    public async Task ChoosingAHundredRows_ShowsEveryDrive_AndTheTopPagerGoesAway()
    {
        var cut = Detail(Week(60));
        await cut.Find("[data-testid='events-pager-next-top']").TriggerEventAsync("onclick", new MouseEventArgs());

        await cut.Find("[data-testid='events-pager-size-bottom']").TriggerEventAsync("onchange", new ChangeEventArgs { Value = "100" });

        Assert.Equal(60, cut.FindAll(".realm-drive-row").Count);
        Assert.Empty(cut.FindAll("[data-testid='events-pager-top']"));
        Assert.Equal("1–60 of 60", cut.Find("[data-testid='events-pager-range-bottom']").TextContent);
    }

    [Fact]
    public void Printing_IgnoresThePager_EveryDriveIsARowOfThePrintTable()
    {
        var cut = Detail(Week(60));

        Assert.Equal(25, cut.FindAll(".realm-drive-row").Count);
        var rows = cut.FindAll("[data-testid='print-event-row']");
        Assert.Equal(60, rows.Count);
        Assert.Equal(7, rows[0].QuerySelectorAll("td").Length);
        // The screen controls carry the class the print stylesheet hides; the table carries the one it shows.
        Assert.All(cut.FindAll("[data-testid^='events-pager-']").Where(e => e.GetAttribute("data-testid")!.Split('-').Length == 3), pager => Assert.Contains("realm-no-print", pager.ClassList));
        Assert.Contains("realm-print-only", cut.Find("[data-testid='print-events']").ClassList);
        Assert.Contains("realm-screen-only", cut.Find("[data-testid='drive-list']").ClassList);
    }

    [Fact]
    public async Task ANewWeek_StartsOnItsFirstPage()
    {
        var cut = Detail(Week(60));
        await cut.Find("[data-testid='events-pager-next-top']").TriggerEventAsync("onclick", new MouseEventArgs());
        Assert.Equal("26–50 of 60", cut.Find("[data-testid='events-pager-range-bottom']").TextContent);

        cut.Render(parameters => parameters.Add(p => p.Data, Week(70)));

        Assert.Equal("1–25 of 70", cut.Find("[data-testid='events-pager-range-bottom']").TextContent);
    }

    // ---- the long periods on the page --------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task ThePeriodQuery_SelectsALongPeriod_TheTitleLosesWeekly_AndTheCardsLinkWithThePeriod()
    {
        await using var session = DrivingFormatterTests.Demo();
        var cut = RenderPage(session, "?period=3m");

        Assert.Equal("Driving Report", cut.Find("h1").TextContent);
        Assert.Equal("Jul 1 – Sep 30", cut.Find(".realm-driving__range").TextContent);
        Assert.Equal("Last 3 months", cut.Find("[data-testid='period-main']").TextContent.Trim());
        Assert.All(cut.FindAll("[role='radio']"), chip => Assert.Equal("false", chip.GetAttribute("aria-checked")));
        Assert.Equal("driving/jester?period=3m", cut.Find("[data-testid='driver-card-jester']").GetAttribute("href"));
        Assert.Equal("Driving Report · Last 3 months · Jul 1 – Sep 30 · printed Sep 30, 2026", cut.Find("[data-testid='print-header']").TextContent);
        Assert.Equal(4, cut.FindAll("[data-testid='print-drivers'] tbody tr").Count);
    }

    [Fact]
    public async Task ACustomRangeInTheAddress_ShowsThatRange()
    {
        await using var session = DrivingFormatterTests.Demo();
        var cut = RenderPage(session, "?period=custom&from=2026-09-01&to=2026-09-15");

        Assert.Equal("Sep 1 – Sep 15", cut.Find(".realm-driving__range").TextContent);
        Assert.Equal("Sep 1 – Sep 15", cut.Find("[data-testid='period-main']").TextContent.Trim());
        Assert.Equal("driving/jester?period=custom&from=2026-09-01&to=2026-09-15", cut.Find("[data-testid='driver-card-jester']").GetAttribute("href"));
    }

    [Theory]
    [InlineData("?period=custom&from=2026-09-20&to=2026-10-30")]   // ends in the future
    [InlineData("?period=custom&from=2020-01-01&to=2020-02-01")]   // before the history that is kept
    [InlineData("?period=custom&from=nonsense&to=2026-09-01")]
    [InlineData("?period=decade")]
    public async Task ARangeOrPeriodThatFailsTheChecks_ReadsAsThisWeek(string query)
    {
        await using var session = DrivingFormatterTests.Demo();
        var cut = RenderPage(session, query);

        Assert.Equal("Weekly Driving Report", cut.Find("h1").TextContent);
        Assert.Equal("Sep 28 – Oct 4 · so far", cut.Find(".realm-driving__range").TextContent);
    }

    [Fact]
    public async Task SixMonthsAndAYear_NeedTheRetentionToCoverThem_OtherwiseTheLinkReadsAsThisWeek()
    {
        await using var inner = DrivingFormatterTests.Demo();
        await using var shortRetention = new RetentionSession(inner, 100);
        var year = RenderPage(shortRetention, "?period=1y");
        Assert.Equal("Weekly Driving Report", year.Find("h1").TextContent);
        year.Dispose();

        await using var fullRetention = new RetentionSession(inner, 366);
        var covered = RenderPage(fullRetention, "?period=1y");
        Assert.Equal("Driving Report", covered.Find("h1").TextContent);
        Assert.Equal("Oct 1, 2025 – Sep 30, 2026", covered.Find(".realm-driving__range").TextContent);
    }

    [Fact]
    public async Task TheWeekQuery_StillWorks_AndAWeekPageLinksWithTheWeek()
    {
        await using var session = DrivingFormatterTests.Demo();
        var cut = RenderPage(session, "?week=1");

        Assert.Equal("Weekly Driving Report", cut.Find("h1").TextContent);
        Assert.Equal("Sep 21 – Sep 27", cut.Find(".realm-driving__range").TextContent);
        Assert.Equal("driving/jester?week=1", cut.Find("[data-testid='driver-card-jester']").GetAttribute("href"));
        Assert.Equal("true", cut.Find("[data-testid='week-chip-1']").GetAttribute("aria-checked"));
    }

    [Fact]
    public async Task ADriverPage_OfALongPeriod_ListsEveryDriveOfThePeriod_PagedAtTwentyFive()
    {
        await using var session = DrivingFormatterTests.Demo();
        var cut = RenderDriver(session, "jester", "?period=last-month");

        Assert.Equal("Last month · Aug 1 – Aug 31", cut.Find(".realm-driver-week__week").TextContent);
        Assert.Equal("driving?period=last-month", cut.Find("[data-testid='detail-back']").GetAttribute("href"));
        Assert.Equal(25, cut.FindAll(".realm-drive-row").Count);
        Assert.Single(cut.FindAll("[data-testid='events-pager-top']"));
        var total = int.Parse(cut.Find("[data-testid='events-pager-range-top']").TextContent.Split(" of ")[1], System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(total > 25);
        Assert.Equal(total, cut.FindAll("[data-testid='print-event-row']").Count);
    }

    [Fact]
    public async Task NoDriveOfAnyPeriod_IsEverFromOrToAnUnknownPlace()
    {
        await using var session = DrivingFormatterTests.Demo();
        var cut = RenderDriver(session, "jester", "?period=1y");
        await cut.Find("[data-testid='events-pager-size-bottom']").TriggerEventAsync("onchange", new ChangeEventArgs { Value = "100" });

        var routes = cut.FindAll(".realm-drive-row__route").Select(route => route.TextContent).ToList();
        Assert.NotEmpty(routes);
        Assert.All(routes, route => Assert.DoesNotContain("Unknown", route, StringComparison.Ordinal));
        Assert.DoesNotContain("Unknown place", cut.Markup, StringComparison.Ordinal);
    }

    // ---- helpers -----------------------------------------------------------------------------------------------------------------------------

    private IRenderedComponent<EventsPager> Pager(int total, int page, int size) =>
        RenderWithProviders<EventsPager>(pager => pager
            .Add(p => p.Position, "bottom")
            .Add(p => p.Total, total)
            .Add(p => p.Page, page)
            .Add(p => p.PageSize, size));

    private IRenderedComponent<DriverWeekDetail> Detail(DriverWeek week) =>
        RenderWithProviders<DriverWeekDetail>(detail => detail
            .Add(p => p.Data, week)
            .Add(p => p.TopSpeedMps, 27.7)
            .Add(p => p.Zone, Chicago));

    private static DriverWeek Week(int drives)
    {
        var trips = Enumerable.Range(0, drives)
            .Select(i => new DriveVm(
                Start.AddHours(-i * 3),
                Start.AddHours(-i * 3).AddMinutes(12),
                "Hearth Haven",
                "Cobblestone Court",
                1609.344 * (1 + (i % 5)),
                20 + (i % 10),
                new Dictionary<string, int?> { [EventKeys.Speeding] = i % 3, [EventKeys.Phone] = null, [EventKeys.Accel] = null, [EventKeys.Braking] = null }))
            .ToList();
        var summary = new DriverSummary("alden", drives, trips.Sum(t => t.Meters), DistanceBasis.Gps, 0, false, new Dictionary<string, int?> { [EventKeys.Speeding] = 5, [EventKeys.Phone] = null }, 5, false, true, null);
        return new DriverWeek(summary, trips);
    }

    private IRenderedComponent<DrivingPage> RenderPage(IRealmSession session, string query)
    {
        GoTo("driving" + query);
        var cut = RenderWithProviders<DrivingPage>(parameters => parameters.AddCascadingValue(session));
        cut.WaitForAssertion(() => cut.Find("[data-testid='stat-speeding']"));
        return cut;
    }

    private IRenderedComponent<DriverWeekPage> RenderDriver(IRealmSession session, string memberId, string query)
    {
        GoTo("driving/" + memberId + query);
        var cut = RenderWithProviders<DriverWeekPage>(parameters => parameters.Add(page => page.MemberId, memberId).AddCascadingValue(session));
        cut.WaitForAssertion(() => cut.Find("[data-testid='drive-row-0']"));
        return cut;
    }

    private void GoTo(string relative) => Services.GetRequiredService<NavigationManager>().NavigateTo(relative);

    // A session that is the inner one in all but the retention its snapshot reports.
    private sealed class RetentionSession(IRealmSession inner, int days) : IRealmSession
    {
        public RealmSnapshot Current => inner.Current with { RetentionFixDays = days };

        public event Action? Changed
        {
            add => inner.Changed += value;
            remove => inner.Changed -= value;
        }

        public TimeProvider Time => inner.Time;

        public TimeZoneInfo Zone => inner.Zone;

        public ValueTask<WeekReportVm> GetWeekReportAsync(int weekOffset, DayOfWeek weekStart, CancellationToken ct) => inner.GetWeekReportAsync(weekOffset, weekStart, ct);

        public ValueTask<WeekReportVm> GetPeriodReportAsync(ReportWindow window, CancellationToken ct) => inner.GetPeriodReportAsync(window, ct);

        public ValueTask<DriverWeek?> GetDriverPeriodAsync(string memberId, ReportWindow window, CancellationToken ct) => inner.GetDriverPeriodAsync(memberId, window, ct);

        public ValueTask<DriverWeek?> GetDriverWeekAsync(string memberId, int weekOffset, DayOfWeek weekStart, CancellationToken ct) =>
            inner.GetDriverWeekAsync(memberId, weekOffset, weekStart, ct);

        public string? ResolveMe(string? haUserId) => inner.ResolveMe(haUserId);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
