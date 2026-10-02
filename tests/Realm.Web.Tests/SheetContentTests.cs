using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Realm.Demo;
using Realm.Domain;
using Realm.Web.Components.Sheet;
using Xunit;

namespace Realm.Web.Tests;

/// <summary>
/// The list body of the sheet (<see cref="SheetContent"/>, 01 sections 5.1 to 5.3): the rows of each section from the Demo cast, in the order of the spec, with the test ids of 01
/// Appendix B and the summary's counts; the placeholder vehicle; the entity a tap raises for S8; the same markup in the bottom sheet and in the panel; and the clock and zone, which
/// come from the parameters or the cascaded session and never from the browser. The row components themselves are covered by <see cref="MemberRowTests"/>.
/// </summary>
public sealed class SheetContentTests : ComponentTestBase
{
    private static readonly IRealmSession Session = new DemoRealmSessionFactory().Create(null);

    private static readonly RealmSnapshot Demo = Session.Current;

    private static readonly DateTimeOffset DemoNow = Session.Time.GetUtcNow();

    // ---- Drivers ------------------------------------------------------------------------------------------------------------------------

    [Fact(DisplayName = "[AC-25] Drivers lists five rows in the order Alden, Briar, Cass, Dara, Elio, each a button with the test id of Appendix B")]
    public void Drivers_ListsTheFivePeopleInOrder_EachAButton()
    {
        var cut = Content(Section.Drivers);

        var rows = cut.FindAll("[data-testid='sheet-list'] > li > button");
        Assert.Equal(DemoCast.Members.Select(member => $"row-member-{member.Id}"), rows.Select(row => row.GetAttribute("data-testid")));
        Assert.Equal(DemoCast.Members.Select(member => member.Name), rows.Select(row => row.QuerySelector(".realm-row-name")!.TextContent));
        Assert.Equal(5, cut.FindAll("[data-testid='sheet-list'] > li").Count);
        Assert.Equal("Since 5:52 pm", cut.Find("[data-testid='row-member-king'] .realm-row-detail-text").TextContent);
    }

    [Fact]
    public void Drivers_TheRowsAreWrittenAtTheClockAndZoneGiven()
    {
        // The same instant in UTC: Alden's arrival, 5:52 pm in Chicago, is 10:52 pm there, and (it being 2:25 am on Oct 1) "yesterday".
        var cut = RenderWithProviders<SheetContent>(content => content
            .Add(p => p.Members, Demo.Members)
            .Add(p => p.Places, Demo.Places)
            .Add(p => p.Now, (DateTimeOffset?)DemoNow)
            .Add(p => p.Zone, (TimeZoneInfo?)TimeZoneInfo.Utc));

        Assert.Equal("Since yesterday 10:52 pm", cut.Find("[data-testid='row-member-king'] .realm-row-detail-text").TextContent);
    }

    [Fact]
    public void Drivers_WithoutParameters_TheCascadedSessionGivesTheClockAndTheZone()
    {
        // RealmShell cascades the session; it reaches the sheet's content (D67), so a page that passes neither Now nor Zone still reads the frozen Demo clock in HA's zone.
        var cut = RenderWithProviders<CascadingValue<IRealmSession>>(value => value
            .Add(p => p.Value, Session)
            .Add(p => p.ChildContent, (RenderFragment)(builder =>
            {
                builder.OpenComponent<SheetContent>(0);
                builder.AddAttribute(1, nameof(SheetContent.Members), Demo.Members);
                builder.AddAttribute(2, nameof(SheetContent.Places), Demo.Places);
                builder.CloseComponent();
            })));

        Assert.Equal("Since 5:52 pm", cut.Find("[data-testid='row-member-king'] .realm-row-detail-text").TextContent);
        Assert.Equal("The raven's late — last seen 42 min ago", cut.Find("[data-testid='row-member-cryptid'] .realm-row-detail-text").TextContent);
    }

