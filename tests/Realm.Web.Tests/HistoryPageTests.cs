using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Realm.Domain;
using Realm.Web.Components.History;
using Realm.Web.Pages;
using Realm.Web.State;
using Xunit;

namespace Realm.Web.Tests;

/// <summary>
/// Location History (0.3.0, D123): the page at <c>history/{memberId}</c> rendered from the Demo cast (fixture clock Wed 2026-09-30), and the timeline list on its own. The day is the
/// <c>?date=</c> query, the range is <c>?range=7d</c>, a person who is not tracked gets a sentence and the way back, the print table has the same rows as the list, and the list is
/// virtualized: a long list renders only the rows in view. The map and the layout in pixels belong to the Playwright spec.
/// </summary>
public sealed class HistoryPageTests : ComponentTestBase
{
    private static readonly DateOnly Today = new(2026, 9, 30);

    public HistoryPageTests()
    {
        Services.AddSingleton(new DevicePrefs(JSInterop.JSRuntime));
    }

    [Fact]
    public async Task TheDefaultDay_IsToday_WithTheNameTheDaySelectorAndTheBackLink()
    {
        await using var session = DrivingFormatterTests.Demo();
        var cut = RenderPage(session, "king", null);

        Assert.Equal("Alden", cut.Find("h1.realm-history__name").TextContent);
        Assert.Equal("2026-09-30", cut.Find("[data-testid='history-page']").GetAttribute("data-day"));
        Assert.Equal("day", cut.Find("[data-testid='history-page']").GetAttribute("data-mode"));
        Assert.Equal("./", cut.Find("[data-testid='history-back']").GetAttribute("href"));
        Assert.NotNull(cut.Find("[data-testid='history-prev']"));
        Assert.NotNull(cut.Find("[data-testid='history-date']"));
        Assert.NotNull(cut.Find("[data-testid='history-next']"));
        Assert.True(cut.Find("[data-testid='history-next']").HasAttribute("disabled"));
        Assert.True(cut.Find("[data-testid='history-today']").HasAttribute("disabled"));
        Assert.False(cut.Find("[data-testid='history-prev']").HasAttribute("disabled"));
    }

    [Fact]
    public async Task TheTimeline_OfADay_ListsTheVisitsAndTheDrives_AsButtons()
    {
        await using var session = DrivingFormatterTests.Demo();
        var cut = RenderPage(session, "king", "2026-09-29");

        var day = await session.GetHistoryDayAsync("king", new DateOnly(2026, 9, 29), CancellationToken.None);
        Assert.NotNull(day);
        var rows = cut.FindAll("[data-testid^='history-entry-']");
        Assert.Equal(day.Entries.Count, rows.Count);
        Assert.All(rows, row => Assert.Equal("false", row.GetAttribute("aria-pressed")));
        Assert.Contains("At Hearth Haven", cut.Find(".realm-history__list").TextContent);
        Assert.Contains("Drive · ", cut.Find(".realm-history__list").TextContent);
        Assert.Contains("Work", cut.Find(".realm-history__list").TextContent);
    }

    [Fact]
    public async Task ADate_OutsideTheRetainedRange_IsClamped_AndAWrongOneIsToday()
    {
        await using var session = DrivingFormatterTests.Demo();

        var future = RenderPage(session, "king", "2030-01-01");
        Assert.Equal("2026-09-30", future.Find("[data-testid='history-page']").GetAttribute("data-day"));

        var wrong = RenderPage(session, "king", "last-tuesday");
        Assert.Equal("2026-09-30", wrong.Find("[data-testid='history-page']").GetAttribute("data-day"));
    }

    [Fact]
    public async Task ThePreviousButton_StepsBackADay_AndReplacesTheHistoryEntry()
    {
        await using var session = DrivingFormatterTests.Demo();
        var cut = RenderPage(session, "king", "2026-09-29");
        var navigation = (BunitNavigationManager)Services.GetRequiredService<NavigationManager>();

        cut.Find("[data-testid='history-prev']").Click();

        cut.WaitForAssertion(() => Assert.Equal("2026-09-28", cut.Find("[data-testid='history-page']").GetAttribute("data-day")));
        Assert.EndsWith("date=2026-09-28", navigation.Uri, StringComparison.Ordinal);
        Assert.True(navigation.History.First().Options.ReplaceHistoryEntry);
    }

    [Fact]
    public async Task TheTodayButton_GoesBackToToday()
    {
        await using var session = DrivingFormatterTests.Demo();
        var cut = RenderPage(session, "king", "2026-09-26");

        cut.Find("[data-testid='history-today']").Click();

        cut.WaitForAssertion(() => Assert.Equal("2026-09-30", cut.Find("[data-testid='history-page']").GetAttribute("data-day")));
    }

