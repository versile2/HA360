using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using MudBlazor;
using Realm.Demo;
using Realm.Domain;
using Realm.Web.Components.Shell;
using Realm.Web.State;
using Xunit;
using ConnectionState = Realm.Domain.ConnectionState;   // an alias, so that no other imported namespace can make the name ambiguous (the razor files qualify it for the same reason)

namespace Realm.Web.Tests;

/// <summary>
/// The Settings dialog (01 sections 7.9 and 8.9) opened as the Settings tab of the bottom nav opens it, with IDialogService into a MudDialogProvider, and the device preferences behind it
/// (<see cref="DevicePrefs"/>) against a fake browser storage: the five sections and every string, the rows that must not exist (theme, units, week start, reset), the
/// Diagnostics row and where it points, the Connections chips of the session, and that a choice is current at once and stored under its key. Esc, the scrim and the
/// full-screen layout below 600 px are CSS and keys that only a browser exercises (the Playwright gallery shows both sizes).
/// </summary>
public sealed class SettingsDialogTests : ComponentTestBase
{
    private readonly FakeJs _js = new();

    // The reference of the dialog the test opened last, whose result says how it closed.
    private IDialogReference? _reference;

    public SettingsDialogTests()
    {
        // The dialog reads the circuit's preferences; here they talk to the fake storage, not to bUnit's loose JS.
        Services.AddSingleton(new DevicePrefs(_js));
    }

    // ---- the anatomy ---------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task TheDialog_HasTheFiveSections_TheTitleAndTheSubtitle()
    {
        var cut = await OpenAsync();

        Assert.Equal(["Who's on the map", "Map", "Appearance", "Connections", "About"], Texts(cut, ".realm-settings__section h3"));
        Assert.Equal("Settings", cut.Find(".realm-popup__title").TextContent);
        Assert.Equal("Royal decrees", cut.Find(".realm-settings__subtitle").TextContent);
        Assert.Single(cut.FindAll("[data-testid='settings-dialog']"));
        Assert.Contains("realm-settings", cut.Find("[role='dialog']").ClassList);
    }

    [Fact]
    public async Task TheMapSection_ListsTheFourStyles_TheSwitchAndTheSixRadii_WithTheStringsOfSection8_9()
    {
        var cut = await OpenAsync();

        Assert.Equal(["Night", "Day", "Streets", "Satellite"], Texts(cut, "[aria-labelledby='realm-settings-style'] [role='radio']"));
        Assert.Equal(["6 mi", "16 mi", "25 mi", "62 mi", "155 mi", "Everyone"], Texts(cut, "[aria-labelledby='realm-settings-radius'] [role='radio']"));
        Assert.Equal(["Auto", "Bottom sheet", "Side panel"], Texts(cut, "[aria-labelledby='realm-settings-layout'] [role='radio']"));

        var labels = Texts(cut, ".realm-settings__label, .realm-switch-row__label");
        Assert.Contains("Map style", labels);
        Assert.Contains("Show places", labels);
        Assert.Contains("Default view", labels);
        Assert.Contains("Layout", labels);

        var help = Texts(cut, ".realm-settings__help");
        Assert.Contains("Draw your saved places as circles on the map.", help);
        Assert.Contains("Me and anyone within 25 mi · others appear as edge bubbles", help);
    }

    [Fact]
    public async Task TheDefaults_AreNightPlacesOn25MilesAndAutoLayout()
    {
        var cut = await OpenAsync();

        Assert.Equal(["Night"], Texts(cut, "[aria-labelledby='realm-settings-style'] [aria-checked='true']"));
        Assert.Equal(["25 mi"], Texts(cut, "[aria-labelledby='realm-settings-radius'] [aria-checked='true']"));
        Assert.Equal(["Auto"], Texts(cut, "[aria-labelledby='realm-settings-layout'] [aria-checked='true']"));
        Assert.Equal("true", cut.Find("[role='switch']").GetAttribute("aria-checked"));
    }

