using System.Text.RegularExpressions;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using MudBlazor;
using Realm.Domain;
using Realm.Web.Components.Driving;
using Xunit;

namespace Realm.Web.Tests;

/// <summary>
/// The stat chip and its neighbours on the Driving screen (01 sections 6.2 to 6.5), rendered with the canonical bUnit setup: the four chips with their value, arrow,
/// asterisk and tooltip (AC-34, AC-46), the Top Speed and Drives cards (AC-36), the driver card and its pill, and the week chips. Every figure is built from the Demo
/// cast or from a hand-made <see cref="EventStat"/>, and a null value always reads "—", never 0 (D20, D26). The colours of the arrows are the stylesheet's, so one test
/// reads the stylesheet. Layout in pixels belongs to the Playwright spec.
/// </summary>
public sealed class StatChipTests : ComponentTestBase
{
    private const string Dash = "—";
    private const string Unavailable = "The Realm hasn't recorded this yet";
    private const string SpeedingNote = "Counted from ~42 s samples; short bursts are missed";

    private static readonly IReadOnlyList<WeekRef> Weeks = ChipsOfTheDemo();

    // ---- the chip: a value or a dash ---------------------------------------------------------------------------------------------------------

    [Fact]
    public void Chip_WithNoStat_ReadsADash_WithTheInfoIcon_NoArrow_AndTheUnavailableTooltip()
    {
        var cut = Chip(EventKeys.Accel, null);

        Assert.Equal(Dash, Number(cut));
        Assert.Single(cut.FindAll(".realm-stat-chip__info"));
        Assert.Empty(cut.FindAll("[data-testid='trend-accel']"));
        Assert.Equal(Unavailable, TooltipOf(cut));
        Assert.Equal("Rapid accel.", cut.Find(".realm-stat-chip__label").TextContent);
        Assert.Equal("Rapid acceleration: not recorded yet. Double tap for details.", cut.Find("[data-testid='stat-accel']").GetAttribute("aria-label"));
    }

    [Fact]
    public void Chip_WithANullTotal_ReadsADash_EvenWhenThereIsATrendDelta_OrThePartialFlag()
    {
        var stat = DrivingFormatterTests.Stat(total: null, delta: 5, partial: true, availability: EventAvailability.None);
        var cut = Chip(EventKeys.Braking, stat);

        Assert.Equal(Dash, Number(cut));
        Assert.DoesNotContain('*', Number(cut));
        Assert.DoesNotContain('0', Number(cut));
        Assert.Single(cut.FindAll(".realm-stat-chip__info"));
        Assert.Empty(cut.FindAll("[data-testid='trend-braking']"));
        Assert.Equal(Unavailable, TooltipOf(cut));
    }

    [Fact]
    public void Chip_WithARealZero_ReadsZero_WithoutTheInfoIcon()
    {
        var cut = Chip(EventKeys.Speeding, DrivingFormatterTests.Stat(total: 0));

        Assert.Equal("0", Number(cut));
        Assert.Empty(cut.FindAll(".realm-stat-chip__info"));
        Assert.Null(TooltipOf(cut));
    }

    [Fact]
    public void Chip_IsOneButton_WithTheTestIdOfAppendixB_AndAnIconPerCategory()
    {
        foreach (var key in StatKeys)
        {
            var cut = Chip(key, DrivingFormatterTests.Stat(total: 3));

            var button = cut.Find($"button[data-testid='stat-{key}']");
            Assert.Equal("button", button.GetAttribute("type"));
            Assert.Single(cut.FindAll("button"));
            Assert.Single(cut.FindAll($".realm-stat-chip__icon--{key}"));
            Assert.Equal("true", cut.Find(".realm-stat-chip__icon").GetAttribute("aria-hidden"));
        }
    }

    // ---- the arrow ---------------------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(7, "up")]
    [InlineData(1, "up")]
    [InlineData(-11, "down")]
    [InlineData(0, "flat")]
    public void Chip_TheArrow_FollowsTheSignOfTheDelta(int delta, string direction)
    {
        var cut = Chip(EventKeys.Speeding, DrivingFormatterTests.Stat(total: 56, delta: delta));

        var trend = cut.Find("[data-testid='trend-speeding']");
        Assert.Contains($"realm-stat-chip__trend--{direction}", trend.ClassList);
        Assert.Single(trend.ClassList, name => name.StartsWith("realm-stat-chip__trend--", StringComparison.Ordinal));
        Assert.Equal("true", trend.GetAttribute("aria-hidden"));
    }