    [Fact]
    public void Drivers_TheViewerIsMe_SoTheirRowHasNoDistance_AndTheOthersAreMeasuredFromThem()
    {
        var fromAlden = Content(Section.Drivers);
        Assert.Equal("Since 9:06 pm · 1.0 mi away", fromAlden.Find("[data-testid='row-member-jester'] .realm-row-detail-text").TextContent);

        var fromCass = RenderWithProviders<SheetContent>(content => content
            .Add(p => p.Members, Demo.Members)
            .Add(p => p.Places, Demo.Places)
            .Add(p => p.Now, (DateTimeOffset?)DemoNow)
            .Add(p => p.Zone, (TimeZoneInfo?)Session.Zone)
            .Add(p => p.MeId, DemoCast.Jester.Id));
        Assert.Equal("Since 9:06 pm", fromCass.Find("[data-testid='row-member-jester'] .realm-row-detail-text").TextContent);
        Assert.Equal("Since 5:52 pm · 1.0 mi away", fromCass.Find("[data-testid='row-member-king'] .realm-row-detail-text").TextContent);
    }

    // ---- Vehicles -----------------------------------------------------------------------------------------------------------------------

    [Fact(DisplayName = "[AC-28a] Vehicles lists the pickup with its four lines and the chariot placeholder, aria-disabled, with DemoCast.ChariotNote")]
    public void Vehicles_ThePickupAndThePlaceholder()
    {
        var cut = Content(Section.Vehicles);

        var rows = cut.FindAll("[data-testid='sheet-list'] > li");
        Assert.Equal(["row-vehicle-wagon", "row-vehicle-chariot"], rows.Select(row => row.QuerySelector("[data-testid^='row-vehicle-']")!.GetAttribute("data-testid")));

        var wagon = cut.Find("[data-testid='row-vehicle-wagon']");
        Assert.Equal(DemoCast.Wagon.Name, wagon.QuerySelector(".realm-row-name")!.TextContent);
        Assert.Equal(DemoCast.Wagon.Lore, wagon.QuerySelector(".realm-row-lore")!.TextContent);
        Assert.Equal("At " + DemoPlaces.Home.Name, wagon.QuerySelector(".realm-row-status")!.TextContent);
        Assert.Equal(["Engine off", "Fuel 71%"], wagon.QuerySelectorAll(".realm-row-part").Select(part => part.TextContent.Trim()));
        Assert.Equal("Updated 20 min ago", wagon.QuerySelector(".realm-row-detail-text")!.TextContent);
        Assert.Null(wagon.GetAttribute("aria-disabled"));
        Assert.Single(wagon.QuerySelectorAll(".realm-row-chevron"));

        var chariot = cut.Find("[data-testid='row-vehicle-chariot']");
        Assert.Equal("true", chariot.GetAttribute("aria-disabled"));
        Assert.Equal(DemoCast.ChariotNote, chariot.QuerySelector(".realm-row-note")!.TextContent);
        Assert.Contains(DemoCast.ChariotNote, chariot.GetAttribute("aria-label"), StringComparison.Ordinal);
        Assert.DoesNotContain("Double tap", chariot.GetAttribute("aria-label"), StringComparison.Ordinal);
        Assert.Empty(chariot.QuerySelectorAll(".realm-row-chevron"));
        Assert.Empty(chariot.QuerySelectorAll(".realm-row-part"));
        Assert.Contains("realm-row--placeholder", chariot.ClassName, StringComparison.Ordinal);
    }

    [Fact]
    public void Vehicles_ThePickupGlyphIsTheTruckSilhouette_AndTheHatchbackIsTheCarIcon()
    {
        var cut = Content(Section.Vehicles);

        var truck = cut.Find("[data-testid='row-vehicle-wagon'] .realm-row-glyph").InnerHtml;
        var car = cut.Find("[data-testid='row-vehicle-chariot'] .realm-row-glyph").InnerHtml;
        Assert.NotEqual(truck, car);
        Assert.Equal(4, truck.Split("<path", StringSplitOptions.None).Length - 1);       // the truck is four shapes: body, window, two wheels
    }

