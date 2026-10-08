using Bunit;
using Microsoft.AspNetCore.Components.Web;
using MudBlazor;
using Realm.Domain;
using Realm.Web.Components.Driving;
using Xunit;

namespace Realm.Web.Tests;

/// <summary>
/// The period control (01 section 6.2): the week chips and the split button of MudButtonGroup + MudMenu. The main part re-applies the last long period, the arrow opens a menu
/// of Last month, Last 3 months, Last 6 months and Last year (the last two only when the retention covers them) and Custom range…; the custom panel checks its dates. The menu
/// items are drawn by MudBlazor's popover provider, so they are read from <see cref="ComponentTestBase.PopoverProvider"/>.
/// </summary>
public sealed class PeriodBarTests : ComponentTestBase
{
    private static readonly TimeZoneInfo Chicago = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 21, 25, 0, TimeSpan.FromHours(-5));

    private static Task OpenMenu(IRenderedComponent<PeriodBar> cut) =>
        cut.Find(".realm-period-split__menu button").TriggerEventAsync("onclick", new MouseEventArgs());

    [Fact]
    public void TheMainPart_ReadsLastMonthByDefault_AndTheFourWeekChipsStay()
    {
        var cut = Bar(new List<ReportPeriod>());

        Assert.Equal("Last month", cut.Find("[data-testid='period-main']").TextContent.Trim());
        Assert.Equal(4, cut.FindAll("[role='radio']").Count);
        Assert.Equal("true", cut.Find("[data-testid='week-chip-0']").GetAttribute("aria-checked"));
        Assert.Equal("false", cut.Find("[data-testid='period-main']").GetAttribute("aria-pressed"));
    }

    [Fact]
    public void ALongPeriod_SelectsTheSplitButton_AndNoWeekChip()
    {
        var cut = Bar(new List<ReportPeriod>(), period: new ReportPeriod(PeriodKind.Last3Months), lastLong: new ReportPeriod(PeriodKind.Last3Months));

        Assert.Equal("Last 3 months", cut.Find("[data-testid='period-main']").TextContent.Trim());
        Assert.Equal("true", cut.Find("[data-testid='period-main']").GetAttribute("aria-pressed"));
        Assert.All(cut.FindAll("[role='radio']"), chip => Assert.Equal("false", chip.GetAttribute("aria-checked")));
        Assert.Contains("realm-period-split--selected", cut.Find("[data-testid='period-split']").ClassList);
        // One tab stop in the group even though no chip is selected.
        Assert.Single(cut.FindAll("[role='radio'][tabindex='0']"));
    }

    [Fact]
    public async Task TheMainPart_ReappliesTheLastChosenLongPeriod_AndAWeekChipChoosesThatWeek()
    {
        var chosen = new List<ReportPeriod>();
        var cut = Bar(chosen, lastLong: new ReportPeriod(PeriodKind.Last6Months));

        await cut.Find("[data-testid='period-main']").TriggerEventAsync("onclick", new MouseEventArgs());
        await cut.Find("[data-testid='week-chip-2']").TriggerEventAsync("onclick", new MouseEventArgs());

        Assert.Equal([new ReportPeriod(PeriodKind.Last6Months), ReportPeriod.OfWeek(2)], chosen);
    }

    [Theory]
    [InlineData(100, new[] { "last-month", "3m", "custom" })]
    [InlineData(91, new[] { "last-month", "custom" })]
    [InlineData(184, new[] { "last-month", "3m", "custom" })]
    [InlineData(185, new[] { "last-month", "3m", "6m", "custom" })]
    [InlineData(365, new[] { "last-month", "3m", "6m", "custom" })]
    [InlineData(366, new[] { "last-month", "3m", "6m", "1y", "custom" })]
    [InlineData(400, new[] { "last-month", "3m", "6m", "1y", "custom" })]
    public async Task TheMenu_OffersSixMonthsAndAYearOnlyWhenTheRetentionCoversThem(int retentionDays, string[] expected)
    {
        var cut = Bar(new List<ReportPeriod>(), retentionDays: retentionDays);
        await OpenMenu(cut);

        var items = PopoverProvider!.FindAll("[data-testid^='period-item-']").Select(item => item.GetAttribute("data-testid")!["period-item-".Length..]);
        Assert.Equal(expected, items);
    }

    [Fact]
    public async Task AMenuEntry_ReportsItsPeriod()
    {
        var chosen = new List<ReportPeriod>();
        var cut = Bar(chosen, retentionDays: 400);
        await OpenMenu(cut);

        await PopoverProvider!.Find("[data-testid='period-item-1y']").TriggerEventAsync("onclick", new MouseEventArgs());

        Assert.Equal([new ReportPeriod(PeriodKind.LastYear)], chosen);
    }

    [Fact]
    public async Task CustomRange_OpensThePanelWithTwoPickers_AndApplyWithoutDatesSaysSo()
    {
        var chosen = new List<ReportPeriod>();
        var cut = Bar(chosen);
        Assert.Empty(cut.FindAll("[data-testid='custom-range']"));

        await OpenMenu(cut);
        await PopoverProvider!.Find("[data-testid='period-item-custom']").TriggerEventAsync("onclick", new MouseEventArgs());

        Assert.Equal(2, cut.FindComponents<MudDatePicker>().Count);
        await cut.Find("[data-testid='custom-apply']").TriggerEventAsync("onclick", new MouseEventArgs());
        Assert.Equal("Choose a start date and an end date.", cut.Find("[data-testid='custom-message']").TextContent);
        Assert.Empty(chosen);

        await cut.Find("[data-testid='custom-cancel']").TriggerEventAsync("onclick", new MouseEventArgs());
        Assert.Empty(cut.FindAll("[data-testid='custom-range']"));
    }

    [Fact]
    public async Task ARangeThatEndsBeforeItStarts_IsRefusedWithAMessage_AndAValidOneIsApplied()
    {
        var chosen = new List<ReportPeriod>();
        var cut = Bar(chosen, retentionDays: 100);
        await OpenMenu(cut);
        await PopoverProvider!.Find("[data-testid='period-item-custom']").TriggerEventAsync("onclick", new MouseEventArgs());
        var pickers = cut.FindComponents<MudDatePicker>();

        await cut.InvokeAsync(() => pickers[0].Instance.DateChanged.InvokeAsync(new DateTime(2026, 9, 20)));
        await cut.InvokeAsync(() => pickers[1].Instance.DateChanged.InvokeAsync(new DateTime(2026, 9, 10)));
        await cut.Find("[data-testid='custom-apply']").TriggerEventAsync("onclick", new MouseEventArgs());
        Assert.Equal("The start date must be on or before the end date.", cut.Find("[data-testid='custom-message']").TextContent);

        await cut.InvokeAsync(() => pickers[0].Instance.DateChanged.InvokeAsync(new DateTime(2026, 6, 1)));
        await cut.InvokeAsync(() => pickers[1].Instance.DateChanged.InvokeAsync(new DateTime(2026, 9, 10)));
        await cut.Find("[data-testid='custom-apply']").TriggerEventAsync("onclick", new MouseEventArgs());
        Assert.Equal(
            "The Realm keeps 100 days of history, back to Jun 22, 2026. Choose a start on or after that day.",
            cut.Find("[data-testid='custom-message']").TextContent);

        await cut.InvokeAsync(() => pickers[0].Instance.DateChanged.InvokeAsync(new DateTime(2026, 9, 1)));
        await cut.InvokeAsync(() => pickers[1].Instance.DateChanged.InvokeAsync(new DateTime(2026, 10, 1)));
        await cut.Find("[data-testid='custom-apply']").TriggerEventAsync("onclick", new MouseEventArgs());
        Assert.Equal("The end date can't be after today.", cut.Find("[data-testid='custom-message']").TextContent);

        await cut.InvokeAsync(() => pickers[1].Instance.DateChanged.InvokeAsync(new DateTime(2026, 9, 15)));
        await cut.Find("[data-testid='custom-apply']").TriggerEventAsync("onclick", new MouseEventArgs());
        Assert.Equal([ReportPeriod.OfRange(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 15))], chosen);
        Assert.Empty(cut.FindAll("[data-testid='custom-range']"));
    }

    private IRenderedComponent<PeriodBar> Bar(List<ReportPeriod> chosen, ReportPeriod? period = null, ReportPeriod? lastLong = null, int retentionDays = 100) =>
        RenderWithProviders<PeriodBar>(bar => bar
            .Add(p => p.Weeks, WeekMath.Chips(Now, DayOfWeek.Monday, Chicago))
            .Add(p => p.Period, period ?? ReportPeriod.ThisWeek)
            .Add(p => p.LastLong, lastLong ?? new ReportPeriod(PeriodKind.LastMonth))
            .Add(p => p.RetentionFixDays, retentionDays)
            .Add(p => p.Today, new DateOnly(2026, 9, 30))
            .Add(p => p.PeriodChanged, (ReportPeriod value) =>
            {
                chosen.Add(value);
                return Task.CompletedTask;
            }));
}