    [Fact]
    public void Chip_WithNoComparator_HasNoArrow()
    {
        var cut = Chip(EventKeys.Speeding, DrivingFormatterTests.Stat(total: 44, delta: null));

        Assert.Equal("44", Number(cut));
        Assert.Empty(cut.FindAll("[data-testid='trend-speeding']"));
    }

    [Fact]
    public void TheArrowColours_AreTokens_MoreEventsIsWorse()
    {
        var css = ReadRepositoryFile("src", "Realm.Web", "wwwroot", "css", "realm-driving.css");

        // 01 section 6.3: up is the error colour, down the success colour, flat the stale colour. The literal colours live in RealmPalette alone (the guard checks that).
        Assert.Contains("color: var(--realm-error);", RuleOf(css, ".realm-stat-chip__trend--up"), StringComparison.Ordinal);
        Assert.Contains("color: var(--realm-success);", RuleOf(css, ".realm-stat-chip__trend--down"), StringComparison.Ordinal);
        Assert.Contains("color: var(--realm-stale);", RuleOf(css, ".realm-stat-chip__trend--flat"), StringComparison.Ordinal);
    }

    [Fact]
    public void TheStylesheet_LoadsAfterTheSheetOnes_InTheAppShell()
    {
        var app = ReadRepositoryFile("src", "Realm.Web", "Components", "App.razor");

        var sheet = app.IndexOf("css/realm-sheet.css", StringComparison.Ordinal);
        var driving = app.IndexOf("<link rel=\"stylesheet\" href=\"css/realm-driving.css\" />", StringComparison.Ordinal);
        Assert.True(sheet >= 0);
        Assert.True(driving > sheet);
    }

    // ---- the asterisk ------------------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(true, EventAvailability.Some, "60*")]
    [InlineData(true, EventAvailability.All, "60*")]
    [InlineData(false, EventAvailability.Some, "60")]
    [InlineData(false, EventAvailability.All, "60")]
    [InlineData(true, EventAvailability.None, "60")]
    public void Chip_TheAsterisk_IsThePartialFlagOfTheStat_NeverForAPermanentGap(bool partial, EventAvailability availability, string expected)
    {
        var cut = Chip(EventKeys.Phone, DrivingFormatterTests.Stat(total: 60, partial: partial, availability: availability));

        Assert.Equal(expected, Number(cut));
    }

    // ---- the tooltip -------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Chip_TheTooltip_IsThePartialSentence_TheNote_OrBoth()
    {
        var shared = new[] { new EventDriverCount("king", 60, 71), new EventDriverCount("queen", null, null), new EventDriverCount("jester", null, null), new EventDriverCount("cryptid", null, null) };

        Assert.Equal(
            "Only 1 of 4 drivers shared this",
            TooltipOf(Chip(EventKeys.Phone, DrivingFormatterTests.Stat(total: 60, partial: true, availability: EventAvailability.Some, drivers: shared))));
        Assert.Equal(SpeedingNote, TooltipOf(Chip(EventKeys.Speeding, DrivingFormatterTests.Stat(total: 56, note: SpeedingNote))));
        Assert.Equal(
            "Only 1 of 4 drivers shared this. " + SpeedingNote,
            TooltipOf(Chip(EventKeys.Phone, DrivingFormatterTests.Stat(total: 60, partial: true, availability: EventAvailability.Some, note: SpeedingNote, drivers: shared))));
    }

    [Fact]
    public void Chip_TheTooltipText_IsTheAccessibleDescription_NotPartOfTheName()
    {
        var cut = Chip(EventKeys.Speeding, DrivingFormatterTests.Stat(total: 56, delta: 7, note: SpeedingNote));

        var button = cut.Find("[data-testid='stat-speeding']");
        var describedBy = button.GetAttribute("aria-describedby");
        Assert.Equal("realm-stat-desc-speeding", describedBy);
        Assert.Equal(SpeedingNote, cut.Find($"#{describedBy}").TextContent);
        Assert.DoesNotContain("short bursts", button.GetAttribute("aria-label"), StringComparison.Ordinal);
    }

