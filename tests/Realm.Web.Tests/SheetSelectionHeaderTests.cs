using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Realm.Demo;
using Realm.Domain;
using Realm.Web.Components.Sheet;
using Realm.Web.Formatting;
using Realm.Web.Sheet;
using Xunit;

namespace Realm.Web.Tests;

/// <summary>
/// The selection header (<see cref="SheetSelectionHeader"/>, 01 section 3.4.2, D45 and D46) and its floating pill: a tap anywhere on the header but the X raises <c>OnToggle</c> once and never
/// <c>OnClear</c>; the X raises <c>OnClear</c> once and never <c>OnToggle</c>; the group is named by the accessible name and its lines are hidden from screen readers; the battery pill shows for
/// a person with a reading and for nobody else; the lines are the view model's; the avatar is the person's initial and colour or the vehicle's or the zone's icon; and the pill is the same
/// view model with an inert body (A-2). The header is built from the Demo cast through <see cref="SelectionHeaderFormatter"/>, so no string a test compares is retyped.
/// </summary>
public sealed class SheetSelectionHeaderTests : ComponentTestBase
{
    private const string Root = "[data-testid='sheet-selection-header']";

    private const string Clear = "[data-testid='sheet-selection-clear']";

    private const string Battery = "[data-testid='sheet-selection-battery']";

    private static readonly IRealmSession Session = new DemoRealmSessionFactory().Create(null);

    private static readonly RealmSnapshot Demo = Session.Current;

    private static readonly DateTimeOffset DemoNow = Session.Time.GetUtcNow();

    private static readonly RowFacts Facts = RowFacts.Create(Demo.Members, Demo.Places, null, DemoNow, Session.Zone);

    private static SelectionHeaderVm MemberHeader(string id) => SelectionHeaderFormatter.Member(Demo.Members.Single(member => member.Id == id), Facts);

    private static SelectionHeaderVm VehicleHeader(string id) => SelectionHeaderFormatter.Vehicle(Demo.Vehicles.Single(vehicle => vehicle.Id == id), Facts);

    private static SelectionHeaderVm PlaceHeader(string id) => SelectionHeaderFormatter.Place(Demo.Places.Single(place => place.Id == id));

    private static SelectionHeaderVm Cass() => MemberHeader(DemoCast.Jester.Id);

    // ---- D46: the body toggles, the X clears ---------------------------------------------------------------------------------------------------

    [Theory(DisplayName = "[AC-23a] A tap anywhere on the header but the X raises OnToggle once and OnClear never (D46)")]
    [InlineData(Root)]
    [InlineData(".realm-sel-avatar")]
    [InlineData(".realm-sel-body")]
    [InlineData(".realm-sel-title")]
    [InlineData(".realm-sel-line")]
    [InlineData(Battery)]
    public async Task ATap_AnywhereButTheX_RaisesOnToggle_Once_AndNeverOnClear(string target)
    {
        var calls = new Calls();
        var cut = Draw(Cass(), calls);

        await cut.Find(target).TriggerEventAsync("onclick", new MouseEventArgs());

        Assert.Equal(1, calls.Toggles);
        Assert.Equal(0, calls.Clears);
    }

    [Fact(DisplayName = "[AC-23b] The X raises OnClear once and OnToggle never: its click stops before it reaches the header (D46)")]
    public async Task TheX_RaisesOnClear_Once_AndNeverOnToggle()
    {
        var calls = new Calls();
        var cut = Draw(Cass(), calls);

        await cut.Find(Clear).TriggerEventAsync("onclick", new MouseEventArgs());

        Assert.Equal(1, calls.Clears);
        Assert.Equal(0, calls.Toggles);
    }

    [Fact]
    public async Task ASecondTap_RaisesTheCallbackAgain_TheHeaderKeepsNoStateOfItsOwn()
    {
        var calls = new Calls();
        var cut = Draw(Cass(), calls);

        await cut.Find(Root).TriggerEventAsync("onclick", new MouseEventArgs());
        await cut.Find(Root).TriggerEventAsync("onclick", new MouseEventArgs());

        Assert.Equal(2, calls.Toggles);
    }

    [Fact]
    public async Task WithNoHandlers_ATapAndTheX_AreNoOps()
    {
        var cut = Draw(Cass());

        await cut.Find(Root).TriggerEventAsync("onclick", new MouseEventArgs());
        await cut.Find(Clear).TriggerEventAsync("onclick", new MouseEventArgs());

        Assert.NotEmpty(cut.FindAll(Root));
    }

    // ---- the group, the X and the keyboard -------------------------------------------------------------------------------------------------------