    [Fact]
    public async Task Vehicles_TheInfoButtonOpensTheExplanation_AndTappingTheRowDoesNothing()
    {
        var selected = new List<EntityRef>();
        var cut = Content(Section.Vehicles, selected.Add);

        var info = cut.Find(".realm-row-info");
        Assert.Equal("false", info.GetAttribute("aria-expanded"));
        Assert.Empty(cut.FindAll(".realm-row-popover"));

        await info.TriggerEventAsync("onclick", new MouseEventArgs());
        Assert.Equal("true", cut.Find(".realm-row-info").GetAttribute("aria-expanded"));
        Assert.Equal(
            "This vehicle's maker has no official Home Assistant integration yet. When one exists, the Chariot will appear on the map.",
            cut.Find(".realm-row-popover").TextContent);
        Assert.Equal(cut.Find(".realm-row-popover").Id, cut.Find(".realm-row-info").GetAttribute("aria-controls"));

        await cut.Find(".realm-row-info").TriggerEventAsync("onkeydown", new KeyboardEventArgs { Key = "Escape" });
        Assert.Empty(cut.FindAll(".realm-row-popover"));

        await cut.Find(".realm-row-info").TriggerEventAsync("onclick", new MouseEventArgs());
        await cut.Find(".realm-row-popover").TriggerEventAsync("onclick", new MouseEventArgs());
        Assert.Empty(cut.FindAll(".realm-row-popover"));

        await cut.Find("[data-testid='row-vehicle-chariot']").TriggerEventAsync("onclick", new MouseEventArgs());
        Assert.Empty(selected);
    }

    [Fact]
    public void Vehicles_TheInfoButtonIsASiblingOfTheRow_NotInsideIt()
    {
        var cut = Content(Section.Vehicles);

        // A button cannot hold a button: the info control sits beside the aria-disabled row, in the same list item.
        var item = cut.FindAll("[data-testid='sheet-list'] > li")[1];
        Assert.Equal(2, item.QuerySelectorAll("button").Length);
        Assert.Empty(cut.Find("[data-testid='row-vehicle-chariot']").QuerySelectorAll("button"));
        Assert.Equal("About this vehicle", item.QuerySelector(".realm-row-info")!.GetAttribute("aria-label"));
    }

    // ---- Places -------------------------------------------------------------------------------------------------------------------------

    [Fact(DisplayName = "[AC-29] Places lists 14 rows: Hearth Haven and The Jester's Hall with 1 here, then the empty places A to Z; Work (2) and Rollerdome (2) exist, the arrival zone does not")]
    public void Places_FourteenRows_OccupiedFirst()
    {
        var cut = Content(Section.Places);

        var rows = cut.FindAll("[data-testid='sheet-list'] > li > button");
        Assert.Equal(14, rows.Count);
        Assert.Equal(["row-place-home", "row-place-jester_hall"], rows.Take(2).Select(row => row.GetAttribute("data-testid")));
        Assert.Equal([DemoPlaces.Home.Name, DemoPlaces.JesterHall.Name], rows.Take(2).Select(row => row.QuerySelector(".realm-row-name")!.TextContent));
        Assert.All(rows.Take(2), row => Assert.Equal("1 here", row.QuerySelector(".realm-row-count")!.TextContent));

        var empties = rows.Skip(2).ToList();
        Assert.All(empties, row => Assert.Equal("Empty", row.QuerySelector(".realm-row-count")!.TextContent));
        var names = empties.Select(row => row.QuerySelector(".realm-row-name")!.TextContent).ToList();
        Assert.Equal(names.OrderBy(name => name, StringComparer.OrdinalIgnoreCase), names);
        Assert.Contains(DemoPlaces.Work2.Name, names);
        Assert.Contains(DemoPlaces.SkateTwo.Name, names);
        Assert.Empty(cut.FindAll("[data-testid='row-place-approach']"));
    }

    [Fact]
    public void Places_TheSubtitlesAreTheDemoTable_AndAnOccupiedRowShowsItsPeople()
    {
        var cut = Content(Section.Places);

        foreach (var place in DemoPlaces.Drawn)
        {
            Assert.Equal(place.Subtitle, cut.Find($"[data-testid='row-place-{place.Id}'] .realm-row-subtitle").TextContent);
        }

        var home = cut.Find("[data-testid='row-place-home']");
        Assert.Single(home.QuerySelectorAll(".realm-mini"));
        Assert.Equal("A", home.QuerySelector(".realm-mini-initial")!.TextContent);
        Assert.Empty(cut.Find("[data-testid='row-place-vet']").QuerySelectorAll(".realm-mini"));
        Assert.Contains("realm-row-count--empty", cut.Find("[data-testid='row-place-vet'] .realm-row-count").ClassName, StringComparison.Ordinal);
        Assert.Equal(
            $"{DemoPlaces.Home.Name}, {DemoPlaces.Home.Subtitle}. 1 here: {DemoCast.King.Name}. Double tap to show on map.",
            home.GetAttribute("aria-label"));
    }

