using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudX;
using Realm.Web.Components.Sheet;
using Realm.Web.Layout;
using Xunit;

namespace Realm.Web.Tests;

/// <summary>
/// <see cref="RealmSheetHost"/> (03 section 3.5; D42): the one component that knows MudX. The tests read what the host passes to <c>MudXSheet</c> (its
/// parameter values) and what it does with the callbacks MudX raises, which is the contract: <c>Open</c> kept true, <c>PresetSizes [19, 80]</c>, the
/// <c>data-state</c> of the contract element, the panel's one size, the aside fallback. The DOM that MudX renders inside the popover provider (the
/// computed geometry) is the business of the Playwright specs; bUnit has no layout. The host is rendered through a probe that plays the page: it owns
/// <c>Size</c>, records what the host asks for, and can change the mode and the width.
/// </summary>
public sealed class RealmSheetHostTests : ComponentTestBase
{
    // ---- the bottom sheet (Compact) --------------------------------------------------------------------------------------------------------

    [Fact]
    public void Compact_Peek_IsABottomMudXSheet_WithTheTwoPresetsAndTheContractAttributes()
    {
        var cut = Host(LayoutMode.Compact, 412);
        var sheet = Sheet(cut);

        Assert.Equal(Position.Bottom, sheet.Position);
        Assert.True(sheet.Open);
        Assert.Equal([19, 80], sheet.PresetSizes);
        Assert.Equal(19, sheet.CurrentSize);
        Assert.True(sheet.SnapMode);
        Assert.True(sheet.EnableDragToSize);
        Assert.True(sheet.Standard);
        Assert.False(sheet.Paper);
        Assert.False(sheet.CloseOnEscapeKey);
        Assert.Equal(0, sheet.Elevation);
        Assert.Equal(24, sheet.BorderRadius);
        Assert.Equal("Realm list", sheet.AriaLabel);
        Assert.NotNull(sheet.SheetHandleFragment);
        Assert.Equal("sheet", sheet.UserAttributes["data-testid"]);
        Assert.Equal("peek", sheet.UserAttributes["data-state"]);
    }

    [Fact]
    public void Compact_Tall_Is80Percent_AndTheStateSaysSo()
    {
        var cut = Host(LayoutMode.Compact, 412, SheetSize.Tall);
        var sheet = Sheet(cut);

        Assert.Equal(80, sheet.CurrentSize);
        Assert.Equal("80", sheet.UserAttributes["data-state"]);
        Assert.Equal([19, 80], sheet.PresetSizes);
        Assert.True(sheet.Open);
    }

    [Fact]
    public async Task Compact_TheParentDrivesTheState_WithoutTheHostAskingForAnything()
    {
        // S8 sets Size (a selection at Tall collapses to Peek, D45): the host follows, and a size the parent chose is not a request.
        var cut = Host(LayoutMode.Compact, 412);

        await cut.InvokeAsync(() => cut.Instance.Set(SheetSize.Tall));
        Assert.Equal(80, Sheet(cut).CurrentSize);
        Assert.Equal("80", Sheet(cut).UserAttributes["data-state"]);

        await cut.InvokeAsync(() => cut.Instance.Set(SheetSize.Peek));
        Assert.Equal(19, Sheet(cut).CurrentSize);
        Assert.Equal("peek", Sheet(cut).UserAttributes["data-state"]);
        Assert.Empty(cut.Instance.Requests);
    }

    [Fact]
    public async Task Compact_ASnapToThe80Preset_IsARequestForTall_AndTheParentsAnswerMovesTheSheet()
    {
        var cut = Host(LayoutMode.Compact, 412);

        await cut.InvokeAsync(() => Sheet(cut).CurrentSizeChanged.InvokeAsync(80));

        Assert.Equal([SheetSize.Tall], cut.Instance.Requests);
        Assert.Equal(80, Sheet(cut).CurrentSize);
        Assert.Equal("80", Sheet(cut).UserAttributes["data-state"]);

        await cut.InvokeAsync(() => Sheet(cut).CurrentSizeChanged.InvokeAsync(19));

        Assert.Equal([SheetSize.Tall, SheetSize.Peek], cut.Instance.Requests);
        Assert.Equal("peek", Sheet(cut).UserAttributes["data-state"]);
    }

