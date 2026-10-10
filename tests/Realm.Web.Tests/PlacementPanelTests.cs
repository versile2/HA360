using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Realm.Domain;
using Realm.Web.Components.Shell;
using Realm.Web.Formatting;
using Realm.Web.Sheet;
using Xunit;

namespace Realm.Web.Tests;

/// <summary>The bottom panel of "+ Add place" (0.2.2, D119, D120): the name is required, the slider runs 0 to 2000 m (or 6500 ft) in steps of 1 from 100 m, in the units of Home Assistant (0.2.3, D122), the icon picker offers the place kinds, and Save and Cancel raise the page's callbacks.</summary>
public sealed class PlacementPanelTests : ComponentTestBase
{
    [Fact]
    public void ThePanel_HasTheNameTheSliderTheKindsAndTheTwoButtons()
    {
        var cut = RenderWithProviders<PlacementPanel>();

        Assert.Equal("New place", cut.Find(".realm-placement__title").TextContent);
        var slider = cut.Find("[data-testid='placement-radius']");
        Assert.Equal("0", slider.GetAttribute("min"));
        Assert.Equal("2000", slider.GetAttribute("max"));
        Assert.Equal("1", slider.GetAttribute("step"));
        Assert.Equal("100", slider.GetAttribute("value"));
        Assert.Equal("100 m", cut.Find("[data-testid='placement-radius-text']").TextContent);
        Assert.Equal(PlaceKindIcons.Pickable.Count, cut.FindAll("[role='radio']").Count);
        Assert.Equal("true", cut.Find("[data-testid='placement-kind-other']").GetAttribute("aria-checked"));
        Assert.Equal("Save", cut.Find("[data-testid='placement-save']").TextContent);
        Assert.Equal("Cancel", cut.Find("[data-testid='placement-cancel']").TextContent);
    }

    [Fact]
    public async Task SaveWithoutAName_SaysSo_AndRaisesNothing()
    {
        PlacementDraft? draft = null;
        var cut = RenderWithProviders<PlacementPanel>(p => p.Add(x => x.OnSave, (PlacementDraft d) => draft = d));

        await cut.Find("[data-testid='placement-save']").TriggerEventAsync("onclick", new MouseEventArgs());

        Assert.Null(draft);
        Assert.Equal("Give the place a name.", cut.Find("[data-testid='placement-error']").TextContent);
        Assert.Equal("alert", cut.Find("[data-testid='placement-error']").GetAttribute("role"));
    }

    [Fact]
    public async Task SaveWithANameAndAKind_RaisesTheDraft_Trimmed()
    {
        PlacementDraft? draft = null;
        var cut = RenderWithProviders<PlacementPanel>(p => p.Add(x => x.OnSave, (PlacementDraft d) => draft = d));

        cut.Find("[data-testid='placement-name']").Input("  Grandma's  ");
        await cut.Find("[data-testid='placement-kind-family']").TriggerEventAsync("onclick", new MouseEventArgs());
        await cut.Find("[data-testid='placement-save']").TriggerEventAsync("onclick", new MouseEventArgs());

        Assert.Equal(new PlacementDraft("Grandma's", PlaceKind.Family), draft);
    }

    [Fact]
    public async Task TheSlider_RaisesTheRadius_ClampedToTheRange()
    {
        var radii = new List<double>();
        var cut = RenderWithProviders<PlacementPanel>(p => p.Add(x => x.RadiusMChanged, (double radius) => radii.Add(radius)));

        await cut.Find("[data-testid='placement-radius']").TriggerEventAsync("oninput", new ChangeEventArgs { Value = "450" });
        await cut.Find("[data-testid='placement-radius']").TriggerEventAsync("oninput", new ChangeEventArgs { Value = "99999" });
        await cut.Find("[data-testid='placement-radius']").TriggerEventAsync("oninput", new ChangeEventArgs { Value = "1" });

        Assert.Equal([450d, 2000d, 1d], radii);
    }

    [Fact]
    public void AnImperialPanel_HasFeetLimits_AndShowsFeet()
    {
        var cut = RenderWithProviders<PlacementPanel>(p => p.Add(x => x.Units, LengthUnits.Imperial));

        var slider = cut.Find("[data-testid='placement-radius']");
        Assert.Equal("0", slider.GetAttribute("min"));
        Assert.Equal("6500", slider.GetAttribute("max"));
        Assert.Equal("1", slider.GetAttribute("step"));
        Assert.Equal("328", slider.GetAttribute("value"));   // 100 m, Home Assistant's default
        Assert.Equal("328 ft", cut.Find("[data-testid='placement-radius-text']").TextContent);
        Assert.Equal("328 ft", slider.GetAttribute("aria-valuetext"));
    }

    [Fact]
    public async Task AnImperialSlider_RaisesMetres_ClampedToTheRange()
    {
        var radii = new List<double>();
        var cut = RenderWithProviders<PlacementPanel>(p => p.Add(x => x.Units, LengthUnits.Imperial).Add(x => x.RadiusMChanged, (double radius) => radii.Add(radius)));

        await cut.Find("[data-testid='placement-radius']").TriggerEventAsync("oninput", new ChangeEventArgs { Value = "328" });
        await cut.Find("[data-testid='placement-radius']").TriggerEventAsync("oninput", new ChangeEventArgs { Value = "99999" });

        Assert.Equal(2, radii.Count);
        Assert.Equal(99.97, radii[0], 2);
        Assert.Equal(2000d, radii[1]);
    }

    [Fact]
    public async Task Cancel_AndEscInTheNameField_RaiseOnCancel()
    {
        var cancels = 0;
        var cut = RenderWithProviders<PlacementPanel>(p => p.Add(x => x.OnCancel, () => cancels++));

        await cut.Find("[data-testid='placement-cancel']").TriggerEventAsync("onclick", new MouseEventArgs());
        await cut.Find("[data-testid='placement-name']").TriggerEventAsync("onkeydown", new KeyboardEventArgs { Key = "Escape" });
        await cut.Find("[data-testid='placement-radius']").TriggerEventAsync("onkeydown", new KeyboardEventArgs { Key = "Enter" });

        Assert.Equal(2, cancels);   // the button and Esc in the name field; Enter in the slider does nothing
    }

    [Fact]
    public void AnErrorFromThePage_ShowsInThePanel_AndABusyPanelCannotSave()
    {
        var cut = RenderWithProviders<PlacementPanel>(p => p.Add(x => x.Error, "Home Assistant did not allow this app to add a place.").Add(x => x.Busy, true));

        Assert.Equal("Home Assistant did not allow this app to add a place.", cut.Find("[data-testid='placement-error']").TextContent);
        Assert.True(cut.Find("[data-testid='placement-save']").HasAttribute("disabled"));
    }

    [Theory]
    [InlineData(LengthUnits.Metric, 25, "25 m")]
    [InlineData(LengthUnits.Metric, 1500, "1.5 km")]
    [InlineData(LengthUnits.Imperial, 100, "328 ft")]
    [InlineData(LengthUnits.Imperial, 2000, "1.24 mi")]
    public void TheRadiusText_IsOneUnitOfTheUnitSystem(LengthUnits units, double meters, string expected) => Assert.Equal(expected, PlacementFormatter.Radius(meters, units));
}