    // ---- the tap ------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task ATap_RaisesOnSelect_WithTheEntityOfTheRow()
    {
        var selected = new List<EntityRef>();

        var drivers = Content(Section.Drivers, selected.Add);
        await drivers.Find("[data-testid='row-member-jester']").TriggerEventAsync("onclick", new MouseEventArgs());

        var vehicles = Content(Section.Vehicles, selected.Add);
        await vehicles.Find("[data-testid='row-vehicle-wagon']").TriggerEventAsync("onclick", new MouseEventArgs());

        var places = Content(Section.Places, selected.Add);
        await places.Find("[data-testid='row-place-home']").TriggerEventAsync("onclick", new MouseEventArgs());

        Assert.Equal(
            [new EntityRef(EntityKind.Member, "jester"), new EntityRef(EntityKind.Vehicle, "wagon"), new EntityRef(EntityKind.Place, "home")],
            selected);
    }

    [Fact]
    public async Task ATap_WithNoOnSelectHandler_IsANoOp()
    {
        var cut = Content(Section.Drivers);

        await cut.Find("[data-testid='row-member-king']").TriggerEventAsync("onclick", new MouseEventArgs());

        Assert.NotEmpty(cut.FindAll("[data-testid='row-member-king']"));
    }

    // ---- both layouts, and the rest of the body ----------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(Section.Drivers)]
    [InlineData(Section.Vehicles)]
    [InlineData(Section.Places)]
    public void TheRows_AreIdenticalInTheBottomSheetAndInThePanel(Section section)
    {
        var sheet = Content(section);
        var panel = RenderWithProviders<SheetContent>(content => content
            .Add(p => p.Members, Demo.Members)
            .Add(p => p.Vehicles, Demo.Vehicles)
            .Add(p => p.Places, Demo.Places)
            .Add(p => p.Section, section)
            .Add(p => p.Now, (DateTimeOffset?)DemoNow)
            .Add(p => p.Zone, (TimeZoneInfo?)Session.Zone)
            .Add(p => p.Summary, "4 in the Realm · 1 driving")
            .Add(p => p.ShowSummary, true));

        // Compared as markup, not as strings: bUnit writes every event handler as blazor:onclick="<id>" and the renderer hands out new ids on each render, so the same rows
        // rendered twice differ in those ids alone. MarkupMatches leaves the blazor: attributes out and holds everything else (elements, classes, text) to equal.
        var sheetRows = sheet.FindAll("[data-testid='sheet-list'] > li");
        Assert.NotEmpty(sheetRows);
        Assert.Equal(sheetRows.Count, panel.FindAll("[data-testid='sheet-list'] > li").Count);
        sheet.Find("[data-testid='sheet-list']").MarkupMatches(panel.Find("[data-testid='sheet-list']").OuterHtml);
        Assert.Empty(sheet.FindAll("[data-testid='sheet-summary']"));
        Assert.Single(panel.FindAll("[data-testid='sheet-summary']"));
    }

    [Fact]
    public void TheCountsOfTheThreeSections_AreAnnounced()
    {
        Assert.Equal("5 drivers", Content(Section.Drivers).Find("[data-testid='sheet-announce']").TextContent);
        Assert.Equal("2 vehicles", Content(Section.Vehicles).Find("[data-testid='sheet-announce']").TextContent);
        Assert.Equal("14 places", Content(Section.Places).Find("[data-testid='sheet-announce']").TextContent);
    }

    // ---- helpers ---------------------------------------------------------------------------------------------------------------------------

    // The Demo lists at the Demo clock and zone, with the viewer left to the default (the first live member, Alden); onSelect, when given, receives every tap.
    private IRenderedComponent<SheetContent> Content(Section section, Action<EntityRef>? onSelect = null) =>
        RenderWithProviders<SheetContent>(content =>
        {
            content
                .Add(p => p.Members, Demo.Members)
                .Add(p => p.Vehicles, Demo.Vehicles)
                .Add(p => p.Places, Demo.Places)
                .Add(p => p.Section, section)
                .Add(p => p.Now, (DateTimeOffset?)DemoNow)
                .Add(p => p.Zone, (TimeZoneInfo?)Session.Zone);
            if (onSelect is not null)
            {
                content.Add(p => p.OnSelect, (EntityRef entity) =>
                {
                    onSelect(entity);
                    return Task.CompletedTask;
                });
            }
        });
}
