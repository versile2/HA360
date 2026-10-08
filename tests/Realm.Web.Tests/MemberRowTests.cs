using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Realm.Demo;
using Realm.Domain;
using Realm.Web.Components.Sheet;
using Realm.Web.Sheet;
using Xunit;

namespace Realm.Web.Tests;

/// <summary>
/// The person row of 01 section 5.1 (<see cref="MemberRow"/>): the three lines of the Demo cast as the spec words them (AC-25 to AC-27), the low-battery name, the stale and offline
/// dimming, the photo over the initial, and a display name such as <c>&lt;img onerror=x&gt;</c> reading as text. The row is drawn from a <see cref="MemberRowVm"/> that
/// <see cref="VmFactory"/> builds, so every string here is the one the formatters made; what the row looks like on screen belongs to the Playwright specs.
/// </summary>
public sealed class MemberRowTests : ComponentTestBase
{
    private static readonly IRealmSession Session = FullCast.Session(null);

    private static readonly RealmSnapshot Demo = Session.Current;

    private static readonly DateTimeOffset DemoNow = Session.Time.GetUtcNow();

    // ---- the cast, as the spec words it ---------------------------------------------------------------------------------------------------

    [Fact(DisplayName = "[AC-25] Alden's row: name, lore, L2 at his hall, L3 Since 5:52 pm, a charging 19% pill, no distance")]
    public void Alden_ReadsTheThreeLines_WithAChargingPill()
    {
        var cut = RenderRow(DemoRow(DemoCast.King.Id));

        var row = cut.Find("[data-testid='row-member-king']");
        Assert.Equal(DemoCast.King.Name, cut.Find(".realm-row-name").TextContent);
        Assert.Equal(DemoCast.King.Lore, cut.Find(".realm-row-lore").TextContent);
        Assert.Equal("At " + DemoPlaces.Home.Name, cut.Find(".realm-row-status").TextContent);
        Assert.Equal("Since 5:52 pm", cut.Find(".realm-row-detail-text").TextContent);
        Assert.DoesNotContain("away", row.TextContent, StringComparison.Ordinal);

        var pill = cut.Find(".realm-battery-pill");
        Assert.Equal("19%", pill.TextContent.Trim());
        Assert.Equal("true", pill.GetAttribute("data-charging"));
        Assert.Contains("charging", pill.GetAttribute("aria-label"), StringComparison.Ordinal);
        Assert.Null(pill.GetAttribute("data-low"));
    }

    [Fact(DisplayName = "[AC-26] Briar's row reads Driving · 54 mph with her street and Since 9:12 pm; Cass's reads 1.0 mi away and a low pill whose name says low")]
    public void BriarDrives_AndCassIsLow()
    {
        var briar = RenderRow(DemoRow(DemoCast.Queen.Id));
        Assert.Equal($"Driving · 54 mph on {DemoCast.Queen.Address}", briar.Find(".realm-row-status").TextContent);
        Assert.Equal("Since 9:12 pm", briar.Find(".realm-row-detail-text").TextContent);
        Assert.Equal("62%", briar.Find(".realm-battery-pill").TextContent.Trim());

        var cass = RenderRow(DemoRow(DemoCast.Jester.Id));
        Assert.Equal("At " + DemoPlaces.JesterHall.Name, cass.Find(".realm-row-status").TextContent);
        Assert.Equal("Since 9:06 pm · 1.0 mi away", cass.Find(".realm-row-detail-text").TextContent);
        var pill = cass.Find(".realm-battery-pill");
        Assert.Equal("12%", pill.TextContent.Trim());
        Assert.Contains("realm-battery-pill--low", pill.ClassName, StringComparison.Ordinal);
        Assert.Equal("true", pill.GetAttribute("data-low"));
        Assert.Contains("low", pill.GetAttribute("aria-label"), StringComparison.Ordinal);
        Assert.Contains("low", cass.Find("[data-testid='row-member-jester']").GetAttribute("aria-label"), StringComparison.Ordinal);
    }

