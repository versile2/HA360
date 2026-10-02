using System.Globalization;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using Realm.Domain;
using Realm.Web.Components.Driving;
using Realm.Web.Formatting;
using Realm.Web.Pages;
using Xunit;

namespace Realm.Web.Tests;

/// <summary>
/// The popup of the six headline items of the Driving screen (01 sections 6.7 and 6.7.1, AC-38 to AC-40), opened as the page opens it, with IDialogService into a
/// MudDialogProvider, and rendered from the Demo week reports: the title, lore and summary sentence of each item, the bars in value order with their fill shares and pills,
/// "—" for every figure that was not recorded (never 0), the Drives and Miles toggle, the footnotes (the Realm's own records, never the name of the original source, D26),
/// the Android Auto note (D36) and the "sampled" note (D39), and the two buttons that close it. Esc, the focus returning to the opener and the pixels belong to the
/// Playwright spec: Esc is a MudBlazor key interceptor that only a browser raises.
/// </summary>
public sealed class StatPopupTests : ComponentTestBase
{
    private const string Dash = "—";

    private static readonly TimeZoneInfo Chicago = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");

    private static readonly IReadOnlyDictionary<string, MemberVm> DemoMembers =
        DrivingFormatterTests.Demo().Current.Members.ToDictionary(member => member.Id, StringComparer.Ordinal);

    // The reference of the popup the test opened last, whose result says how it closed.
    private IDialogReference? _reference;

    // ---- the anatomy: title, lore, summary ---------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("speeding", "Speeding", "Heralds of haste")]
    [InlineData("phone", "Phone use", "Eyes on the road, good sirs")]
    [InlineData("accel", "Rapid acceleration", "The sudden gallop")]
    [InlineData("braking", "Hard braking", "Whoa, steed!")]
    [InlineData("topspeed", "Top Speed", "The fastest charge")]
    [InlineData("drives", "Total Drives", "Leagues travelled")]
    public async Task EveryPopup_HasAnIcon_ATitle_AndALoreLine_AndTheDialogIsNamedByTheTitle(string key, string title, string lore)
    {
        var cut = await OpenAsync(key, await DrivingFormatterTests.Report(0));

        Assert.Equal(title, cut.Find(".realm-popup__title").TextContent);
        Assert.Equal(lore, cut.Find(".realm-popup__lore").TextContent);
        Assert.Equal("true", cut.Find(".realm-popup__icon").GetAttribute("aria-hidden"));
        Assert.Single(cut.FindAll(".realm-popup__icon .mud-icon-root"));

        // The dialog is labelled by its header, whose only text is the title (the icon is hidden from assistive technology), so the name is the title alone.
        var dialog = cut.Find("[role='dialog']");
        var header = cut.Find($"#{dialog.GetAttribute("aria-labelledby")}");
        Assert.Contains("realm-popup__head", header.ClassList);
        Assert.Equal(title, header.TextContent.Trim());
        Assert.Single(cut.FindAll($"[data-testid='popup-{key}']"));
    }

    [Fact]
    public async Task ThePopupTitle_TakesTheFocus_WhenItOpens()
    {
        var cut = await OpenAsync(StatNameFormatter.DrivesKey, await DrivingFormatterTests.Report(0));

        // The heading is focused after a yield, so the trap of the dialog has saved the opener first.
        cut.WaitForAssertion(() => JSInterop.VerifyFocusAsyncInvoke());
    }

    [Theory(DisplayName = "[AC-38] The six popups read the one-sentence summaries of 01 section 6.7 for the default fixture")]
    [InlineData("speeding", "56 speeding events this week, 7 more than last week.")]
    [InlineData("phone", "60 phone-use events this week from the 1 driver tracked, 11 fewer than last week.")]
    [InlineData("accel", "The Realm hasn't recorded this yet.")]
    [InlineData("braking", "The Realm hasn't recorded this yet.")]
    [InlineData("topspeed", "Alden hit 96 mph on Tue, Sep 29.")]
    [InlineData("drives", "64 drives, 781 miles on the road.")]
    public async Task TheSummary_OfTheDefaultFixture_IsTheSentenceOfSection6_7(string key, string expected)
    {
        var cut = await OpenAsync(key, await DrivingFormatterTests.Report(0));

        Assert.Equal(expected, cut.Find(".realm-popup__summary").TextContent);
    }

