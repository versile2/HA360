using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Realm.Web.Components.Map;
using Realm.Web.Map;
using Realm.Web.State;
using Xunit;

namespace Realm.Web.Tests;

/// <summary>
/// The map style popover as a history layer (03 section 3.7, R3-03, AC-38): while it is open it is one layer of the depth, the Android Back closes it through the same
/// <c>OnClose</c> as Esc and a tap outside, and however it closes (the owner clears <c>Open</c>, the component goes away) the layer leaves the stack. Its anatomy and the tiles are
/// the business of <c>SheetPartsTests</c> and the Playwright specs.
/// </summary>
public sealed class MapStylePopoverLayerTests : ComponentTestBase
{
    private static readonly OverlayLayer Layer = new(OverlayKind.Popover, "map-style");

    private int _closes;

    private RealmUiState Ui => Services.GetRequiredService<RealmUiState>();

    [Fact]
    public void AClosedPopover_HoldsNoLayer()
    {
        RenderPopover(open: false);

        Assert.Empty(Ui.Overlays);
        Assert.Equal(0, Ui.Depth);
    }

    [Fact]
    public void AnOpenPopover_IsOneLayer_UntilItsOwnerClosesIt()
    {
        var cut = RenderPopover(open: false);

        cut.Render(parameters => parameters.Add(popover => popover.Open, true));
        Assert.Equal(Layer, Assert.Single(Ui.Overlays));
        Assert.Equal(1, Ui.Depth);

        cut.Render(parameters => parameters.Add(popover => popover.Open, false));
        Assert.Empty(Ui.Overlays);
        Assert.Equal(0, _closes);   // the owner closed it; nothing asked it to
    }

    [Fact]
    public void ARepeatedRender_OfAnOpenPopover_DoesNotStackAnotherLayer()
    {
        var cut = RenderPopover(open: true);

        cut.Render();
        cut.Render(parameters => parameters.Add(popover => popover.StyleId, MapStyleIds.Day));

        Assert.Equal(Layer, Assert.Single(Ui.Overlays));
    }

    [Fact(DisplayName = "[R3-03] The Back gesture closes the open popover through OnClose, once, and the owner clearing Open afterwards finds the layer gone")]
    public void TheBackGesture_AsksTheOwnerToClose_Once()
    {
        var cut = RenderPopover(open: true);

        Assert.Equal(BackStep.Overlay, Ui.Back());

        cut.WaitForAssertion(() => Assert.Equal(1, _closes));
        Assert.Empty(Ui.Overlays);

        cut.Render(parameters => parameters.Add(popover => popover.Open, false));
        Assert.Empty(Ui.Overlays);
        Assert.Equal(1, _closes);
    }

    [Fact]
    public void ABackGesture_OnAnotherLayerAbove_ClosesThatFirst_AndLeavesThePopoverOpen()
    {
        RenderPopover(open: true);
        var closedAbove = false;
        Ui.OpenOverlay(new OverlayLayer(OverlayKind.Dialog, "above"), () => closedAbove = true);

        Assert.Equal(BackStep.Overlay, Ui.Back());

        Assert.True(closedAbove);
        Assert.Equal(Layer, Assert.Single(Ui.Overlays));
        Assert.Equal(0, _closes);
    }

    [Fact]
    public async Task APopoverStillOpenWhenItsOwnerGoes_TakesItsLayerWithIt()
    {
        var ui = Ui;
        RenderPopover(open: true);
        Assert.Equal(Layer, Assert.Single(ui.Overlays));

        await DisposeAsync();

        Assert.Empty(ui.Overlays);
    }

    private IRenderedComponent<MapStylePopover> RenderPopover(bool open) =>
        RenderWithProviders<MapStylePopover>(parameters => parameters
            .Add(popover => popover.Open, open)
            .Add(popover => popover.OnClose, EventCallback.Factory.Create(this, () => { _closes++; })));
}