    [Fact]
    public void Chip_WithNothingToSay_HasNoTooltip_AndNoDescription()
    {
        var cut = Chip(EventKeys.Speeding, DrivingFormatterTests.Stat(total: 56, delta: 7));

        Assert.Null(TooltipOf(cut));
        Assert.Null(cut.Find("[data-testid='stat-speeding']").GetAttribute("aria-describedby"));
        Assert.Empty(cut.FindAll(".realm-sr-only"));
    }

    // ---- the Demo week, as AC-34 and AC-46 read it -------------------------------------------------------------------------------------------

    [Fact]
    public async Task TheDefaultWeek_ReadsAsInAc34_WithTheAccessibleNamesOfAc46()
    {
        var report = await DrivingFormatterTests.Report(0);
        var cuts = StatKeys.ToDictionary(key => key, key => Chip(key, report.Events[key]));

        Assert.Equal(["56", "60*", Dash, Dash], StatKeys.Select(key => Number(cuts[key])));
        Assert.Equal(
            ["trend-speeding", "trend-phone"],
            StatKeys.SelectMany(key => cuts[key].FindAll("[data-testid^='trend-']")).Select(arrow => arrow.GetAttribute("data-testid")));
        Assert.Contains("realm-stat-chip__trend--up", cuts[EventKeys.Speeding].Find("[data-testid='trend-speeding']").ClassList);
        Assert.Contains("realm-stat-chip__trend--down", cuts[EventKeys.Phone].Find("[data-testid='trend-phone']").ClassList);
        Assert.Equal(
            [SpeedingNote, "Only 1 of 4 drivers shared this", Unavailable, Unavailable],
            StatKeys.Select(key => TooltipOf(cuts[key])));
        Assert.Equal(
            [
                "Speeding: 56 events this week, up 7 from last week, which is worse. Double tap for details.",
                "Phone use: 60 events this week, from 1 of 4 drivers, down 11 from last week, which is better. Double tap for details.",
                "Rapid acceleration: not recorded yet. Double tap for details.",
                "Hard braking: not recorded yet. Double tap for details.",
            ],
            StatKeys.Select(key => cuts[key].Find($"[data-testid='stat-{key}']").GetAttribute("aria-label")));
        Assert.Equal(
            [false, false, true, true],
            StatKeys.Select(key => cuts[key].FindAll(".realm-stat-chip__info").Count == 1));
    }

    [Fact]
    public async Task TheAllSourcesWeek_HasNoAsterisk_AndFourArrows()
    {
        var report = await DrivingFormatterTests.Report(0, "all-sources");
        var cuts = StatKeys.Select(key => (Key: key, Cut: Chip(key, report.Events[key]))).ToList();

        Assert.Equal(["56", "250", "18", "5"], cuts.Select(chip => Number(chip.Cut)));
        Assert.Equal(["up", "down", "up", "down"], cuts.Select(chip => Direction(chip.Cut, chip.Key)));
    }

    [Fact]
    public async Task WhenPhoneIsUnavailable_ThePhoneChipIsADash_NeverZero()
    {
        var report = await DrivingFormatterTests.Report(0, "phone-unavailable");
        var cut = Chip(EventKeys.Phone, report.Events[EventKeys.Phone]);

        Assert.Equal(Dash, Number(cut));
        Assert.Empty(cut.FindAll("[data-testid='trend-phone']"));
        Assert.Equal(Unavailable, TooltipOf(cut));
    }