    [Theory]
    [InlineData(19)]   // the echo of the state the sheet is already in (MudX raises it once more for every size it was given)
    [InlineData(20)]   // a drag in progress: MudX raises CurrentSizeChanged on every pointer move
    [InlineData(37)]
    [InlineData(55)]
    [InlineData(79)]
    [InlineData(95)]
    public async Task Compact_AnythingButTheOtherPreset_IsNotARequest(int percent)
    {
        var cut = Host(LayoutMode.Compact, 412);

        await cut.InvokeAsync(() => Sheet(cut).CurrentSizeChanged.InvokeAsync(percent));

        Assert.Empty(cut.Instance.Requests);
        Assert.Equal("peek", Sheet(cut).UserAttributes["data-state"]);
    }

    [Fact]
    public async Task Compact_TheEchoOf80_WhenAlreadyTall_IsIgnored()
    {
        var cut = Host(LayoutMode.Compact, 412, SheetSize.Tall);

        await cut.InvokeAsync(() => Sheet(cut).CurrentSizeChanged.InvokeAsync(80));

        Assert.Empty(cut.Instance.Requests);
    }

    [Fact]
    public async Task Compact_TheSheetNeverCloses_OpenIsReassertedTrue()
    {
        var cut = Host(LayoutMode.Compact, 412);
        var sheet = Sheet(cut);

        // MudX closed itself (a drag to the bottom edge, the escape key) and says so through both callbacks.
        await cut.InvokeAsync(() => sheet.OpenChanged.InvokeAsync(false));
        cut.WaitForAssertion(() => Assert.True(Sheet(cut).Open, "Open must be true again after OpenChanged(false): the sheet never closes."));
        Assert.Same(sheet, Sheet(cut));   // reopened, not rebuilt

        await cut.InvokeAsync(() => sheet.OnDismissed.InvokeAsync());
        cut.WaitForAssertion(() => Assert.True(Sheet(cut).Open, "Open must be true again after OnDismissed."));

        await cut.InvokeAsync(() => sheet.OpenChanged.InvokeAsync(true));
        Assert.True(Sheet(cut).Open);
        Assert.Empty(cut.Instance.Requests);
    }

    // ---- the left panel (Expanded) ---------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(884.0, 45)]
    [InlineData(840.0, 48)]
    [InlineData(1440.0, 28)]
    public void Expanded_IsALeftMudXSheet_OfOneSize_ThatMakes400Px(double width, int percent)
    {
        var cut = Host(LayoutMode.Expanded, width);
        var sheet = Sheet(cut);

        Assert.Equal(Position.Left, sheet.Position);
        Assert.True(sheet.Open);
        Assert.Equal(percent, sheet.CurrentSize);
        Assert.Equal([percent], sheet.PresetSizes);
        Assert.False(sheet.SnapMode);
        Assert.False(sheet.EnableDragToSize);
        Assert.True(sheet.Standard);
        Assert.False(sheet.Paper);
        Assert.False(sheet.CloseOnEscapeKey);
        Assert.Equal("sheet", sheet.UserAttributes["data-testid"]);
        Assert.Equal("panel", sheet.UserAttributes["data-state"]);
    }

    [Fact]
    public async Task Expanded_ThePanelHasNoStates_ASizeFromMudXIsNeverARequest()
    {
        var cut = Host(LayoutMode.Expanded, 884);

        await cut.InvokeAsync(() => Sheet(cut).CurrentSizeChanged.InvokeAsync(80));
        await cut.InvokeAsync(() => Sheet(cut).CurrentSizeChanged.InvokeAsync(19));

        Assert.Empty(cut.Instance.Requests);
        Assert.Equal("panel", Sheet(cut).UserAttributes["data-state"]);
    }

