using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using Realm.Demo;
using Realm.Domain;
using Realm.Web.Components.Driving;
using Realm.Web.Pages;
using Xunit;

namespace Realm.Web.Tests;

/// <summary>
/// A driver's week (01 section 6.6, AC-37 and AC-41): the page at <c>driving/{memberId}</c> with the header, the four tiles, the event row and the drives grouped by day,
/// rendered from the Demo cast, and the body (<see cref="DriverWeekDetail"/>) from hand-made weeks for the states the Demo has none of. The week is the <c>?week=</c> query,
/// read on every navigation (D69); an id that is not a report driver leaves for <c>driving</c> and replaces the history entry (R2-008); a null count is "—", never 0. The
/// layout in pixels belongs to the Playwright spec.
/// </summary>
public sealed class DriverWeekPageTests : ComponentTestBase
{
    private const string Dash = "—";

    private static readonly TimeZoneInfo Chicago = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");

    // ---- the header --------------------------------------------------------------------------------------------------------------------------

    [Fact(DisplayName = "[AC-41] The page of Cass shows the name, the lore, the week, the Back link to driving?week=0 and the four tiles")]
    public async Task ThePage_OfTheDefaultWeek_ShowsTheHeader_AndTheTiles()
    {
        await using var session = DrivingFormatterTests.Demo();
        var cut = RenderPage(session, "jester", week: null);

        Assert.Equal("Cass", cut.Find("h1.realm-driver-week__name").TextContent);
        Assert.Equal(DemoCast.Jester.Lore, cut.Find(".realm-driver-week__lore").TextContent);
        Assert.Equal("This week · Sep 28 – Oct 4", cut.Find(".realm-driver-week__week").TextContent);
        Assert.Equal("C", cut.Find(".realm-driver-week__bar .realm-avatar__initial").TextContent);
        Assert.Contains("realm-avatar--ring", cut.Find(".realm-driver-week__bar .realm-avatar").ClassList);

        var back = cut.Find("a[data-testid='detail-back']");
        Assert.Equal("driving?week=0", back.GetAttribute("href"));
        Assert.Equal("Back to the driving report", back.GetAttribute("aria-label"));
        Assert.Equal("false", cut.Find("section.realm-driver-week").GetAttribute("aria-busy"));

        Assert.Equal(["Drives", "Miles", "Top speed", "Events"], cut.FindAll(".realm-week-tile__label").Select(label => label.TextContent));
        Assert.Equal(["18", "202.6", "88 mph", "38 speeding events"], cut.FindAll(".realm-week-tile__value").Select(value => value.TextContent));
    }

    [Theory]
    [InlineData("0", "This week · Sep 28 – Oct 4", "driving?week=0")]
    [InlineData("1", "Last week · Sep 21 – Sep 27", "driving?week=1")]
    [InlineData("2", "Sep 14 – Sep 20", "driving?week=2")]
    [InlineData("3", "Sep 7 – Sep 13", "driving?week=3")]
    [InlineData("9", "This week · Sep 28 – Oct 4", "driving?week=0")]
    public async Task TheWeek_IsTheQuery_TheBackLinkCarriesItAndAnythingElseIsThisWeek(string query, string label, string href)
    {
        await using var session = DrivingFormatterTests.Demo();
        var cut = RenderPage(session, "jester", query);

        Assert.Equal(label, cut.Find(".realm-driver-week__week").TextContent);
        Assert.Equal(href, cut.Find("[data-testid='detail-back']").GetAttribute("href"));
        Assert.NotEqual('/', href[0]);
    }

    [Fact]
    public async Task LastWeek_ShowsTheDriversFiguresOfThatWeek_WithTheirTopSpeedFromTheReport()
    {
        await using var session = DrivingFormatterTests.Demo();
        var cut = RenderPage(session, "jester", "1");

        Assert.Equal(["20", "215.2", "92 mph", "41 speeding events"], cut.FindAll(".realm-week-tile__value").Select(value => value.TextContent));
        Assert.Equal(20, cut.FindAll(".realm-drive-row").Count);
    }