    [Fact(DisplayName = "[AC-22c] The header is role=group named by the accessible name, and its lines and battery pill are hidden from screen readers")]
    public void TheHeader_IsAGroup_NamedByTheAccessibleName_AndItsLinesAreHidden()
    {
        var header = Cass();
        var cut = Draw(header);

        var root = cut.Find(Root);
        Assert.Equal("group", root.GetAttribute("role"));
        Assert.Equal(header.AccessibleName, root.GetAttribute("aria-label"));
        Assert.Equal("true", cut.Find(".realm-sel-body").GetAttribute("aria-hidden"));
        Assert.Equal("true", cut.Find(Battery).GetAttribute("aria-hidden"));
        Assert.Equal("true", cut.Find(".realm-sel-avatar").GetAttribute("aria-hidden"));
    }

    [Fact]
    public void TheX_IsTheOnlyButton_NamedClearSelection_AndTheHeaderIsNeitherAButtonNorATabStop()
    {
        var cut = Draw(Cass());

        var buttons = cut.FindAll("button");
        var clear = Assert.Single(buttons);
        Assert.Equal("button", clear.GetAttribute("type"));
        Assert.Equal(SelectionHeaderFormatter.ClearLabel, clear.GetAttribute("aria-label"));
        Assert.Equal("Clear selection", clear.GetAttribute("aria-label"));
        Assert.Equal("sheet-selection-clear", clear.GetAttribute("data-testid"));

        var root = cut.Find(Root);
        Assert.Null(root.GetAttribute("tabindex"));
        Assert.NotEqual("button", root.GetAttribute("role"));
        Assert.Empty(cut.FindAll("a"));
    }

    // ---- the battery pill ---------------------------------------------------------------------------------------------------------------------

    [Fact(DisplayName = "[AC-22d] Cass's header carries the 12% battery pill in the low style")]
    public void Cass_HasTheBatteryPill_InTheLowStyle()
    {
        var cut = Draw(Cass());

        var pill = cut.Find(Battery);
        Assert.Equal("12%", pill.QuerySelector(".realm-battery-text")!.TextContent);
        Assert.Contains("realm-battery-pill", pill.ClassList);
        Assert.Contains("realm-battery-pill--low", pill.ClassList);
        Assert.Equal("true", pill.GetAttribute("data-low"));
        Assert.Null(pill.GetAttribute("data-charging"));
        Assert.Single(pill.QuerySelectorAll("svg"));
    }

    [Fact]
    public void Alden_HasTheChargingPill_NotInTheLowStyle()
    {
        var cut = Draw(MemberHeader(DemoCast.King.Id));

        var pill = cut.Find(Battery);
        Assert.Equal("19%", pill.QuerySelector(".realm-battery-text")!.TextContent);
        Assert.Equal("true", pill.GetAttribute("data-charging"));
        Assert.Null(pill.GetAttribute("data-low"));
        Assert.DoesNotContain("realm-battery-pill--low", pill.ClassList);
    }

    [Fact]
    public void ChargingAndLowTogether_ShowTheBoltAndTheLowStyle()
    {
        var charging = new BatteryBadgeVm(9, "9%", true, true, "Battery 9 percent, charging, low");
        var cut = Draw(Cass() with { Battery = charging });

        var pill = cut.Find(Battery);
        Assert.Equal("true", pill.GetAttribute("data-charging"));
        Assert.Equal("true", pill.GetAttribute("data-low"));
        Assert.Contains("realm-battery-pill--low", pill.ClassList);
    }

    [Fact]
    public void TheStaticPrince_TheWagonAndAPlace_HaveNoBatteryPill()
    {
        Assert.Empty(Draw(MemberHeader(DemoCast.Prince.Id)).FindAll(Battery));
        Assert.Empty(Draw(VehicleHeader(DemoCast.Wagon.Id)).FindAll(Battery));
        Assert.Empty(Draw(PlaceHeader(DemoPlaces.Home.Id)).FindAll(Battery));
        Assert.Empty(Draw(Cass() with { Battery = null }).FindAll(Battery));
    }

    // ---- the lines ---------------------------------------------------------------------------------------------------------------------------

    [Fact(DisplayName = "[AC-22e] Cass: name and lore on line 1, the status line and the time on line 2")]
    public void Cass_ReadsNameAndLore_ThenTheStatusLineAndTheTime()
    {
        var header = Cass();
        var cut = Draw(header);

        Assert.Equal(header.Line1, cut.Find(".realm-row-name").TextContent);
        Assert.Equal(header.Line1Lore, cut.Find(".realm-row-lore").TextContent);
        Assert.Equal(header.Line2Lead, cut.Find(".realm-sel-lead").TextContent);
        Assert.Equal(SelectionHeaderFormatter.Separator + header.Line2Tail, cut.Find(".realm-sel-tail").TextContent);
        Assert.Equal(header.Line2, cut.Find(".realm-sel-line").TextContent);
        Assert.Equal(DemoCast.Jester.Name, cut.Find(".realm-row-name").TextContent);
        Assert.Equal(DemoCast.Jester.Lore, cut.Find(".realm-row-lore").TextContent);
    }