    [Theory]
    [InlineData("phone", "250 phone-use events this week, 24 fewer than last week.")]
    [InlineData("accel", "18 rapid accelerations this week, 6 more than last week.")]
    [InlineData("braking", "5 hard-braking events this week, 2 fewer than last week.")]
    public async Task TheSummary_OfTheAllSourcesFixture_NamesTheEventsOfEveryDriver(string key, string expected)
    {
        var cut = await OpenAsync(key, await DrivingFormatterTests.Report(0, "all-sources"));

        Assert.Equal(expected, cut.Find(".realm-popup__summary").TextContent);
    }

    [Fact]
    public async Task TheSummary_FollowsTheWeekOfTheReport()
    {
        var cut = await OpenAsync(EventKeys.Speeding, await DrivingFormatterTests.Report(1), week: 1);

        Assert.Equal("63 speeding events last week, 5 more than the week before.", cut.Find(".realm-popup__summary").TextContent);
    }

    [Theory]
    [InlineData("speeding")]
    [InlineData("phone")]
    [InlineData("accel")]
    [InlineData("braking")]
    [InlineData("topspeed")]
    [InlineData("drives")]
    public async Task AReportThatCouldNotBeRead_SaysTheRealmHasNotRecordedIt_AndHasNoBars(string key)
    {
        var cut = await OpenAsync(key, report: null);

        Assert.Equal("The Realm hasn't recorded this yet.", cut.Find(".realm-popup__summary").TextContent);
        Assert.Empty(cut.FindAll(".realm-bar"));
    }

    [Theory]
    [InlineData("speeding")]
    [InlineData("topspeed")]
    [InlineData("drives")]
    public async Task AWeekNobodyWasRecordedIn_IsUnavailable_AndEveryDriverReadsADash(string key)
    {
        var cut = await OpenAsync(key, await DrivingFormatterTests.Report(2, "fresh-install"), week: 2);

        Assert.Equal("The Realm hasn't recorded this yet.", cut.Find(".realm-popup__summary").TextContent);
        Assert.NotEmpty(cut.FindAll(".realm-bar"));
        Assert.All(cut.FindAll(".realm-bar__value"), value => Assert.Equal(Dash, value.TextContent));
        Assert.All(cut.FindAll(".realm-bar"), bar => Assert.Contains("realm-bar--unknown", bar.ClassList));
    }

    // ---- the bars ----------------------------------------------------------------------------------------------------------------------------

    [Fact(DisplayName = "[AC-39] The Speeding bars run Cass, Dara, Alden, Briar, the biggest at 100 %, each fill the share of the biggest")]
    public async Task Speeding_TheBarsAreInValueOrder_TheLargestIsFull_TheRestAreItsShare()
    {
        var cut = await OpenAsync(EventKeys.Speeding, await DrivingFormatterTests.Report(0));

        Assert.Equal(["bar-jester", "bar-cryptid", "bar-king", "bar-queen"], cut.FindAll(".realm-bar").Select(bar => bar.GetAttribute("data-testid")));
        Assert.Equal(["38", "10", "6", "2"], cut.FindAll(".realm-bar__value").Select(value => value.TextContent));
        AssertFractions([1, 10d / 38, 6d / 38, 2d / 38], cut);
        Assert.Equal(
            ["Cass: 38 speeding events", "Dara: 10 speeding events", "Alden: 6 speeding events", "Briar: 2 speeding events"],
            cut.FindAll(".realm-bar .realm-sr-only").Select(text => text.TextContent));
        Assert.Empty(cut.FindAll(".realm-bar--unknown"));
        Assert.Empty(cut.FindAll(".realm-bar__info"));
    }