    [Fact]
    public async Task NoChipNamesTheOriginalSource()
    {
        // D26: the data is the Realm's own, so nothing on the Driving screen says "Life360".
        var report = await DrivingFormatterTests.Report(0, "all-sources");

        foreach (var key in StatKeys)
        {
            Assert.DoesNotContain("Life360", Chip(key, report.Events[key]).Markup, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ---- the chip opens its popup ------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("speeding")]
    [InlineData("phone")]
    [InlineData("accel")]
    [InlineData("braking")]
    public async Task Chip_ATap_RaisesOnOpen_WithItsKey(string key)
    {
        var opened = new List<string>();
        var cut = RenderWithProviders<StatChip>(chip => chip
            .Add(p => p.Key, key)
            .Add(p => p.Stat, DrivingFormatterTests.Stat(total: 5))
            .Add(p => p.OnOpen, (string value) =>
            {
                opened.Add(value);
                return Task.CompletedTask;
            }));

        await cut.Find($"[data-testid='stat-{key}']").TriggerEventAsync("onclick", new MouseEventArgs());

        Assert.Equal([key], opened);
    }

    // ---- the Top Speed card ------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task TopSpeedCard_OfTheDefaultWeek_ShowsTheSpeed_AndTheDriversInitial()
    {
        var report = await DrivingFormatterTests.Report(0);
        var driver = Members().Single(member => member.Id == report.TopSpeed!.MemberId);

        var cut = RenderWithProviders<TopSpeedCard>(card => card.Add(p => p.TopSpeed, report.TopSpeed).Add(p => p.Driver, driver));

        var button = cut.Find("button[data-testid='card-topspeed']");
        Assert.Equal("96 mph", cut.Find(".realm-card__number").TextContent);
        Assert.Equal("Top Speed", cut.Find(".realm-card__label").TextContent);
        Assert.Equal("A", cut.Find(".realm-avatar__initial").TextContent);
        Assert.Empty(cut.FindAll(".realm-stat-chip__info"));
        Assert.Null(cut.FindComponent<MudTooltip>().Instance.Text);
        Assert.Equal("Top speed: 96 miles per hour, Alden. Double tap for details.", button.GetAttribute("aria-label"));
    }

    [Fact]
    public void TopSpeedCard_WithNoSpeed_ReadsADash_WithAGlyph_TheInfoIcon_AndTheUnavailableTooltip()
    {
        var cut = RenderWithProviders<TopSpeedCard>();

        Assert.Equal(Dash, cut.Find(".realm-card__number").TextContent);
        Assert.Empty(cut.FindAll(".realm-avatar"));
        Assert.Single(cut.FindAll(".realm-card__glyph"));
        Assert.Single(cut.FindAll(".realm-stat-chip__info"));
        Assert.Equal(Unavailable, cut.FindComponent<MudTooltip>().Instance.Text);
        Assert.Equal("Top speed: not recorded yet. Double tap for details.", cut.Find("[data-testid='card-topspeed']").GetAttribute("aria-label"));
    }

    [Fact]
    public async Task TopSpeedCard_ATap_RaisesOnOpen()
    {
        var taps = 0;
        var cut = RenderWithProviders<TopSpeedCard>(card => card.Add(p => p.OnOpen, () =>
        {
            taps++;
            return Task.CompletedTask;
        }));

        await cut.Find("[data-testid='card-topspeed']").TriggerEventAsync("onclick", new MouseEventArgs());

        Assert.Equal(1, taps);
    }

    // ---- the Drives card ---------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task DrivesCard_OfTheDefaultWeek_ShowsTheCountAndTheWholeMiles()
    {
        var report = await DrivingFormatterTests.Report(0);

        var cut = RenderWithProviders<DrivesCard>(card => card.Add(p => p.Totals, report.Totals));

        Assert.Equal(["Drives", "Total mi"], cut.FindAll(".realm-card__label").Select(label => label.TextContent));
        Assert.Equal(["64", "781"], cut.FindAll(".realm-card__number").Select(number => number.TextContent));
        Assert.Null(cut.FindComponent<MudTooltip>().Instance.Text);
        Assert.Equal("Drives: 64. Total miles: 781. Double tap for details.", cut.Find("[data-testid='card-drives']").GetAttribute("aria-label"));
    }

    [Fact]
    public void DrivesCard_WithNoTotals_ReadsTwoDashes_NeverZero()
    {
        var cut = RenderWithProviders<DrivesCard>();

        Assert.Equal([Dash, Dash], cut.FindAll(".realm-card__number").Select(number => number.TextContent));
        Assert.Equal(Unavailable, cut.FindComponent<MudTooltip>().Instance.Text);
        Assert.Equal("Drives: not recorded yet. Double tap for details.", cut.Find("[data-testid='card-drives']").GetAttribute("aria-label"));
    }

    [Fact]
    public void DrivesCard_ARealZeroWeek_ReadsZeroAndZero()
    {
        var cut = RenderWithProviders<DrivesCard>(card => card.Add(p => p.Totals, new WeekTotals(0, 0)));

        Assert.Equal(["0", "0"], cut.FindAll(".realm-card__number").Select(number => number.TextContent));
    }

    [Fact]
    public async Task DrivesCard_ATap_RaisesOnOpen()
    {
        var taps = 0;
        var cut = RenderWithProviders<DrivesCard>(card => card.Add(p => p.OnOpen, () =>
        {
            taps++;
            return Task.CompletedTask;
        }));

        await cut.Find("[data-testid='card-drives']").TriggerEventAsync("onclick", new MouseEventArgs());

        Assert.Equal(1, taps);
    }

    // ---- the driver card ---------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task DriverCard_OfTheDefaultWeek_IsOneRelativeLink_WithTheLineAndThePill()
    {
        var report = await DrivingFormatterTests.Report(0);
        var members = Members();
        var alden = report.Drivers[0];

        var cut = RenderWithProviders<DriverCard>(card => card
            .Add(p => p.Driver, alden)
            .Add(p => p.Member, members.Single(member => member.Id == alden.MemberId))
            .Add(p => p.Week, 0));

        var link = cut.Find("a[data-testid='driver-card-king']");
        Assert.Equal("driving/king?week=0", link.GetAttribute("href"));
        Assert.Equal("Alden", cut.Find(".realm-driver-card__name").TextContent);
        Assert.Equal("22 drives • 94.4 miles", cut.Find(".realm-driver-card__line").TextContent);
        Assert.Equal("6 speeding · 60 phone", PillText(cut, "king"));
        Assert.Equal("Alden: 22 drives, 94.4 miles, 6 speeding · 60 phone. Double tap for weekly details.", link.GetAttribute("aria-label"));
        Assert.Equal("A", cut.Find(".realm-avatar__initial").TextContent);
        Assert.Contains("realm-avatar--ring", cut.Find(".realm-avatar").ClassList);
    }

    [Theory]
    [InlineData(0, "driving/jester?week=0")]
    [InlineData(2, "driving/jester?week=2")]
    public void DriverCard_TheLink_CarriesTheWeek_AndHasNoLeadingSlash(int week, string expected)
    {
        var cut = RenderWithProviders<DriverCard>(card => card.Add(p => p.Driver, DrivingFormatterTests.Driver()).Add(p => p.Week, week));

        var href = cut.Find("a").GetAttribute("href");
        Assert.Equal(expected, href);
        Assert.NotEqual('/', href![0]);
    }

    [Fact]
    public void DriverCard_WithoutAMember_FallsBackToTheIdAndItsInitial()
    {
        var cut = RenderWithProviders<DriverCard>(card => card.Add(p => p.Driver, DrivingFormatterTests.Driver()));

        Assert.Equal("jester", cut.Find(".realm-driver-card__name").TextContent);
        Assert.Equal("J", cut.Find(".realm-avatar__initial").TextContent);
    }

    [Fact]
    public void DriverCard_ThePillAsterisk_FollowsCoarseTrips_AndCarriesItsTooltip()
    {
        var sparse = RenderWithProviders<DriverCard>(card => card.Add(p => p.Driver, DrivingFormatterTests.Driver(speeding: 38, coarseTrips: 2)));

        Assert.Equal("38* speeding events", PillText(sparse, "jester"));
        Assert.Equal("Some drives were too sparse to measure speed", sparse.FindComponent<MudTooltip>().Instance.Text);
    }

    [Fact]
    public void DriverCard_ThePillHasNoAsterisk_ForANullCountOfOneKind()
    {
        var cut = RenderWithProviders<DriverCard>(card => card.Add(p => p.Driver, DrivingFormatterTests.Driver(speeding: 6, phone: null, phoneCapable: true, coarseTrips: 0)));

        Assert.Equal("6 speeding events", PillText(cut, "jester"));
        Assert.Null(cut.FindComponent<MudTooltip>().Instance.Text);
    }

    [Fact]
    public void DriverCard_ThePill_IsAnAllClear_WhenEveryCountIsZero()
    {
        var cut = RenderWithProviders<DriverCard>(card => card.Add(p => p.Driver, DrivingFormatterTests.Driver(speeding: 0)));

        Assert.Equal("No events", PillText(cut, "jester"));
        Assert.Contains("realm-pill--clear", cut.Find("[data-testid='driver-pill-jester']").ClassList);
    }

    [Fact]
    public void DriverCard_WithNoRecord_SaysSo_AndHasNoPill()
    {
        var cut = RenderWithProviders<DriverCard>(card => card.Add(
            p => p.Driver,
            DrivingFormatterTests.Driver(covered: false, drives: null, miles: null, speeding: null, eventsTotal: null)));

        Assert.Equal("No record of this week", cut.Find(".realm-driver-card__line").TextContent);
        Assert.Empty(cut.FindAll("[data-testid='driver-pill-jester']"));
    }

    [Fact]
    public void DriverCard_WithADriveFreeWeek_SaysRestingInTheCastle_AndHasNoPill()
    {
        var cut = RenderWithProviders<DriverCard>(card => card.Add(p => p.Driver, DrivingFormatterTests.Driver(drives: 0, miles: 0, speeding: 0)));

        Assert.Equal("No drives this week · resting in the castle", cut.Find(".realm-driver-card__line").TextContent);
        Assert.Empty(cut.FindAll("[data-testid='driver-pill-jester']"));
    }

    [Fact]
    public async Task DriverCard_ThePhoto_FallsBackToTheInitial_WhenItFailsToLoad()
    {
        var member = Members().Single(m => m.Id == "king") with { AvatarUrl = "avatars/king" };
        var cut = RenderWithProviders<DriverCard>(card => card.Add(p => p.Driver, DrivingFormatterTests.Driver()).Add(p => p.Member, member));

        Assert.Equal("avatars/king", cut.Find(".realm-avatar img").GetAttribute("src"));
        Assert.Empty(cut.Find(".realm-avatar img").GetAttribute("alt")!);
        Assert.Empty(cut.FindAll(".realm-avatar__initial"));

        await cut.Find(".realm-avatar img").TriggerEventAsync("onerror", new Microsoft.AspNetCore.Components.Web.ErrorEventArgs());

        Assert.Empty(cut.FindAll(".realm-avatar img"));
        Assert.Equal("A", cut.Find(".realm-avatar__initial").TextContent);
    }

    [Fact]
    public async Task DriverCard_TheMemberColour_IsHandedToTheStylesheetAsACustomProperty()
    {
        var report = await DrivingFormatterTests.Report(0);
        var member = Members().Single(m => m.Id == "king");

        var cut = RenderWithProviders<DriverCard>(card => card.Add(p => p.Driver, report.Drivers[0]).Add(p => p.Member, member));

        Assert.Contains($"--realm-member-color:{member.Color};", cut.Find(".realm-avatar").GetAttribute("style"), StringComparison.Ordinal);
    }

    // ---- the week chips ----------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void WeekChips_AreOneRadioGroup_OfFourButtons_WithTheSelectedOneInTheTabOrder()
    {
        var cut = WeekChipsAt(0, _ => { });

        Assert.Equal("radiogroup", cut.Find("[data-testid='week-chips']").GetAttribute("role"));
        var chips = cut.FindAll("button[role='radio']");
        Assert.Equal(["week-chip-0", "week-chip-1", "week-chip-2", "week-chip-3"], chips.Select(chip => chip.GetAttribute("data-testid")));
        Assert.Equal(["This week", "Last week", "Sep 14 – Sep 20", "Sep 7 – Sep 13"], chips.Select(chip => chip.TextContent));
        Assert.Equal(["true", "false", "false", "false"], chips.Select(chip => chip.GetAttribute("aria-checked")));
        Assert.Equal(["0", "-1", "-1", "-1"], chips.Select(chip => chip.GetAttribute("tabindex")));
        Assert.Equal(
            [
                "This week, September 28 to October 4.",
                "Last week, September 21 to September 27.",
                "September 14 to September 20.",
                "September 7 to September 13.",
            ],
            chips.Select(chip => chip.GetAttribute("aria-label")));
    }

    [Fact]
    public void WeekChips_TheSelection_FollowsTheParameter()
    {
        var cut = WeekChipsAt(2, _ => { });

        Assert.Equal(["false", "false", "true", "false"], cut.FindAll("button[role='radio']").Select(chip => chip.GetAttribute("aria-checked")));
        Assert.Equal(["-1", "-1", "0", "-1"], cut.FindAll("button[role='radio']").Select(chip => chip.GetAttribute("tabindex")));
    }

    [Fact]
    public void WeekChips_ScrollTheSelectedChipIntoView_AfterTheFirstRender()
    {
        var module = JSInterop.SetupModule(Realm.Web.Shell.ShellInterop.ModulePath);
        module.Mode = JSRuntimeMode.Loose;

        WeekChipsAt(1, _ => { });

        module.VerifyInvoke("scrollChipIntoView");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task WeekChips_ATap_ReportsTheOffset(int offset)
    {
        var chosen = new List<int>();
        var cut = WeekChipsAt(0, chosen.Add);

        await cut.Find($"[data-testid='week-chip-{offset}']").TriggerEventAsync("onclick", new MouseEventArgs());

        Assert.Equal([offset], chosen);
    }

    [Theory]
    [InlineData(0, "ArrowRight", 1)]
    [InlineData(3, "ArrowRight", 0)]   // wraps
    [InlineData(0, "ArrowLeft", 3)]    // wraps
    [InlineData(2, "ArrowLeft", 1)]
    [InlineData(1, "Home", 0)]
    [InlineData(1, "End", 3)]
    public async Task WeekChips_TheArrowKeys_MoveTheSelection_AndFocusFollows(int from, string key, int expected)
    {
        var chosen = new List<int>();
        var cut = WeekChipsAt(from, chosen.Add);

        await cut.Find($"[data-testid='week-chip-{from}']").TriggerEventAsync("onkeydown", new KeyboardEventArgs { Key = key });

        Assert.Equal([expected], chosen);
        JSInterop.VerifyFocusAsyncInvoke();
    }

    [Fact]
    public async Task WeekChips_OtherKeys_ChooseNothing()
    {
        var chosen = new List<int>();
        var cut = WeekChipsAt(0, chosen.Add);

        await cut.Find("[data-testid='week-chip-0']").TriggerEventAsync("onkeydown", new KeyboardEventArgs { Key = "a" });
        await cut.Find("[data-testid='week-chip-0']").TriggerEventAsync("onkeydown", new KeyboardEventArgs { Key = "Enter" });

        Assert.Empty(chosen);
    }

    // ---- helpers -----------------------------------------------------------------------------------------------------------------------------

    private static readonly string[] StatKeys = [EventKeys.Speeding, EventKeys.Phone, EventKeys.Accel, EventKeys.Braking];

    private IRenderedComponent<StatChip> Chip(string key, EventStat? stat, int week = 0) =>
        RenderWithProviders<StatChip>(chip => chip.Add(p => p.Key, key).Add(p => p.Stat, stat).Add(p => p.WeekOffset, week));

    private IRenderedComponent<WeekChips> WeekChipsAt(int selected, Action<int> onChanged) =>
        RenderWithProviders<WeekChips>(chips => chips
            .Add(p => p.Weeks, Weeks)
            .Add(p => p.Selected, selected)
            .Add(p => p.SelectedChanged, (int offset) =>
            {
                onChanged(offset);
                return Task.CompletedTask;
            }));

    private static string PillText(IRenderedComponent<DriverCard> cut, string memberId) =>
        cut.Find($"[data-testid='driver-pill-{memberId}'] .realm-pill__text").TextContent;

    // The direction word of the arrow's modifier class (realm-stat-chip__trend--up, --down or --flat).
    private static string Direction(IRenderedComponent<StatChip> cut, string key)
    {
        const string Prefix = "realm-stat-chip__trend--";
        var modifier = cut.Find($"[data-testid='trend-{key}']").ClassList.Single(name => name.StartsWith(Prefix, StringComparison.Ordinal));
        return modifier[Prefix.Length..];
    }

    private static string Number(IRenderedComponent<StatChip> cut) => cut.Find(".realm-stat-chip__number").TextContent;

    private static string? TooltipOf(IRenderedComponent<StatChip> cut) => cut.FindComponent<MudTooltip>().Instance.Text;

    private static IReadOnlyList<WeekRef> ChipsOfTheDemo()
    {
        var session = DrivingFormatterTests.Demo();
        return WeekMath.Chips(session.Time.GetUtcNow(), session.Current.WeekStart, session.Zone);
    }

    private static IReadOnlyList<MemberVm> Members() => DrivingFormatterTests.Demo().Current.Members;

    private static string ReadRepositoryFile(params string[] path) =>
        File.ReadAllText(Path.Combine([PayloadContractTests.FindRepositoryRoot(), .. path]));

    // The declarations of the first rule whose selector is exactly this one.
    private static string RuleOf(string css, string selector)
    {
        var match = Regex.Match(css, Regex.Escape(selector) + @"\s*\{([^}]*)\}");
        Assert.True(match.Success, $"{selector} has no rule in realm-driving.css.");
        return match.Groups[1].Value;
    }
}