    [Fact]
    public void ANullLore_HasNoLoreElement_AndANullTail_HasNoTailElement()
    {
        var cut = Draw(Cass() with { Line1Lore = null, Line2Tail = null });

        Assert.Empty(cut.FindAll(".realm-row-lore"));
        Assert.Empty(cut.FindAll(".realm-sel-tail"));
        Assert.Equal(Cass().Line2Lead, cut.Find(".realm-sel-line").TextContent);
    }

    [Fact]
    public void TheStaticPrince_ReadsHisLabelAlone()
    {
        var cut = Draw(MemberHeader(DemoCast.Prince.Id));

        Assert.Equal(DemoCast.Prince.Name, cut.Find(".realm-row-name").TextContent);
        Assert.Equal(DemoCast.Prince.StaticLabel, cut.Find(".realm-sel-line").TextContent);
        Assert.Empty(cut.FindAll(".realm-sel-tail"));
    }

    [Fact]
    public void TheWagon_ReadsLocationEngineAndTheUpdate()
    {
        var header = VehicleHeader(DemoCast.Wagon.Id);
        var cut = Draw(header);

        Assert.Equal(DemoCast.Wagon.Name, cut.Find(".realm-row-name").TextContent);
        Assert.Equal(DemoCast.Wagon.Lore, cut.Find(".realm-row-lore").TextContent);
        Assert.Equal(header.Line2, cut.Find(".realm-sel-line").TextContent);
        Assert.Equal(SelectionHeaderFormatter.Separator + "Updated 20 min ago", cut.Find(".realm-sel-tail").TextContent);
    }

    [Fact]
    public void APlace_ReadsItsNameAndHereNow()
    {
        var cut = Draw(PlaceHeader(DemoPlaces.Home.Id));

        Assert.Equal(DemoPlaces.Home.Name, cut.Find(".realm-row-name").TextContent);
        Assert.Empty(cut.FindAll(".realm-row-lore"));
        Assert.Equal("Here now (2)", cut.Find(".realm-sel-line").TextContent);
    }

    [Fact]
    public void AHostileName_IsRenderedAsText_NeverAsMarkup()
    {
        const string hostile = "<script>alert(1)</script><b>x</b>";
        var cut = Draw(Cass() with { Line1 = hostile, Line1Lore = hostile, Line2Lead = hostile, AccessibleName = hostile });

        Assert.Empty(cut.FindAll("script"));
        Assert.Empty(cut.FindAll("b"));
        Assert.Equal(hostile, cut.Find(".realm-row-name").TextContent);
        Assert.Equal(hostile, cut.Find(".realm-row-lore").TextContent);
        Assert.Equal(hostile, cut.Find(".realm-sel-lead").TextContent);
        Assert.Equal(hostile, cut.Find(Root).GetAttribute("aria-label"));
    }

    // ---- the avatar -------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void APerson_HasTheInitialAndTheColour_AndNoTileIcon()
    {
        var cut = Draw(Cass());

        var avatar = cut.Find(".realm-sel-avatar");
        Assert.DoesNotContain("realm-sel-avatar--tile", avatar.ClassList);
        Assert.Equal("C", avatar.QuerySelector(".realm-row-initial")!.TextContent);
        Assert.Equal("--realm-row-color:" + DemoCast.Jester.Color, avatar.GetAttribute("style"));
        Assert.Empty(avatar.QuerySelectorAll("img"));
        Assert.Empty(avatar.QuerySelectorAll("svg"));
    }

    [Fact]
    public void APhoto_IsALazyImageOverTheInitial_AndNoColourMeansNoStyle()
    {
        var cut = Draw(Cass() with { AvatarUrl = "avatar/jester", Color = null });

        var avatar = cut.Find(".realm-sel-avatar");
        var photo = Assert.Single(avatar.QuerySelectorAll("img.realm-row-photo"));
        Assert.Equal("avatar/jester", photo.GetAttribute("src"));
        Assert.Equal(string.Empty, photo.GetAttribute("alt"));
        Assert.Equal("lazy", photo.GetAttribute("loading"));
        Assert.Null(avatar.GetAttribute("style"));
        Assert.Single(avatar.QuerySelectorAll(".realm-row-initial"));
    }