    [Fact]
    public async Task TheWeek_IsReadOnEveryNavigation_WithoutLeavingThePage()
    {
        await using var session = DrivingFormatterTests.Demo();
        var cut = RenderPage(session, "jester", "0");
        Assert.Equal(18, cut.FindAll(".realm-drive-row").Count);

        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo(navigation.GetUriWithQueryParameter("week", "1"));

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Last week · Sep 21 – Sep 27", cut.Find(".realm-driver-week__week").TextContent);
            Assert.Equal("driving?week=1", cut.Find("[data-testid='detail-back']").GetAttribute("href"));
            Assert.Equal(20, cut.FindAll(".realm-drive-row").Count);
        });
    }

    // ---- the event row -----------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task TheEventRow_HasTheFourTypes_SpeedingIsSampled_AndAnUnrecordedCountIsADash()
    {
        await using var session = DrivingFormatterTests.Demo();
        var cut = RenderPage(session, "jester", week: null);

        var types = cut.FindAll(".realm-week-event");
        Assert.Equal(["speeding", "phone", "accel", "braking"], types.Select(type => type.GetAttribute("data-event")));
        Assert.Equal(["Speeding", "Phone use", "Rapid accel.", "Hard braking"], types.Select(type => type.QuerySelector(".realm-week-event__label")!.TextContent));
        Assert.Equal(["38", Dash, Dash, Dash], types.Select(type => type.QuerySelector(".realm-week-event__count")!.TextContent));
        Assert.Equal(["sampled"], cut.FindAll(".realm-week-event__caption").Select(caption => caption.TextContent));
        Assert.Single(cut.FindAll("[data-event='speeding'] .realm-week-event__caption"));
    }

    [Fact]
    public async Task Alden_HasAPhoneCount_SoTheEventsTileAndTheRowShowIt()
    {
        await using var session = DrivingFormatterTests.Demo();
        var cut = RenderPage(session, "king", week: null);

        Assert.Equal(["22", "94.4", "96 mph", "6 speeding · 60 phone"], cut.FindAll(".realm-week-tile__value").Select(value => value.TextContent));
        Assert.Equal(["6", "60", Dash, Dash], cut.FindAll(".realm-week-event__count").Select(count => count.TextContent));
    }

    // ---- the drives --------------------------------------------------------------------------------------------------------------------------

    [Fact(DisplayName = "[AC-41] Cass's 18 drives are grouped under three day headers, newest first, with the chips of the non-zero counts only")]
    public async Task TheDrives_AreGroupedByDay_NewestFirst_WithChipsOnlyForNonZeroCounts()
    {
        await using var session = DrivingFormatterTests.Demo();
        var cut = RenderPage(session, "jester", week: null);

        var days = cut.FindAll("section.realm-drive-day");
        Assert.Equal(["Wed, Sep 30", "Tue, Sep 29", "Mon, Sep 28"], days.Select(day => day.QuerySelector(".realm-drive-day__header")!.TextContent));
        Assert.Equal([6, 4, 8], days.Select(day => day.QuerySelectorAll("li.realm-drive-row").Length));
        Assert.Equal(18, cut.FindAll("li.realm-drive-row").Count);
        Assert.Equal(Enumerable.Range(0, 18).Select(i => $"drive-row-{i}"), cut.FindAll("li.realm-drive-row").Select(row => row.GetAttribute("data-testid")));

        var newest = cut.Find("[data-testid='drive-row-0']");
        Assert.Equal("9:01 – 9:06 pm", newest.QuerySelector(".realm-drive-row__time")!.TextContent);
        Assert.Equal("Hearth Haven → The Jester's Hall", newest.QuerySelector(".realm-drive-row__route")!.TextContent);
        Assert.Equal("1.1 mi · Top 38 mph", newest.QuerySelector(".realm-drive-row__detail")!.TextContent);
        Assert.Empty(newest.QuerySelectorAll(".realm-drive-event"));
        Assert.Empty(newest.QuerySelectorAll(".realm-drive-row__events"));

        // Only the drives with speeding events carry a chip, and the chips add up to the week's 38.
        var chips = cut.FindAll(".realm-drive-event");
        Assert.All(chips, chip => Assert.Contains("realm-drive-event--speeding", chip.ClassList));
        Assert.Equal(38, chips.Sum(chip => int.Parse(chip.QuerySelector(".realm-drive-event__count")!.TextContent, System.Globalization.CultureInfo.InvariantCulture)));
        Assert.Equal("4 speeding events", cut.Find("[data-testid='drive-row-2'] .realm-drive-event").GetAttribute("aria-label"));
        Assert.Equal("img", cut.Find("[data-testid='drive-row-2'] .realm-drive-event").GetAttribute("role"));
        Assert.Equal("4", cut.Find("[data-testid='drive-row-2'] .realm-drive-event__count").TextContent);
    }

    [Fact]
    public async Task TheDriveRows_AreNotButtonsOrLinks_InV1()
    {
        await using var session = DrivingFormatterTests.Demo();
        var cut = RenderPage(session, "jester", week: null);

        Assert.Empty(cut.FindAll(".realm-drive-row button, .realm-drive-row a"));
        Assert.All(cut.FindAll(".realm-drive-row"), row => Assert.Equal("li", row.TagName.ToLowerInvariant()));
    }

    [Fact]
    public async Task TheCaption_SaysTheDistancesAreGpsEstimated()
    {
        await using var session = DrivingFormatterTests.Demo();
        var cut = RenderPage(session, "jester", week: null);

        Assert.Equal("Distances are GPS-estimated (about 2–5 % low on winding roads).", cut.Find(".realm-driving__caption").TextContent);
    }

    [Fact]
    public async Task NothingOnThePage_NamesTheOriginalSource()
    {
        await using var session = DrivingFormatterTests.Demo("all-sources");
        var cut = RenderPage(session, "jester", week: null);

        Assert.DoesNotContain("Life360", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }

    // ---- a week nobody was recorded in -------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task AWeekWithNoRecord_SaysSo_WithDashesForEveryFigure_AndNoDrivesOrCaption()
    {
        await using var session = DrivingFormatterTests.Demo("fresh-install");
        var cut = RenderPage(session, "jester", "2", ready: "p.realm-driving__quiet");

        Assert.Equal("Sep 14 – Sep 20", cut.Find(".realm-driver-week__week").TextContent);
        Assert.Equal("The scribes have no record of this week.", cut.Find("p.realm-driving__quiet").TextContent);
        Assert.Equal([Dash, Dash, Dash, Dash], cut.FindAll(".realm-week-tile__value").Select(value => value.TextContent));
        Assert.Equal([Dash, Dash, Dash, Dash], cut.FindAll(".realm-week-event__count").Select(count => count.TextContent));
        Assert.Empty(cut.FindAll(".realm-drive-row"));
        Assert.Empty(cut.FindAll(".realm-drive-day"));
        Assert.Empty(cut.FindAll(".realm-driving__caption"));
        Assert.Equal("driving?week=2", cut.Find("[data-testid='detail-back']").GetAttribute("href"));
    }

    // ---- an id that is not a report driver ---------------------------------------------------------------------------------------------------

    [Theory(DisplayName = "[AC-37] A static member and an unknown id leave for driving, replacing the history entry")]
    [InlineData("prince", null)]
    [InlineData("nobody", null)]
    [InlineData("prince", "2")]
    public async Task AnIdThatIsNotAReportDriver_RedirectsToDriving_WithReplace(string memberId, string? week)
    {
        await using var session = DrivingFormatterTests.Demo();
        var navigation = (BunitNavigationManager)Services.GetRequiredService<NavigationManager>();
        var cut = RenderAt(session, memberId, week);

        cut.WaitForAssertion(() =>
        {
            var redirect = Assert.Single(navigation.History, entry => entry.Uri == "driving");
            Assert.True(redirect.Options.ReplaceHistoryEntry);
            Assert.Equal("http://localhost/driving", navigation.Uri);
        });
        Assert.Empty(cut.FindAll(".realm-week-tile"));
        Assert.Empty(cut.FindAll(".realm-drive-row"));
    }

    [Fact]
    public async Task AReportDriver_IsNotRedirected()
    {
        await using var session = DrivingFormatterTests.Demo();
        var navigation = (BunitNavigationManager)Services.GetRequiredService<NavigationManager>();
        var cut = RenderPage(session, "king", week: null);

        Assert.Equal(22, cut.FindAll(".realm-drive-row").Count);
        Assert.Empty(navigation.History);
    }

    // ---- when the data cannot be read --------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task WhenTheWeekCannotBeRead_TheBannerSaysSo_TheBackLinkStays_AndThereIsNoRedirect()
    {
        await using var inner = DrivingFormatterTests.Demo();
        var session = new WrappedSession(inner)
        {
            OnDriverWeek = (_, _, _, _) => throw new InvalidOperationException("The store is gone."),
        };
        var navigation = (BunitNavigationManager)Services.GetRequiredService<NavigationManager>();

        var cut = RenderAt(session, "jester", null);

        cut.WaitForAssertion(() => Assert.Equal("The scribes can't reach the records right now. Showing what we have.", cut.Find("p.realm-driving__banner").TextContent));
        Assert.Equal("false", cut.Find("section.realm-driver-week").GetAttribute("aria-busy"));
        Assert.Equal("Cass", cut.Find("h1.realm-driver-week__name").TextContent);
        Assert.Equal("driving?week=0", cut.Find("[data-testid='detail-back']").GetAttribute("href"));
        Assert.Empty(cut.FindAll(".realm-week-tile"));
        Assert.Empty(navigation.History);
    }

    [Fact]
    public async Task WhenOnlyTheReportCannotBeRead_TheTopSpeedIsADash_AndThereIsNoBanner()
    {
        await using var inner = DrivingFormatterTests.Demo();
        var session = new WrappedSession(inner)
        {
            OnReport = (_, _, _) => throw new InvalidOperationException("The report is gone."),
        };

        var cut = RenderAt(session, "jester", null);

        cut.WaitForAssertion(() => Assert.Equal(["18", "202.6", Dash, "38 speeding events"], cut.FindAll(".realm-week-tile__value").Select(value => value.TextContent)));
        Assert.Empty(cut.FindAll("p.realm-driving__banner"));
        Assert.Equal(18, cut.FindAll(".realm-drive-row").Count);
    }

    // ---- the body, from hand-made weeks ------------------------------------------------------------------------------------------------------

    [Fact]
    public void ADriverWithNoDrives_GetsTheQuietSentence_AndRealZeros_NotDashes()
    {
        var week = new DriverWeek(DrivingFormatterTests.Driver(drives: 0, miles: 0, speeding: 0), []);

        var cut = Body(week, topSpeedMps: null);

        Assert.Equal("No drives this week. The roads are quiet.", cut.Find("p.realm-driving__quiet").TextContent);
        Assert.Equal("status", cut.Find("p.realm-driving__quiet").GetAttribute("role"));
        Assert.Equal(["0", "0.0", Dash, "No events"], cut.FindAll(".realm-week-tile__value").Select(value => value.TextContent));
        Assert.Equal(["0", Dash, Dash, Dash], cut.FindAll(".realm-week-event__count").Select(count => count.TextContent));
        Assert.Empty(cut.FindAll(".realm-drive-day"));
        Assert.Empty(cut.FindAll(".realm-driving__caption"));
    }

    [Fact]
    public void ADriverWhoWasNotCovered_GetsTheNoRecordSentence_AndNothingIsZero()
    {
        var week = new DriverWeek(DrivingFormatterTests.Driver(covered: false, drives: null, miles: null, speeding: null, eventsTotal: null), []);

        var cut = Body(week, topSpeedMps: 42.9);

        Assert.Equal("The scribes have no record of this week.", cut.Find("p.realm-driving__quiet").TextContent);
        Assert.Equal([Dash, Dash, Dash, Dash], cut.FindAll(".realm-week-tile__value").Select(value => value.TextContent));
        Assert.Equal([Dash, Dash, Dash, Dash], cut.FindAll(".realm-week-event__count").Select(count => count.TextContent));
    }

    [Fact]
    public void ASparseDriver_TheEventsTileHasTheAsterisk_AndItsTooltip()
    {
        var week = new DriverWeek(DrivingFormatterTests.Driver(speeding: 38, coarseTrips: 2), []);

        var cut = Body(week, topSpeedMps: null);

        Assert.Equal("38* speeding events", cut.Find(".realm-week-tile__value--text").TextContent);
        Assert.Equal("Some drives were too sparse to measure speed", cut.FindComponent<MudTooltip>().Instance.Text);
    }

    [Fact]
    public void ADriveRow_DrawsAChipOnlyForACountAboveZero_ASingularName_AndUnnamedPlaces()
    {
        var start = new DateTimeOffset(2026, 9, 30, 13, 0, 0, TimeSpan.Zero);
        var events = new Dictionary<string, int?> { [EventKeys.Speeding] = 1, [EventKeys.Phone] = 0, [EventKeys.Accel] = null, [EventKeys.Braking] = 2 };
        var trip = new DriveVm(start, start.AddMinutes(20), null, null, 8046.72, null, events);
        var week = new DriverWeek(DrivingFormatterTests.Driver(drives: 1, miles: 5), [trip]);

        var cut = Body(week, topSpeedMps: null);

        var row = cut.Find("[data-testid='drive-row-0']");
        Assert.Equal("Somewhere in the Realm → Somewhere in the Realm", row.QuerySelector(".realm-drive-row__route")!.TextContent);
        Assert.Equal("5.0 mi · Top —", row.QuerySelector(".realm-drive-row__detail")!.TextContent);
        Assert.Equal(["1 speeding event", "2 hard-braking events"], row.QuerySelectorAll(".realm-drive-event").Select(chip => chip.GetAttribute("aria-label")));
        Assert.Equal(["1", "2"], row.QuerySelectorAll(".realm-drive-event__count").Select(count => count.TextContent));
    }

    // ---- helpers -----------------------------------------------------------------------------------------------------------------------------

    // Renders the page and waits until the drives (or the sentence that stands in for them) are on it.
    private IRenderedComponent<DriverWeekPage> RenderPage(IRealmSession session, string memberId, string? week, string ready = "[data-testid='drive-row-0']")
    {
        var cut = RenderAt(session, memberId, week);
        cut.WaitForAssertion(() => cut.Find(ready));
        return cut;
    }

    // The week is the query, as D69 reads it: the page is rendered at driving/{memberId}?week=n, with the session RealmShell would cascade.
    private IRenderedComponent<DriverWeekPage> RenderAt(IRealmSession session, string memberId, string? week)
    {
        if (week is not null)
        {
            var navigation = Services.GetRequiredService<NavigationManager>();
            navigation.NavigateTo(navigation.GetUriWithQueryParameter("week", week));
        }

        return RenderWithProviders<DriverWeekPage>(parameters => parameters
            .Add(page => page.MemberId, memberId)
            .AddCascadingValue(session));
    }

    private IRenderedComponent<DriverWeekDetail> Body(DriverWeek week, double? topSpeedMps) =>
        RenderWithProviders<DriverWeekDetail>(parameters => parameters
            .Add(detail => detail.Data, week)
            .Add(detail => detail.TopSpeedMps, topSpeedMps)
            .Add(detail => detail.Zone, Chicago));

    // The Demo session with a read or two replaced, to say what the page does when one of them fails.
    private sealed class WrappedSession : IRealmSession
    {
        private readonly IRealmSession _inner;

        public WrappedSession(IRealmSession inner)
        {
            _inner = inner;
        }

        public Func<string, int, DayOfWeek, CancellationToken, ValueTask<DriverWeek?>>? OnDriverWeek { get; init; }

        public Func<int, DayOfWeek, CancellationToken, ValueTask<WeekReportVm>>? OnReport { get; init; }

        public RealmSnapshot Current => _inner.Current;

        public event Action? Changed
        {
            add => _inner.Changed += value;
            remove => _inner.Changed -= value;
        }

        public TimeProvider Time => _inner.Time;

        public TimeZoneInfo Zone => _inner.Zone;

        public ValueTask<WeekReportVm> GetWeekReportAsync(int weekOffset, DayOfWeek weekStart, CancellationToken ct) =>
            OnReport is null ? _inner.GetWeekReportAsync(weekOffset, weekStart, ct) : OnReport(weekOffset, weekStart, ct);

        public ValueTask<WeekReportVm> GetPeriodReportAsync(ReportWindow window, CancellationToken ct) => _inner.GetPeriodReportAsync(window, ct);

        public ValueTask<DriverWeek?> GetDriverPeriodAsync(string memberId, ReportWindow window, CancellationToken ct) => _inner.GetDriverPeriodAsync(memberId, window, ct);

        public ValueTask<DriverWeek?> GetDriverWeekAsync(string memberId, int weekOffset, DayOfWeek weekStart, CancellationToken ct) =>
            OnDriverWeek is null ? _inner.GetDriverWeekAsync(memberId, weekOffset, weekStart, ct) : OnDriverWeek(memberId, weekOffset, weekStart, ct);

        public string? ResolveMe(string? haUserId) => _inner.ResolveMe(haUserId);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