    [Fact(DisplayName = "[AC-27] Dara's row is dimmed to the stale class with a warning line and a clock; Elio's has no pill and keeps his lore")]
    public void Dara_IsStale_AndElioIsStatic()
    {
        var dara = RenderRow(DemoRow(DemoCast.Cryptid.Id));
        var daraRow = dara.Find("[data-testid='row-member-cryptid']");
        Assert.Contains("realm-row--stale", daraRow.ClassName, StringComparison.Ordinal);
        Assert.DoesNotContain("realm-row--offline", daraRow.ClassName, StringComparison.Ordinal);
        Assert.Equal("The raven's late — last seen 42 min ago", dara.Find(".realm-row-detail-text").TextContent);
        Assert.Contains("realm-row-detail--warning", dara.Find(".realm-row-detail").ClassName, StringComparison.Ordinal);
        Assert.Single(dara.FindAll(".realm-row-clock"));
        Assert.Equal("10%", dara.Find(".realm-battery-pill").TextContent.Trim());
        Assert.Contains("battery as of 42 min ago", dara.Find(".realm-battery-pill").GetAttribute("aria-label"), StringComparison.Ordinal);

        var elio = RenderRow(DemoRow(DemoCast.Prince.Id));
        Assert.Equal(DemoCast.Prince.StaticLabel, elio.Find(".realm-row-status").TextContent);
        Assert.Equal("Location isn't shared", elio.Find(".realm-row-detail-text").TextContent);
        Assert.Equal(DemoCast.Prince.Lore, elio.Find(".realm-row-lore").TextContent);
        Assert.Empty(elio.FindAll(".realm-battery-pill"));
        Assert.Empty(elio.FindAll(".realm-row-clock"));
    }

    [Fact]
    public void AnOfflineRow_IsDimmedFurther_AndItsLineUsesTheStaleColourWithoutTheClock()
    {
        var offline = Demo.Members.Single(member => member.Id == DemoCast.Cryptid.Id) with { Freshness = Freshness.Offline, LastUpdateUtc = DemoNow - TimeSpan.FromDays(2) };

        var cut = RenderRow(RowOf(offline));

        Assert.Contains("realm-row--offline", cut.Find(".realm-row").ClassName, StringComparison.Ordinal);
        Assert.DoesNotContain("realm-row--stale", cut.Find(".realm-row").ClassName, StringComparison.Ordinal);
        Assert.StartsWith("Gone dark — last seen ", cut.Find(".realm-row-detail-text").TextContent, StringComparison.Ordinal);
        Assert.Contains("realm-row-detail--stale", cut.Find(".realm-row-detail").ClassName, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll(".realm-row-clock"));
    }

    // ---- the accessible name -------------------------------------------------------------------------------------------------------------

    [Fact(DisplayName = "[AC-46] a row is one button named with the pin's name and Double tap to show on map")]
    public void TheRow_IsOneButton_NamedLikeThePin_PlusTheHint()
    {
        var cut = RenderRow(DemoRow(DemoCast.Jester.Id));

        var button = cut.Find("[data-testid='row-member-jester']");
        Assert.Equal("button", button.TagName.ToLowerInvariant());
        Assert.Equal("button", button.GetAttribute("type"));
        Assert.Single(cut.FindAll("button"));
        Assert.Equal(
            $"{DemoCast.Jester.Name}, {DemoCast.Jester.Lore}. At {DemoPlaces.JesterHall.Name} since 9:06 pm. Battery 12 percent, low. 1.0 mile away. Double tap to show on map.",
            button.GetAttribute("aria-label"));
    }

    [Fact]
    public void TheAvatarTheLinesAndThePill_AreNotNamedTwice()
    {
        var cut = RenderRow(DemoRow(DemoCast.King.Id));

        // The button's own aria-label is its name; the initial is hidden from assistive technology and the photo (when there is one) is decorative.
        Assert.Equal("true", cut.Find(".realm-row-initial").GetAttribute("aria-hidden"));
        Assert.Empty(cut.FindAll("img"));
    }

    // ---- selection and the tap -----------------------------------------------------------------------------------------------------------