    [Fact]
    public async Task TheDialog_HasNoThemeUnitsTimeWeekStartOrResetRows()
    {
        var cut = await OpenAsync();
        var text = cut.Find("[data-testid='settings-dialog']").TextContent;

        foreach (var absent in new[] { "Theme", "Units", "Time format", "12-hour", "24-hour", "Week starts", "Reset", "Trail" })
        {
            Assert.DoesNotContain(absent, text, StringComparison.OrdinalIgnoreCase);
        }

        // The switch and the radio groups are the whole set of controls, besides the back arrow.
        Assert.Single(cut.FindAll("[role='switch']"));
        Assert.Equal(3, cut.FindAll("[role='radiogroup']").Count);
    }

    [Fact]
    public async Task TheAboutSection_HasTheVersion_TheCredits_AndTheDiagnosticsRowThatOpensTheFileInANewTab()
    {
        var cut = await OpenAsync();

        var about = cut.Find("[aria-labelledby='realm-settings-about']");
        Assert.Contains("Version", about.TextContent, StringComparison.Ordinal);
        Assert.Contains("Map attribution and credits", about.TextContent, StringComparison.Ordinal);
        Assert.Contains("OpenStreetMap", about.TextContent, StringComparison.Ordinal);
        Assert.Contains("MapLibre", about.TextContent, StringComparison.Ordinal);

        var link = cut.Find("a[href='diagnostics.json']");
        Assert.Equal("_blank", link.GetAttribute("target"));
        Assert.Contains("noopener", link.GetAttribute("rel"), StringComparison.Ordinal);
        Assert.False(link.GetAttribute("href")!.StartsWith('/'), "the link is a relative URL, so it works under the Ingress prefix");
        Assert.Contains("Diagnostics", link.TextContent, StringComparison.Ordinal);
        Assert.Contains("States and counts only. No locations.", link.TextContent, StringComparison.Ordinal);

        // D105: Report an issue and Star this project follow Diagnostics: three links in About, the two new ones absolute, in a new tab, with no opener.
        var links = about.QuerySelectorAll("a[href]");
        Assert.Equal(
            new[] { "diagnostics.json", "https://github.com/Versile2/ha-cartographer/issues/new/choose", "https://github.com/Versile2/ha-cartographer" },
            links.Select(l => l.GetAttribute("href")).ToArray());
        Assert.Equal(3, cut.FindAll("a[href]").Count);
        foreach (var external in links.Skip(1))
        {
            Assert.Equal("_blank", external.GetAttribute("target"));
            Assert.Equal("noopener noreferrer", external.GetAttribute("rel"));
        }

        Assert.Contains("Report an issue", links[1].TextContent, StringComparison.Ordinal);
        Assert.Contains("Star this project", links[2].TextContent, StringComparison.Ordinal);
        Assert.Contains("Opens GitHub", links[2].TextContent, StringComparison.Ordinal);
    }

    // ---- the Connections section -----------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task TheConnections_AreOneRowPerEntryOfTheSession_UnderTheNamesOfSection8_9()
    {
        await using var session = new DemoRealmSessionFactory().Create(null);

        var cut = await OpenAsync(session);

        Assert.Equal(
            ["Home Assistant", "Life360"],
            Texts(cut, ".realm-settings__connection-name"));
        Assert.Equal(["Connected", "Connected"], Texts(cut, ".realm-settings__chip"));
        Assert.Equal(["Last sync just now", "Last sync just now"], Texts(cut, ".realm-settings__connection .realm-settings__help"));
    }

    [Theory]
    [InlineData(ConnectionState.Connected, "Connected", "connected")]
    [InlineData(ConnectionState.Reconnecting, "Reconnecting…", "reconnecting")]
    [InlineData(ConnectionState.Unavailable, "Unavailable", "unavailable")]
    [InlineData(ConnectionState.NotConnected, "Not connected", "notconnected")]
    public async Task EachConnectionState_ReadsAsItsWord_AndUnknownNamesAreIgnored(ConnectionState state, string word, string kind)
    {
        await using var inner = new DemoRealmSessionFactory().Create(null);
        var session = new StubSession(inner, [new ConnectionVm(ConnectionNames.HomeAssistant, state, null), new ConnectionVm("SomethingNew", ConnectionState.Connected, null)]);

        var cut = await OpenAsync(session);

        var chip = Assert.Single(cut.FindAll(".realm-settings__chip"));
        Assert.Equal(word, chip.TextContent);
        Assert.Contains("realm-settings__chip--" + kind, chip.ClassList);
        Assert.Equal(["Home Assistant"], Texts(cut, ".realm-settings__connection-name"));
        Assert.Empty(cut.FindAll(".realm-settings__connection .realm-settings__help"));
    }