    [Fact]
    public async Task EveryBar_HasTheDriversAvatar_InTheirColour_AndItsValueIsHiddenFromAssistiveTechnology()
    {
        var cut = await OpenAsync(EventKeys.Speeding, await DrivingFormatterTests.Report(0));

        var cass = cut.Find("[data-testid='bar-jester']");
        Assert.Equal("C", cass.QuerySelector(".realm-avatar__initial")!.TextContent);
        Assert.Contains($"--realm-member-color:{DemoMembers["jester"].Color};", cass.QuerySelector(".realm-avatar")!.GetAttribute("style"), StringComparison.Ordinal);
        Assert.Contains("--realm-avatar-size:40px;", cass.QuerySelector(".realm-avatar")!.GetAttribute("style"), StringComparison.Ordinal);
        Assert.Equal("true", cass.QuerySelector(".realm-bar__pill")!.GetAttribute("aria-hidden"));
        Assert.Equal("list", cut.Find(".realm-bars").GetAttribute("role"));
        Assert.Equal("Speeding by driver", cut.Find(".realm-bars").GetAttribute("aria-label"));
        Assert.All(cut.FindAll(".realm-bar"), bar => Assert.Equal("listitem", bar.GetAttribute("role")));
    }

    [Fact]
    public async Task Phone_OnlyAldenSharesIt_SoTheOthersReadADash_NeverZero_AndSitAtTheEnd()
    {
        var cut = await OpenAsync(EventKeys.Phone, await DrivingFormatterTests.Report(0));

        Assert.Equal(["bar-king", "bar-jester", "bar-cryptid", "bar-queen"], cut.FindAll(".realm-bar").Select(bar => bar.GetAttribute("data-testid")));
        Assert.Equal(["60", Dash, Dash, Dash], cut.FindAll(".realm-bar__value").Select(value => value.TextContent));
        Assert.Equal(
            ["Alden: 60 phone-use events", "Cass: not recorded", "Dara: not recorded", "Briar: not recorded"],
            cut.FindAll(".realm-bar .realm-sr-only").Select(text => text.TextContent));
        Assert.Equal(3, cut.FindAll(".realm-bar--unknown").Count);
        Assert.Equal(3, cut.FindAll(".realm-bar__info").Count);
        Assert.DoesNotContain("0", string.Concat(cut.FindAll(".realm-bar--unknown .realm-bar__value").Select(value => value.TextContent)), StringComparison.Ordinal);
        AssertFractions([1, 0, 0, 0], cut);
    }

    [Theory]
    [InlineData("accel")]
    [InlineData("braking")]
    public async Task AccelAndBraking_OfTheDefaultFixture_ReadADashForEveryDriver_ByDriveCount(string key)
    {
        var cut = await OpenAsync(key, await DrivingFormatterTests.Report(0));

        Assert.Equal(["bar-king", "bar-jester", "bar-cryptid", "bar-queen"], cut.FindAll(".realm-bar").Select(bar => bar.GetAttribute("data-testid")));
        Assert.All(cut.FindAll(".realm-bar__value"), value => Assert.Equal(Dash, value.TextContent));
        Assert.Equal(4, cut.FindAll(".realm-bar--unknown").Count);
        Assert.All(cut.FindAll(".realm-bar .realm-sr-only"), text => Assert.EndsWith(": not recorded", text.TextContent, StringComparison.Ordinal));
    }

    [Fact(DisplayName = "[AC-40] When phone data is unavailable the Phone popup is a dash for every driver and the sentence says so")]
    public async Task PhoneUnavailable_EveryDriverReadsADash_AndTheSentenceSaysSo()
    {
        var cut = await OpenAsync(EventKeys.Phone, await DrivingFormatterTests.Report(0, "phone-unavailable"));

        Assert.Equal("The Realm hasn't recorded this yet.", cut.Find(".realm-popup__summary").TextContent);
        Assert.Equal([Dash, Dash, Dash, Dash], cut.FindAll(".realm-bar__value").Select(value => value.TextContent));
        Assert.Equal(4, cut.FindAll(".realm-bar--unknown").Count);
        AssertFractions([0, 0, 0, 0], cut);
    }