    [Fact]
    public async Task TheDatePicker_IsLimitedToTheRetainedDays()
    {
        await using var session = DrivingFormatterTests.Demo();
        var cut = RenderPage(session, "king", null);

        var picker = cut.FindComponent<MudBlazor.MudDatePicker>();

        Assert.Equal(Today.ToDateTime(TimeOnly.MinValue), picker.Instance.MaxDate);
        Assert.NotNull(picker.Instance.MinDate);
        Assert.True(picker.Instance.MinDate < picker.Instance.MaxDate);
    }

    [Fact]
    public async Task TheRange_ListsSevenDays_EachWithAHeadingThatOpensTheDay()
    {
        await using var session = DrivingFormatterTests.Demo();
        var cut = RenderPage(session, "king", null, range: true);

        Assert.Equal("range", cut.Find("[data-testid='history-page']").GetAttribute("data-mode"));
        Assert.Empty(cut.FindAll("[data-testid='history-prev']"));
        var headings = cut.FindAll("a[data-testid^='history-day-2']");
        Assert.NotEmpty(headings);
        Assert.Equal("history/king?date=2026-09-30", headings[0].GetAttribute("href"));
    }

    [Fact]
    public async Task ThePrintTable_HasTheHeaderAndTheRowsOfTheDay()
    {
        await using var session = DrivingFormatterTests.Demo();
        var cut = RenderPage(session, "king", "2026-09-29");

        Assert.Equal("Location History · Alden · Tue, Sep 29, 2026", cut.Find("[data-testid='print-header']").TextContent);
        Assert.NotEmpty(cut.FindAll("[data-testid='print-history-row']"));
        Assert.Contains("realm-print-only", cut.Find("[data-testid='print-history']").ClassList);
        Assert.NotNull(cut.Find("[data-testid='btn-print']"));
    }

    [Theory]
    [InlineData("nobody")]
    [InlineData("hatchback")]
    public async Task APersonWithoutHistory_GetsASentenceAndTheWayBack(string id)
    {
        await using var session = DrivingFormatterTests.Demo();
        var cut = RenderPage(session, id, null, ready: "[data-testid='history-none']");

        Assert.NotNull(cut.Find("a[href='./']"));
        Assert.Empty(cut.FindAll("[data-testid='history-prev']"));
        Assert.Empty(cut.FindAll("[data-testid='history-list']"));
    }

    // ---- the list on its own -------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void ALongList_RendersOnlyTheRowsInView()
    {
        var rows = Enumerable.Range(0, 2000)
            .Select(i => (HistoryRow)new NoteRow("n" + i, "Row " + i))
            .ToList();

        var cut = RenderWithProviders<HistoryTimeline>(parameters => parameters.Add(timeline => timeline.Rows, rows));

        var rendered = cut.FindAll(".realm-history__item").Count;
        Assert.True(rendered < 200, $"{rendered} of 2000 rows were rendered");
    }

    [Fact]
    public async Task ASelectedRow_IsPressed_AndATapReportsTheEntry()
    {
        await using var session = DrivingFormatterTests.Demo();
        var day = await session.GetHistoryDayAsync("king", new DateOnly(2026, 9, 29), CancellationToken.None);
        Assert.NotNull(day);
        var rows = HistoryRow.OfDay(day, "Alden").ToList();
        var drive = day.Drives.First();
        HistoryEntry? tapped = null;

        var cut = RenderWithProviders<HistoryTimeline>(parameters => parameters
            .Add(timeline => timeline.Rows, rows)
            .Add(timeline => timeline.SelectedId, drive.Id)
            .Add(timeline => timeline.Zone, session.Zone)
            .Add(timeline => timeline.OnSelect, EventCallback.Factory.Create<HistoryEntry>(this, entry => tapped = entry)));

        var button = cut.Find($"[data-testid='history-entry-{drive.Id}']");
        Assert.Equal("true", button.GetAttribute("aria-pressed"));
        Assert.False(string.IsNullOrWhiteSpace(button.GetAttribute("aria-label")));

        var other = day.Stays.First();
        cut.Find($"[data-testid='history-entry-{other.Id}']").Click();

        Assert.Equal(other, tapped);
    }

    private IRenderedComponent<HistoryPage> RenderPage(IRealmSession session, string memberId, string? date, bool range = false, string ready = "[data-testid='history-summary']")
    {
        var navigation = Services.GetRequiredService<NavigationManager>();
        var query = new Dictionary<string, object?> { ["date"] = date, ["range"] = range ? "7d" : null };
        navigation.NavigateTo(navigation.GetUriWithQueryParameters(query));

        var cut = RenderWithProviders<HistoryPage>(parameters => parameters
            .Add(page => page.MemberId, memberId)
            .AddCascadingValue(session));
        cut.WaitForAssertion(() => cut.Find(ready));
        return cut;
    }
}