    // ---- choices apply at once and are stored ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task ChoosingAStyle_MakesItCurrent_AndStoresItUnderItsKey()
    {
        var prefs = Services.GetRequiredService<DevicePrefs>();
        var cut = await OpenAsync();

        await Option(cut, "realm-settings-style", "Day").TriggerEventAsync("onclick", new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.Equal(["Day"], Texts(cut, "[aria-labelledby='realm-settings-style'] [aria-checked='true']")));
        Assert.Equal("day", prefs.MapStyle);
        Assert.Equal("day", _js.Store["realm.mapStyle"]);
    }

    [Fact]
    public async Task TheSwitch_TheRadiusAndTheLayout_ApplyAtOnce_AndAreStored()
    {
        var prefs = Services.GetRequiredService<DevicePrefs>();
        var cut = await OpenAsync();

        await cut.Find("[role='switch']").TriggerEventAsync("onclick", new MouseEventArgs());
        await Option(cut, "realm-settings-radius", "62 mi").TriggerEventAsync("onclick", new MouseEventArgs());
        await Option(cut, "realm-settings-layout", "Side panel").TriggerEventAsync("onclick", new MouseEventArgs());

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("false", cut.Find("[role='switch']").GetAttribute("aria-checked"));
            Assert.Equal(["62 mi"], Texts(cut, "[aria-labelledby='realm-settings-radius'] [aria-checked='true']"));
            Assert.Equal(["Side panel"], Texts(cut, "[aria-labelledby='realm-settings-layout'] [aria-checked='true']"));
            Assert.Contains("Me and anyone within 62 mi · others appear as edge bubbles", Texts(cut, ".realm-settings__help"));
        });
        Assert.False(prefs.ShowZones);
        Assert.Equal(100, prefs.ViewRadiusKm);
        Assert.Equal("panel", prefs.Layout);
        Assert.Equal("false", _js.Store["realm.showZones"]);
        Assert.Equal("100", _js.Store["realm.viewRadiusKm"]);
        Assert.Equal("panel", _js.Store["realm.layout"]);
    }

    [Fact]
    public async Task EveryoneAsTheRadius_SaysThereAreNoEdgeBubbles()
    {
        var cut = await OpenAsync();

        await Option(cut, "realm-settings-radius", "Everyone").TriggerEventAsync("onclick", new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.Contains("Everyone is on the map · no edge bubbles", Texts(cut, ".realm-settings__help")));
        Assert.Equal("all", _js.Store["realm.viewRadiusKm"]);
        Assert.Equal(double.PositiveInfinity, Services.GetRequiredService<DevicePrefs>().ViewRadiusKm);
    }

    [Fact]
    public async Task TheDialog_ShowsTheStoredValues_WhenItIsTheFirstToReadThem()
    {
        _js.Store["realm.mapStyle"] = "satellite";
        _js.Store["realm.viewRadiusKm"] = "10";

        var cut = await OpenAsync();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(["Satellite"], Texts(cut, "[aria-labelledby='realm-settings-style'] [aria-checked='true']"));
            Assert.Equal(["6 mi"], Texts(cut, "[aria-labelledby='realm-settings-radius'] [aria-checked='true']"));
        });
    }

    [Fact]
    public async Task TheBackArrow_ClosesTheDialog()
    {
        var cut = await OpenAsync();

        await cut.Find("button[aria-label='Close settings']").TriggerEventAsync("onclick", new MouseEventArgs());

        var result = await _reference!.Result.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotNull(result);
        Assert.False(result.Canceled);
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[role='dialog']")));
    }

    // ---- the history layer (03 section 3.7) ----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task TheOpenDialog_IsOneSettingsLayer_OfTheHistoryDepth()
    {
        var ui = Services.GetRequiredService<RealmUiState>();
        Assert.Empty(ui.Overlays);

        await OpenAsync();

        Assert.Equal(new OverlayLayer(OverlayKind.Settings), Assert.Single(ui.Overlays));
        Assert.Equal(1, ui.Depth);
    }

    [Fact(DisplayName = "[R3-03] The Back gesture closes the open Settings dialog before anything else")]
    public async Task TheBackGesture_ClosesTheDialog_AndTakesItsLayerBack()
    {
        var ui = Services.GetRequiredService<RealmUiState>();
        var cut = await OpenAsync();

        Assert.Equal(BackStep.Overlay, ui.Back());

        var result = await _reference!.Result.WaitAsync(TimeSpan.FromSeconds(5));   // null-forgiving: OpenAsync set the reference
        Assert.NotNull(result);
        Assert.False(result.Canceled);
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[role='dialog']")));
        Assert.Empty(ui.Overlays);
    }

    [Fact]
    public async Task ClosingTheDialogByItsOwnArrow_TakesTheLayerBackToo()
    {
        var ui = Services.GetRequiredService<RealmUiState>();
        var cut = await OpenAsync();

        await cut.Find("button[aria-label='Close settings']").TriggerEventAsync("onclick", new MouseEventArgs());

        await _reference!.Result.WaitAsync(TimeSpan.FromSeconds(5));   // null-forgiving: OpenAsync set the reference
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[role='dialog']")));
        Assert.Empty(ui.Overlays);
    }

    [Fact]
    public async Task TheSettingsTab_OpensTheDialog_WithTheOptionsThatLetEscAndTheScrimCloseIt()
    {
        var dialogs = RenderWithProviders<MudDialogProvider>();
        var controls = Render<BottomNav>();

        var gear = controls.Find("[data-testid='btn-settings']");
        Assert.Equal("BUTTON", gear.TagName);
        Assert.Equal("Settings", gear.QuerySelector(".realm-nav-label")!.TextContent);
        await gear.TriggerEventAsync("onclick", new MouseEventArgs());

        dialogs.WaitForAssertion(() => dialogs.Find("[data-testid='settings-dialog']"));
        Assert.Single(dialogs.FindAll("[role='dialog']"));
        Assert.Empty(dialogs.FindAll(".mud-dialog-title .mud-button-close"));
        Assert.Contains("realm-popup-scrim", dialogs.Find(".mud-dialog-container .mud-overlay").ClassList);
    }

    // ---- DevicePrefs against a fake browser storage ------------------------------------------------------------------------------------------

    [Fact]
    public async Task Prefs_TouchNothingUntilAskedToLoad_ThenRoundTripTheFourKeys()
    {
        var js = new FakeJs();
        js.Store["realm.mapStyle"] = "streets";
        js.Store["realm.showZones"] = "false";
        js.Store["realm.viewRadiusKm"] = "250";
        js.Store["realm.layout"] = "sheet";
        await using var prefs = new DevicePrefs(js);

        // Nothing has been asked of the browser yet: this is what prerendering sees.
        Assert.Empty(js.Calls);
        Assert.False(prefs.Loaded);
        Assert.Equal(("night", true, "40", "auto"), (prefs.MapStyle, prefs.ShowZones, prefs.ViewRadius, prefs.Layout));

        await prefs.EnsureLoadedAsync();

        Assert.True(prefs.Loaded);
        Assert.Equal(("streets", false, "250", "sheet"), (prefs.MapStyle, prefs.ShowZones, prefs.ViewRadius, prefs.Layout));
        Assert.Equal(250, prefs.ViewRadiusKm);
        var reads = js.Calls.Count;
        await prefs.EnsureLoadedAsync();
        Assert.Equal(reads, js.Calls.Count);   // read once
    }

    [Fact]
    public async Task Prefs_AChangeIsCurrentAtOnce_RaisesChangedWithItsKey_AndIsStoredAfterwards()
    {
        var js = new FakeJs();
        await using var prefs = new DevicePrefs(js);
        var changed = new List<string>();
        prefs.Changed += changed.Add;
        await prefs.EnsureLoadedAsync();

        await prefs.SetMapStyleAsync("satellite");
        await prefs.SetShowZonesAsync(false);
        await prefs.SetViewRadiusAsync("all");
        await prefs.SetLayoutAsync("panel");

        Assert.Equal(["realm.mapStyle", "realm.showZones", "realm.viewRadiusKm", "realm.layout"], changed);
        Assert.Equal(
            new Dictionary<string, string>
            {
                ["realm.mapStyle"] = "satellite",
                ["realm.showZones"] = "false",
                ["realm.viewRadiusKm"] = "all",
                ["realm.layout"] = "panel",
            },
            js.Store);
        Assert.Equal("night", prefs.PreviousMapStyle);

        // A second set of preferences, as a reload builds it, reads them back.
        await using var reloaded = new DevicePrefs(js);
        await reloaded.EnsureLoadedAsync();
        Assert.Equal(("satellite", false, "all", "panel"), (reloaded.MapStyle, reloaded.ShowZones, reloaded.ViewRadius, reloaded.Layout));
        Assert.Equal(double.PositiveInfinity, reloaded.ViewRadiusKm);
    }

    [Fact]
    public async Task Prefs_ChoosingTheCurrentValueAgain_ChangesAndStoresNothing()
    {
        var js = new FakeJs();
        await using var prefs = new DevicePrefs(js);
        await prefs.EnsureLoadedAsync();
        var calls = js.Calls.Count;
        var changed = 0;
        prefs.Changed += _ => changed++;

        await prefs.SetMapStyleAsync("night");
        await prefs.SetShowZonesAsync(true);
        await prefs.SetViewRadiusAsync("40");
        await prefs.SetLayoutAsync("auto");

        Assert.Equal(0, changed);
        Assert.Equal(calls, js.Calls.Count);
        Assert.Empty(js.Store);
    }

    [Theory]
    [InlineData("parchment")]
    [InlineData("demo-offline")]
    [InlineData("")]
    public async Task Prefs_AStyleThatIsNotOneOfTheFour_IsIgnored(string styleId)
    {
        var js = new FakeJs();
        await using var prefs = new DevicePrefs(js);
        await prefs.EnsureLoadedAsync();

        await prefs.SetMapStyleAsync(styleId);
        await prefs.SetViewRadiusAsync("7");
        await prefs.SetLayoutAsync("desktop");

        Assert.Equal(("night", "40", "auto"), (prefs.MapStyle, prefs.ViewRadius, prefs.Layout));
        Assert.Empty(js.Store);
    }

    [Fact]
    public async Task Prefs_StoredValuesThatAreNotAllowed_ReadAsTheDefaults()
    {
        var js = new FakeJs();
        js.Store["realm.mapStyle"] = "parchment";
        js.Store["realm.showZones"] = "maybe";
        js.Store["realm.viewRadiusKm"] = "7";
        js.Store["realm.layout"] = "desktop";
        await using var prefs = new DevicePrefs(js);

        await prefs.EnsureLoadedAsync();

        Assert.True(prefs.Loaded);
        Assert.Equal(("night", true, "40", "auto"), (prefs.MapStyle, prefs.ShowZones, prefs.ViewRadius, prefs.Layout));
    }

    [Fact]
    public async Task Prefs_WhenTheBrowserRefusesStorage_TheDefaultsHold_AChoiceStaysForTheCircuit_AndALaterCallAsksAgain()
    {
        var js = new FakeJs { ImportError = new JSException("storage is blocked") };
        await using var prefs = new DevicePrefs(js);

        await prefs.EnsureLoadedAsync();
        Assert.False(prefs.Loaded);
        Assert.Equal(("night", true, "40", "auto"), (prefs.MapStyle, prefs.ShowZones, prefs.ViewRadius, prefs.Layout));

        await prefs.SetMapStyleAsync("day");   // not stored, and no exception
        Assert.Equal("day", prefs.MapStyle);
        Assert.Empty(js.Store);

        var asked = js.Calls.Count;
        await prefs.EnsureLoadedAsync();
        Assert.True(js.Calls.Count > asked);
    }

    [Fact]
    public void Prefs_TheKeysAndTheRadiusChoices_AreThoseOfSection7_9()
    {
        Assert.Equal("realm.mapStyle", DevicePrefs.MapStyleKey);
        Assert.Equal("realm.showZones", DevicePrefs.ShowZonesKey);
        Assert.Equal("realm.viewRadiusKm", DevicePrefs.ViewRadiusKmKey);
        Assert.Equal("realm.layout", DevicePrefs.LayoutKey);
        Assert.Equal(["night", "day", "streets", "satellite"], DevicePrefs.MapStyles);
        Assert.Equal(["10", "25", "40", "100", "250", "all"], DevicePrefs.ViewRadiusChoices.Select(choice => choice.Value));
        Assert.Equal([10, 25, 40, 100, 250, double.PositiveInfinity], DevicePrefs.ViewRadiusChoices.Select(choice => choice.Km));
        Assert.Equal(40, DevicePrefs.RadiusOf("not a radius").Km);
    }

    // ---- helpers -----------------------------------------------------------------------------------------------------------------------------

    private async Task<IRenderedComponent<MudDialogProvider>> OpenAsync(IRealmSession? session = null)
    {
        var cut = RenderWithProviders<MudDialogProvider>(provider =>
        {
            if (session is not null)
            {
                provider.AddCascadingValue(session);
            }
        });
        var dialogs = Services.GetRequiredService<IDialogService>();
        var options = new DialogOptions { CloseOnEscapeKey = true, BackdropClick = true, CloseButton = false, BackgroundClass = "realm-popup-scrim" };

        await Renderer.Dispatcher.InvokeAsync(async () =>
        {
            _reference = await dialogs.ShowAsync<SettingsDialog>(null, options);
        });
        cut.WaitForAssertion(() => cut.Find("[data-testid='settings-dialog']"));
        return cut;
    }

    private static string[] Texts(IRenderedComponent<MudDialogProvider> cut, string selector) =>
        cut.FindAll(selector).Select(element => element.TextContent.Trim()).ToArray();

    // The radio of a group, by its label.
    private static AngleSharp.Dom.IElement Option(IRenderedComponent<MudDialogProvider> cut, string groupLabelId, string label) =>
        cut.FindAll($"[aria-labelledby='{groupLabelId}'] [role='radio']").Single(option => option.TextContent.Trim() == label);

    // The storage and the module of realmShell.js, in memory: prefs.getAll and prefs.set as the script answers them.
    private sealed class FakeJs : IJSRuntime
    {
        public Dictionary<string, string> Store { get; } = [];

        public List<string> Calls { get; } = [];

        public Exception? ImportError { get; init; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            Calls.Add(identifier);
            if (ImportError is not null)
            {
                throw ImportError;
            }

            return new ValueTask<TValue>((TValue)(object)new FakeModule(this));
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            InvokeAsync<TValue>(identifier, args);
    }

    private sealed class FakeModule(FakeJs js) : IJSObjectReference
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            js.Calls.Add(identifier);
            object? result = null;
            switch (identifier)
            {
                case "prefs.getAll":
                    result = new Dictionary<string, string>(js.Store);
                    break;
                case "prefs.set":
                    js.Store[(string)args![0]!] = (string)args[1]!;
                    result = true;
                    break;
            }

            return new ValueTask<TValue>((TValue)result!);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            InvokeAsync<TValue>(identifier, args);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    // A Demo session with the connection list of the test.
    private sealed class StubSession(IRealmSession inner, IReadOnlyList<ConnectionVm> connections) : IRealmSession
    {
        public RealmSnapshot Current => inner.Current with { Connections = connections };

        public event Action? Changed
        {
            add { }
            remove { }
        }

        public IRosterEditor Roster => inner.Roster;

        public TimeProvider Time => inner.Time;

        public TimeZoneInfo Zone => inner.Zone;

        public ValueTask<WeekReportVm> GetWeekReportAsync(int weekOffset, DayOfWeek weekStart, CancellationToken ct) => inner.GetWeekReportAsync(weekOffset, weekStart, ct);

        public ValueTask<DriverWeek?> GetDriverWeekAsync(string memberId, int weekOffset, DayOfWeek weekStart, CancellationToken ct) =>
            inner.GetDriverWeekAsync(memberId, weekOffset, weekStart, ct);

        public string? ResolveMe(string? haUserId) => inner.ResolveMe(haUserId);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