    [Fact]
    public async Task ARealZero_ReadsZero_AtTheFloor_AndIsNotAnUnknownRow()
    {
        // Alden's hard-braking count is a real 0 in the all-sources week: a count, not a gap.
        var cut = await OpenAsync(EventKeys.Braking, await DrivingFormatterTests.Report(0, "all-sources"));

        Assert.Equal(["bar-jester", "bar-cryptid", "bar-queen", "bar-king"], cut.FindAll(".realm-bar").Select(bar => bar.GetAttribute("data-testid")));
        Assert.Equal(["3", "1", "1", "0"], cut.FindAll(".realm-bar__value").Select(value => value.TextContent));
        Assert.Empty(cut.FindAll(".realm-bar--unknown"));
        Assert.Equal("Alden: 0 hard-braking events", cut.Find("[data-testid='bar-king'] .realm-sr-only").TextContent);
        AssertFractions([1, 1d / 3, 1d / 3, 0], cut);
    }

    [Fact]
    public async Task AllSources_EveryDriverHasABar_InTheOrderOfTheirCount()
    {
        var cut = await OpenAsync(EventKeys.Phone, await DrivingFormatterTests.Report(0, "all-sources"));

        Assert.Equal(["bar-jester", "bar-king", "bar-cryptid", "bar-queen"], cut.FindAll(".realm-bar").Select(bar => bar.GetAttribute("data-testid")));
        Assert.Equal(["115", "60", "44", "31"], cut.FindAll(".realm-bar__value").Select(value => value.TextContent));
        AssertFractions([1, 60d / 115, 44d / 115, 31d / 115], cut);
        Assert.Empty(cut.FindAll(".realm-bar--unknown"));
    }

    [Fact]
    public async Task TopSpeed_TheBarsAreMph_InSpeedOrder_WithTheWidePills()
    {
        var cut = await OpenAsync(StatNameFormatter.TopSpeedKey, await DrivingFormatterTests.Report(0));

        Assert.Equal(["bar-king", "bar-jester", "bar-cryptid", "bar-queen"], cut.FindAll(".realm-bar").Select(bar => bar.GetAttribute("data-testid")));
        Assert.Equal(["96 mph", "88 mph", "84 mph", "82 mph"], cut.FindAll(".realm-bar__value").Select(value => value.TextContent));
        Assert.Equal("Alden: 96 miles per hour", cut.Find("[data-testid='bar-king'] .realm-sr-only").TextContent);
        AssertFractions([1, 88d / 96, 84d / 96, 82d / 96], cut);
        Assert.Contains("realm-bars--wide", cut.Find(".realm-bars").ClassList);
    }

    // ---- the Drives popup and its toggle -----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Drives_StartsOnDrives_WithTheBarsOf22_18_14_And10()
    {
        var cut = await OpenAsync(StatNameFormatter.DrivesKey, await DrivingFormatterTests.Report(0));

        Assert.Equal(["bar-king", "bar-jester", "bar-cryptid", "bar-queen"], cut.FindAll(".realm-bar").Select(bar => bar.GetAttribute("data-testid")));
        Assert.Equal(["22", "18", "14", "10"], cut.FindAll(".realm-bar__value").Select(value => value.TextContent));
        AssertFractions([1, 18d / 22, 14d / 22, 10d / 22], cut);
        Assert.Equal(["Drives", "Miles"], cut.FindAll(".realm-popup__toggle-chip").Select(chip => chip.TextContent));
        Assert.Equal(["true", "false"], PressedStates(cut));
        Assert.DoesNotContain("realm-bars--wide", cut.Find(".realm-bars").ClassList);
    }