    [Fact]
    public void TheWagon_HasTheTruckSilhouetteInATile_AndTheHatchbackTheCarIcon()
    {
        var truck = Draw(VehicleHeader(DemoCast.Wagon.Id)).Find(".realm-sel-avatar");
        var car = Draw(VehicleHeader(DemoCast.Chariot.Id)).Find(".realm-sel-avatar");

        Assert.Contains("realm-sel-avatar--tile", truck.ClassList);
        Assert.Contains("realm-sel-avatar--tile", car.ClassList);
        Assert.Equal(4, truck.InnerHtml.Split("<path", StringSplitOptions.None).Length - 1);
        Assert.NotEqual(truck.InnerHtml, car.InnerHtml);
        Assert.Empty(truck.QuerySelectorAll(".realm-row-initial"));
        Assert.Null(truck.GetAttribute("style"));
    }

    [Fact]
    public void APlace_HasItsKindsIconInATile()
    {
        var home = Draw(PlaceHeader(DemoPlaces.Home.Id)).Find(".realm-sel-avatar");
        var vet = Draw(PlaceHeader(DemoPlaces.Vet.Id)).Find(".realm-sel-avatar");

        Assert.Contains("realm-sel-avatar--tile", home.ClassList);
        Assert.Single(home.QuerySelectorAll("svg"));
        Assert.Single(vet.QuerySelectorAll("svg"));
        Assert.NotEqual(home.InnerHtml, vet.InnerHtml);
        Assert.Empty(home.QuerySelectorAll(".realm-row-initial"));
    }

    // ---- the pill ---------------------------------------------------------------------------------------------------------------------------

    [Fact(DisplayName = "The pill variant renders the same view model: the same avatar, lines, battery pill and X, under the pill class")]
    public void ThePill_RendersTheSameViewModel_AsTheHeader()
    {
        var header = Cass();
        var sheet = Draw(header);
        var pill = Draw(header, pill: true);

        Assert.Equal("realm-sel", sheet.Find(Root).ClassName);
        Assert.Equal("realm-sel realm-sel--pill", pill.Find(Root).ClassName);
        Assert.Equal("group", pill.Find(Root).GetAttribute("role"));
        Assert.Equal(header.AccessibleName, pill.Find(Root).GetAttribute("aria-label"));

        var sheetChildren = sheet.Find(Root).Children;
        var pillChildren = pill.Find(Root).Children;
        Assert.Equal(sheetChildren.Length, pillChildren.Length);
        for (var index = 0; index < sheetChildren.Length; index++)
        {
            sheetChildren[index].MarkupMatches(pillChildren[index].OuterHtml);
        }
    }

    [Fact(DisplayName = "The pill's body is inert even when OnToggle is passed (A-2); only its X acts")]
    public async Task ThePill_BodyIsInert_AndOnlyTheXActs()
    {
        var calls = new Calls();
        var cut = Draw(Cass(), calls, pill: true);

        await cut.Find(Root).TriggerEventAsync("onclick", new MouseEventArgs());
        await cut.Find(".realm-sel-body").TriggerEventAsync("onclick", new MouseEventArgs());
        await cut.Find(".realm-sel-avatar").TriggerEventAsync("onclick", new MouseEventArgs());
        Assert.Equal(0, calls.Toggles);
        Assert.Equal(0, calls.Clears);

        await cut.Find(Clear).TriggerEventAsync("onclick", new MouseEventArgs());
        Assert.Equal(1, calls.Clears);
        Assert.Equal(0, calls.Toggles);
    }

    [Fact]
    public void ThePill_IsStillNotAButtonNorATabStop_AndItsXIsTheOnlyButton()
    {
        var cut = Draw(Cass(), pill: true);

        Assert.Single(cut.FindAll("button"));
        Assert.Null(cut.Find(Root).GetAttribute("tabindex"));
        Assert.Equal("Clear selection", cut.Find(Clear).GetAttribute("aria-label"));
    }

    // ---- helpers ---------------------------------------------------------------------------------------------------------------------------

    // Counts what the header raised; null handlers (no calls object) leave both callbacks unset.
    private sealed class Calls
    {
        public int Toggles { get; set; }

        public int Clears { get; set; }
    }

    private IRenderedComponent<SheetSelectionHeader> Draw(SelectionHeaderVm header, Calls? calls = null, bool pill = false) =>
        RenderWithProviders<SheetSelectionHeader>(component =>
        {
            component
                .Add(p => p.Header, header)
                .Add(p => p.Pill, pill);
            if (calls is not null)
            {
                component.Add(p => p.OnToggle, () =>
                {
                    calls.Toggles++;
                    return Task.CompletedTask;
                });
                component.Add(p => p.OnClear, () =>
                {
                    calls.Clears++;
                    return Task.CompletedTask;
                });
            }
        });
}