    [Fact]
    public async Task Expanded_TheWidthIsRecomputedOnEveryReport()
    {
        var cut = Host(LayoutMode.Expanded, 884);
        Assert.Equal(45, Sheet(cut).CurrentSize);

        await cut.InvokeAsync(() => cut.Instance.Change(LayoutMode.Expanded, 1440));

        Assert.Equal(28, Sheet(cut).CurrentSize);
        Assert.Equal([28], Sheet(cut).PresetSizes);
    }

    [Fact]
    public async Task Expanded_TheSheetNeverCloses_EitherEvenInThePanel()
    {
        var cut = Host(LayoutMode.Expanded, 884);

        await cut.InvokeAsync(() => Sheet(cut).OnDismissed.InvokeAsync());

        cut.WaitForAssertion(() => Assert.True(Sheet(cut).Open));
    }

    // ---- switching between the two ---------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task FoldingAndUnfolding_MakesANewMudXSheet_AtTheOtherEdge_AndKeepsTheState()
    {
        var cut = Host(LayoutMode.Compact, 412, SheetSize.Tall);
        var compact = Sheet(cut);
        Assert.Equal(Position.Bottom, compact.Position);

        await cut.InvokeAsync(() => cut.Instance.Change(LayoutMode.Expanded, 884));
        var expanded = Sheet(cut);
        Assert.NotSame(compact, expanded);
        Assert.Equal(Position.Left, expanded.Position);
        Assert.Equal("panel", expanded.UserAttributes["data-state"]);

        await cut.InvokeAsync(() => cut.Instance.Change(LayoutMode.Compact, 412));
        var folded = Sheet(cut);
        Assert.NotSame(expanded, folded);
        Assert.Equal(Position.Bottom, folded.Position);
        Assert.Equal("80", folded.UserAttributes["data-state"]);   // the parent's Size survived the round trip
        Assert.Equal(80, folded.CurrentSize);
    }

    [Fact]
    public void TheModeUnknown_RendersNothing_NoSheetNoAside()
    {
        var cut = Host(LayoutMode.Unknown, 0);

        Assert.Empty(cut.FindComponents<MudXSheet>());
        Assert.Empty(cut.FindAll("aside"));
        Assert.Empty(cut.FindAll("#body"));
    }

    // ---- the aside fallback (Realm:Ui:SheetHost=aside; the panel only) ---------------------------------------------------------------------

    [Fact]
    public void Aside_Expanded_IsAPlainAside_WithTheContractAndTheBody()
    {
        UseSheetHost("aside");

        var cut = Host(LayoutMode.Expanded, 884);

        Assert.Empty(cut.FindComponents<MudXSheet>());
        var aside = cut.Find("aside[data-testid='sheet']");
        Assert.Equal("panel", aside.GetAttribute("data-state"));
        Assert.Equal("region", aside.GetAttribute("role"));
        Assert.Equal("Realm list", aside.GetAttribute("aria-label"));
        Assert.Equal("body", aside.QuerySelector("#body")?.TextContent);
        Assert.Empty(cut.FindAll("[data-testid='sheet-handle']"));
    }

    [Fact]
    public async Task Aside_ASizeFromTheParent_ChangesNothing_ThePanelHasNoStates()
    {
        UseSheetHost("aside");
        var cut = Host(LayoutMode.Expanded, 884);

        await cut.InvokeAsync(() => cut.Instance.Set(SheetSize.Tall));

        Assert.Equal("panel", cut.Find("aside[data-testid='sheet']").GetAttribute("data-state"));
        Assert.Empty(cut.Instance.Requests);
    }