    [Fact(DisplayName = "[AC-39] The Miles toggle re-orders the bars to Dara, Cass, Briar, Alden with the miles pills, and Drives brings them back")]
    public async Task Drives_TheToggleSwitchesTheBarsBetweenDrivesAndMiles()
    {
        var cut = await OpenAsync(StatNameFormatter.DrivesKey, await DrivingFormatterTests.Report(0));

        await cut.Find("[data-testid='popup-toggle-miles']").TriggerEventAsync("onclick", new MouseEventArgs());

        Assert.Equal(["bar-cryptid", "bar-jester", "bar-queen", "bar-king"], cut.FindAll(".realm-bar").Select(bar => bar.GetAttribute("data-testid")));
        Assert.Equal(["366.0 mi", "202.6 mi", "118.2 mi", "94.4 mi"], cut.FindAll(".realm-bar__value").Select(value => value.TextContent));
        Assert.Equal("Dara: 366.0 miles", cut.Find("[data-testid='bar-cryptid'] .realm-sr-only").TextContent);
        AssertFractions([1, 202.6 / 366.0, 118.2 / 366.0, 94.4 / 366.0], cut);
        Assert.Equal(["false", "true"], PressedStates(cut));
        Assert.Contains("realm-bars--wide", cut.Find(".realm-bars").ClassList);

        // The sentence, the title and the footnote are the same for both views: only the bars change.
        Assert.Equal("64 drives, 781 miles on the road.", cut.Find(".realm-popup__summary").TextContent);

        await cut.Find("[data-testid='popup-toggle-drives']").TriggerEventAsync("onclick", new MouseEventArgs());

        Assert.Equal(["bar-king", "bar-jester", "bar-cryptid", "bar-queen"], cut.FindAll(".realm-bar").Select(bar => bar.GetAttribute("data-testid")));
        Assert.Equal(["true", "false"], PressedStates(cut));
        Assert.DoesNotContain("realm-bars--wide", cut.Find(".realm-bars").ClassList);
    }

    [Theory]
    [InlineData("speeding")]
    [InlineData("phone")]
    [InlineData("accel")]
    [InlineData("braking")]
    [InlineData("topspeed")]
    public async Task TheToggle_IsOnTheDrivesPopupOnly(string key)
    {
        var cut = await OpenAsync(key, await DrivingFormatterTests.Report(0));

        Assert.Empty(cut.FindAll(".realm-popup__toggle"));
        Assert.Empty(cut.FindAll("[data-testid^='popup-toggle-']"));
    }