    [Fact]
    public void ARowThatIsNotSelected_HasNoAriaCurrent_AndTheSelectedOneHasIt()
    {
        var row = DemoRow(DemoCast.King.Id);

        var plain = RenderRow(row);
        Assert.Null(plain.Find(".realm-row").GetAttribute("aria-current"));
        Assert.DoesNotContain("realm-row--selected", plain.Find(".realm-row").ClassName, StringComparison.Ordinal);

        var selected = RenderRow(row, selected: true);
        Assert.Equal("true", selected.Find(".realm-row").GetAttribute("aria-current"));
        Assert.Contains("realm-row--selected", selected.Find(".realm-row").ClassName, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ATap_RaisesOnClick_Once()
    {
        var clicks = 0;
        var cut = RenderWithProviders<MemberRow>(row => row
            .Add(p => p.Row, DemoRow(DemoCast.King.Id))
            .Add(p => p.OnClick, () =>
            {
                clicks++;
                return Task.CompletedTask;
            }));

        await cut.Find("[data-testid='row-member-king']").TriggerEventAsync("onclick", new MouseEventArgs());

        Assert.Equal(1, clicks);
    }

    // ---- the title -------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void ALoreTitle_IsItsOwnElement_AndIsLeftOutWhenThereIsNone()
    {
        var withLore = RenderRow(RowOf(Demo.Members[0]));
        Assert.Equal(DemoCast.King.Lore, withLore.Find(".realm-row-lore").TextContent);

        var without = RenderRow(RowOf(Demo.Members[0] with { LoreTitle = null }));
        Assert.Empty(without.FindAll(".realm-row-lore"));
        Assert.Equal(DemoCast.King.Name, without.Find(".realm-row-name").TextContent);
    }

    [Fact(DisplayName = "[AC-25a] a display name such as <img onerror=x> renders as text, never as an element")]
    public void AHostileName_RendersAsText()
    {
        const string hostile = "<img onerror=x>";
        var member = Demo.Members[0] with { DisplayName = hostile, LoreTitle = "<b onclick=y>", Street = "<script>z</script>", PlaceId = null };

        var cut = RenderRow(RowOf(member));

        Assert.Equal(hostile, cut.Find(".realm-row-name").TextContent);
        Assert.Equal("<b onclick=y>", cut.Find(".realm-row-lore").TextContent);
        Assert.Equal("<script>z</script>", cut.Find(".realm-row-status").TextContent);
        Assert.Empty(cut.FindAll("img"));
        Assert.Empty(cut.FindAll("script"));
        Assert.Empty(cut.FindAll("b"));
        Assert.Contains("&lt;img onerror=x&gt;", cut.Markup, StringComparison.Ordinal);
        Assert.Equal("<", cut.Find(".realm-row-initial").TextContent);
        Assert.StartsWith(hostile + ", ", cut.Find(".realm-row").GetAttribute("aria-label"), StringComparison.Ordinal);
    }

    // ---- the avatar ---------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void TheInitial_IsOnTheMemberColour_AndAPhotoIsLaidOverIt_WithAnEmptyAlt()
    {
        var plain = RenderRow(RowOf(Demo.Members[0]));
        Assert.Equal("A", plain.Find(".realm-row-initial").TextContent);
        Assert.Equal($"--realm-row-color:{DemoCast.King.Color}", plain.Find(".realm-row-avatar").GetAttribute("style"));
        Assert.Empty(plain.FindAll(".realm-row-photo"));

        var photo = RenderRow(RowOf(Demo.Members[0] with { AvatarUrl = "avatar/king" }));
        var image = photo.Find("img.realm-row-photo");
        Assert.Equal("avatar/king", image.GetAttribute("src"));
        Assert.Equal(string.Empty, image.GetAttribute("alt"));
        Assert.Equal("A", photo.Find(".realm-row-initial").TextContent);       // still there, under the photo, for when it fails to load
    }

    [Fact]
    public void AColourThatIsNotAPlainHexValue_NeverReachesTheStyleAttribute()
    {
        var cut = RenderRow(RowOf(Demo.Members[0] with { Color = "red;background:url(javascript:x)" }));

        Assert.Null(cut.Find(".realm-row-avatar").GetAttribute("style"));
        Assert.DoesNotContain("javascript", cut.Markup, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(19, true, false)]
    [InlineData(14, false, true)]
    [InlineData(14, true, true)]
    [InlineData(62, false, false)]
    public void ThePill_ShowsThePercentage_AndMarksChargingAndLow(int percent, bool charging, bool low)
    {
        var cut = RenderRow(RowOf(Demo.Members[0] with { BatteryPct = percent, Charging = charging }));

        var pill = cut.Find(".realm-battery-pill");
        Assert.Equal(percent + "%", pill.TextContent.Trim());
        Assert.Single(pill.QuerySelectorAll("svg"));
        Assert.Equal(charging ? "true" : null, pill.GetAttribute("data-charging"));
        Assert.Equal(low ? "true" : null, pill.GetAttribute("data-low"));
        Assert.Equal(low, pill.ClassName!.Contains("realm-battery-pill--low", StringComparison.Ordinal));
    }

    // ---- helpers ---------------------------------------------------------------------------------------------------------------------------

    private static RowFacts Facts() => RowFacts.Create(Demo.Members, Demo.Places, null, DemoNow, Session.Zone);

    private static MemberRowVm DemoRow(string id) => VmFactory.Member(Demo.Members.Single(member => member.Id == id), Facts());

    private static MemberRowVm RowOf(MemberVm member) => VmFactory.Member(member, Facts());

    private IRenderedComponent<MemberRow> RenderRow(MemberRowVm row, bool selected = false) =>
        RenderWithProviders<MemberRow>(component => component.Add(p => p.Row, row).Add(p => p.Selected, selected));
}