    [Fact]
    public void Aside_NeverAppliesToTheBottomSheet_ThePhoneSheetHasNoFallback()
    {
        UseSheetHost("aside");

        var cut = Host(LayoutMode.Compact, 412);

        Assert.Equal(Position.Bottom, Sheet(cut).Position);
        Assert.Empty(cut.FindAll("aside"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("mudx")]
    [InlineData("MUDX")]
    [InlineData("")]
    [InlineData("sidebar")]
    public void MudX_IsTheDefaultHost_AndAnythingButAsideIsMudX(string? value)
    {
        UseSheetHost(value);

        var cut = Host(LayoutMode.Expanded, 884);

        Assert.Equal(Position.Left, Sheet(cut).Position);
        Assert.Empty(cut.FindAll("aside"));
    }

    [Theory]
    [InlineData("aside")]
    [InlineData("ASIDE")]
    public void TheAsideSwitch_IsCaseInsensitive(string value)
    {
        UseSheetHost(value);

        var cut = Host(LayoutMode.Expanded, 884);

        Assert.Empty(cut.FindComponents<MudXSheet>());
        Assert.Single(cut.FindAll("aside[data-testid='sheet']"));
    }

    // ---- the preset constants --------------------------------------------------------------------------------------------------------------

    [Fact]
    public void ThePresets_AreThoseOfD42()
    {
        Assert.Equal(19, RealmSheetHost.PeekPercent);
        Assert.Equal(80, RealmSheetHost.TallPercent);
    }

    // ---- helpers ---------------------------------------------------------------------------------------------------------------------------

    private bool _configured;

    // The host reads Realm:Ui:SheetHost; the setting must be registered before the first render builds the service provider.
    private void UseSheetHost(string? value)
    {
        Assert.False(_configured, "UseSheetHost is called once per test, before Host().");
        _configured = true;
        var settings = new Dictionary<string, string?>();
        if (value is not null)
        {
            settings["Realm:Ui:SheetHost"] = value;
        }

        Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
    }

    private IRenderedComponent<HostProbe> Host(LayoutMode mode, double width, SheetSize size = SheetSize.Peek)
    {
        if (!_configured)
        {
            _configured = true;
            Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        }

        return RenderWithProviders<HostProbe>(probe => probe
            .Add(p => p.Mode, mode)
            .Add(p => p.Width, width)
            .Add(p => p.Initial, size));
    }

    private static MudXSheet Sheet(IRenderedComponent<HostProbe> cut) => cut.FindComponent<MudXSheet>().Instance;

    // The page, as far as the host can tell: it owns the state, records the requests, and accepts them as S7a's LocationPage does.
    private sealed class HostProbe : ComponentBase
    {
        private SheetSize _size;

        [Parameter]
        public LayoutMode Mode { get; set; }

        [Parameter]
        public double Width { get; set; }

        [Parameter]
        public SheetSize Initial { get; set; }

        public List<SheetSize> Requests { get; } = [];

        public void Set(SheetSize size)
        {
            _size = size;
            StateHasChanged();
        }

        public void Change(LayoutMode mode, double width)
        {
            Mode = mode;
            Width = width;
            StateHasChanged();
        }

        protected override void OnInitialized() => _size = Initial;

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<RealmSheetHost>(0);
            builder.AddComponentParameter(1, nameof(RealmSheetHost.Mode), Mode);
            builder.AddComponentParameter(2, nameof(RealmSheetHost.Size), _size);
            builder.AddComponentParameter(3, nameof(RealmSheetHost.ViewportWidth), Width);
            builder.AddComponentParameter(4, nameof(RealmSheetHost.Summary), "4 in the Realm · 1 driving");
            builder.AddComponentParameter(5, nameof(RealmSheetHost.OnSizeChanged), EventCallback.Factory.Create<SheetSize>(this, OnSizeChanged));
            builder.AddComponentParameter(6, nameof(RealmSheetHost.ChildContent), (RenderFragment)(body => body.AddMarkupContent(0, "<span id=\"body\">body</span>")));
            builder.CloseComponent();
        }

        private Task OnSizeChanged(SheetSize size)
        {
            Requests.Add(size);
            _size = size;
            return Task.CompletedTask;
        }
    }
}