    // ---- the footnotes -----------------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("speeding")]
    [InlineData("phone")]
    [InlineData("accel")]
    [InlineData("braking")]
    [InlineData("topspeed")]
    [InlineData("drives")]
    public async Task TheFootnote_AlwaysBeginsWithTheRealmsOwnRecords_AndNothingNamesTheOriginalSource(string key)
    {
        var cut = await OpenAsync(key, await DrivingFormatterTests.Report(0, "all-sources"));

        var footnote = cut.Find(".realm-popup__footnote").TextContent.Trim();
        Assert.StartsWith("Source: The Realm's own records", footnote, StringComparison.Ordinal);
        Assert.DoesNotContain("Life360", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Speeding_TheFootnoteSaysTheCountIsSampled()
    {
        var cut = await OpenAsync(EventKeys.Speeding, await DrivingFormatterTests.Report(0));

        var footnote = cut.Find(".realm-popup__footnote").TextContent;
        Assert.Contains("sampled every ~42 s, so brief bursts are missed", footnote, StringComparison.Ordinal);
        Assert.Contains("above the speeding threshold", footnote, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Phone_TheFootnoteCarriesTheAndroidAutoNote_AsAnInformationButtonWithATooltip()
    {
        var cut = await OpenAsync(EventKeys.Phone, await DrivingFormatterTests.Report(0));

        const string Note = "Screen time while Android Auto is connected isn't counted, so navigation doesn't count as phone use.";
        var info = cut.Find(".realm-popup__footnote button.realm-popup__info");
        Assert.Equal("button", info.GetAttribute("type"));
        Assert.Equal("About Android Auto", info.GetAttribute("aria-label"));
        Assert.Equal(Note, cut.FindComponent<MudTooltip>().Instance.Text);
        Assert.Equal("realm-popup-info-description", info.GetAttribute("aria-describedby"));
        Assert.Equal(Note, cut.Find("#realm-popup-info-description").TextContent);
    }

    [Theory]
    [InlineData("speeding")]
    [InlineData("accel")]
    [InlineData("braking")]
    [InlineData("topspeed")]
    [InlineData("drives")]
    public async Task TheAndroidAutoNote_IsOnThePhonePopupOnly(string key)
    {
        var cut = await OpenAsync(key, await DrivingFormatterTests.Report(0));

        Assert.Empty(cut.FindAll(".realm-popup__info"));
        Assert.Empty(cut.FindAll(".realm-popup__footnote button"));
        Assert.DoesNotContain("Android Auto", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Drives_TheFootnoteCarriesTheGpsCaption()
    {
        var cut = await OpenAsync(StatNameFormatter.DrivesKey, await DrivingFormatterTests.Report(0));

        Assert.Equal(
            "Source: The Realm's own records. Distances are GPS-estimated (about 2–5 % low on winding roads).",
            cut.Find(".realm-popup__footnote").TextContent.Trim());
    }

    // ---- closing it --------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task TheGotItButton_IsFullWidth_AndClosesThePopup()
    {
        var cut = await OpenAsync(EventKeys.Speeding, await DrivingFormatterTests.Report(0));

        var button = cut.Find("button[data-testid='popup-gotit']");
        Assert.Equal("Got it", button.TextContent);
        Assert.Equal("button", button.GetAttribute("type"));

        await button.TriggerEventAsync("onclick", new MouseEventArgs());

        await AssertClosedAsync(cut, EventKeys.Speeding);
    }

    [Fact]
    public async Task TheCloseButton_IsTheFirstControlOfTheBody_NamedClose_AndClosesThePopup()
    {
        var cut = await OpenAsync(StatNameFormatter.DrivesKey, await DrivingFormatterTests.Report(0));

        // The Tab order inside the dialog's focus trap is Close, the toggle, Got it.
        var controls = cut.FindAll(".mud-dialog-content button").Select(button => button.GetAttribute("data-testid")).ToList();
        Assert.Equal(["popup-close", "popup-toggle-drives", "popup-toggle-miles", "popup-gotit"], controls);
        var close = cut.Find("button[data-testid='popup-close']");
        Assert.Equal("Close", close.GetAttribute("aria-label"));

        await close.TriggerEventAsync("onclick", new MouseEventArgs());

        await AssertClosedAsync(cut, StatNameFormatter.DrivesKey);
    }

    [Fact]
    public async Task TheDialog_HasNoHeaderCloseButton_AndTheScrimIsTheRealmsOwn()
    {
        var cut = await OpenAsync(EventKeys.Phone, await DrivingFormatterTests.Report(0));

        Assert.Empty(cut.FindAll(".mud-dialog-title .mud-button-close"));
        Assert.Single(cut.FindAll("button[data-testid='popup-close']"));
        Assert.Contains("realm-popup-scrim", cut.Find(".mud-dialog-container .mud-overlay").ClassList);
        Assert.Contains("realm-popup", cut.Find("[role='dialog']").ClassList);
    }

    // ---- the Driving page opens it -----------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("stat-speeding", "popup-speeding", "56 speeding events this week, 7 more than last week.")]
    [InlineData("stat-phone", "popup-phone", "60 phone-use events this week from the 1 driver tracked, 11 fewer than last week.")]
    [InlineData("stat-accel", "popup-accel", "The Realm hasn't recorded this yet.")]
    [InlineData("stat-braking", "popup-braking", "The Realm hasn't recorded this yet.")]
    [InlineData("card-topspeed", "popup-topspeed", "Alden hit 96 mph on Tue, Sep 29.")]
    [InlineData("card-drives", "popup-drives", "64 drives, 781 miles on the road.")]
    public async Task TheDrivingPage_OpensThePopupOfEachItem_WithTheReportItShows(string opener, string popup, string summary)
    {
        var dialogs = RenderWithProviders<MudDialogProvider>();
        await using var session = DrivingFormatterTests.Demo();
        var page = Render<DrivingPage>(parameters => parameters.AddCascadingValue(session));
        page.WaitForAssertion(() => page.Find($"[data-testid='{opener}']"));

        await page.Find($"[data-testid='{opener}']").TriggerEventAsync("onclick", new MouseEventArgs());

        dialogs.WaitForAssertion(() => dialogs.Find($"[data-testid='{popup}']"));
        Assert.Equal(summary, dialogs.Find(".realm-popup__summary").TextContent);
        Assert.Single(dialogs.FindAll("[role='dialog']"));
    }

    // ---- helpers -----------------------------------------------------------------------------------------------------------------------------

    // Opens the popup the way DrivingPage does (IDialogService into the MudDialogProvider, with the page's options but for the scrim class) and waits for its content.
    private async Task<IRenderedComponent<MudDialogProvider>> OpenAsync(string key, WeekReportVm? report, int week = 0)
    {
        var cut = RenderWithProviders<MudDialogProvider>();
        var dialogs = Services.GetRequiredService<IDialogService>();
        var parameters = new DialogParameters<StatPopup>();
        parameters.Add(popup => popup.Key, key);
        parameters.Add(popup => popup.Report, report);
        parameters.Add(popup => popup.Members, DemoMembers);
        parameters.Add(popup => popup.WeekOffset, week);
        parameters.Add(popup => popup.Zone, Chicago);
        var options = new DialogOptions { CloseOnEscapeKey = true, BackdropClick = true, CloseButton = false, BackgroundClass = "realm-popup-scrim" };

        await Renderer.Dispatcher.InvokeAsync(async () =>
        {
            _reference = await dialogs.ShowAsync<StatPopup>(null, parameters, options);
        });
        cut.WaitForAssertion(() => cut.Find($"[data-testid='popup-{key}']"));
        return cut;
    }

    private async Task AssertClosedAsync(IRenderedComponent<MudDialogProvider> cut, string key)
    {
        var result = await _reference!.Result.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotNull(result);
        Assert.False(result.Canceled);
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll($"[data-testid='popup-{key}']")));
        Assert.Empty(cut.FindAll("[role='dialog']"));
    }

    private static string[] PressedStates(IRenderedComponent<MudDialogProvider> cut) =>
        cut.FindAll(".realm-popup__toggle-chip").Select(chip => chip.GetAttribute("aria-pressed")!).ToArray();

    // The share of the largest bar that each fill takes, as the stylesheet reads it from --realm-bar-fraction (the largest is 1, so it is drawn at 100 %). The markup
    // carries four decimals ("0.4545" for 10 / 22), so a comparison to the exact quotient needs a tolerance: rounding both to three places flips at a midpoint (0.4545
    // rounds to 0.454, 0.45454 to 0.455). The error of four decimals is at most 0.00005, far inside the 2 % (0.02) that AC-39 allows.
    private static void AssertFractions(double[] expected, IRenderedComponent<MudDialogProvider> cut)
    {
        const string Property = "--realm-bar-fraction:";
        const double Tolerance = 0.0001;
        var actual = cut.FindAll(".realm-bar__fill")
            .Select(fill => fill.GetAttribute("style")!)
            .Select(style => double.Parse(style[(style.IndexOf(Property, StringComparison.Ordinal) + Property.Length)..].TrimEnd(';'), CultureInfo.InvariantCulture))
            .ToArray();
        Assert.Equal(expected.Length, actual.Length);
        for (var i = 0; i < expected.Length; i++)
        {
            Assert.InRange(actual[i], expected[i] - Tolerance, expected[i] + Tolerance);
        }
    }
}
