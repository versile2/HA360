using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Realm.Demo;
using Realm.Domain;
using Realm.Web.Components.Sheet;
using Realm.Web.Formatting;
using Realm.Web.Layout;
using Realm.Web.Sheet;
using Realm.Web.State;
using Xunit;

namespace Realm.Web.Tests;

/// <summary>
/// The body of the sheet (<see cref="SheetContent"/>, 01 sections 5.1 to 5.3): the rows of each section from the Demo cast, in the order of the spec, with the test ids of 01
/// Appendix B and the summary's counts; the entity a tap raises for S8; the same markup in the bottom sheet and in the panel; and the clock and zone, which
/// come from the parameters or the cascaded session and never from the browser. The row components themselves are covered by <see cref="MemberRowTests"/>. The body that
/// <see cref="SheetBody.BodyOf"/> derives (D45, 01 section 5.7) is rendered through the component at the end: the list, the selection header at Peek (no back button) and the detail at
/// Tall or in the panel, an entity that has left the lists, and the callbacks the header and the details raise.
/// </summary>
public sealed class SheetContentTests : ComponentTestBase
{
    private static readonly IRealmSession Session = FullCast.Session(null);

    private static readonly RealmSnapshot Demo = Session.Current;

    private static readonly DateTimeOffset DemoNow = Session.Time.GetUtcNow();

    // Alden is the viewer, as the page resolves "me" when nobody was asked for.
    private static readonly RowFacts Facts = RowFacts.Create(Demo.Members, Demo.Places, null, DemoNow, Session.Zone);

    private const string HeaderSel = "[data-testid='sheet-selection-header']";

    private const string ClearSel = "[data-testid='sheet-selection-clear']";

    private const string BackSel = "[data-testid='detail-back']";

    private const string SegmentsSel = "[data-testid='sheet-segments']";

    private const string ListSel = "[data-testid='sheet-list']";

    private static readonly EntityRef Jester = new(EntityKind.Member, DemoCast.Jester.Id);

    private static readonly EntityRef HomePlace = new(EntityKind.Place, DemoPlaces.Home.Id);

    // ---- Drivers ------------------------------------------------------------------------------------------------------------------------

    [Fact(DisplayName = "[AC-25] Drivers lists five rows in the order Alden, Briar, Cass, Dara, Elio, each a button with the test id of Appendix B")]
    public void Drivers_ListsTheFivePeopleInOrder_EachAButton()
    {
        var cut = Content(Section.Drivers);

        var rows = cut.FindAll("[data-testid='sheet-list'] > li > button");
        Assert.Equal(DemoCast.AllMembers.Select(member => $"row-member-{member.Id}"), rows.Select(row => row.GetAttribute("data-testid")));
        Assert.Equal(DemoCast.AllMembers.Select(member => member.Name), rows.Select(row => row.QuerySelector(".realm-row-name")!.TextContent));
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

    [Fact(DisplayName = "[AC-28a] Vehicles lists the pickup with its three lines and the hatchback, both rows tappable")]
    public void Vehicles_ThePickupAndTheHatchback()
    {
        var cut = Content(Section.Vehicles);

        var rows = cut.FindAll("[data-testid='sheet-list'] > li");
        Assert.Equal(["row-vehicle-wagon", "row-vehicle-chariot"], rows.Select(row => row.QuerySelector("[data-testid^='row-vehicle-']")!.GetAttribute("data-testid")));

        var wagon = cut.Find("[data-testid='row-vehicle-wagon']");
        Assert.Equal(DemoCast.Wagon.Name, wagon.QuerySelector(".realm-row-name")!.TextContent);
        Assert.Equal(DemoCast.Wagon.Lore, wagon.QuerySelector(".realm-row-lore")!.TextContent);
        Assert.Equal("At " + DemoPlaces.Home.Name, wagon.QuerySelector(".realm-row-status")!.TextContent);
        Assert.Empty(wagon.QuerySelectorAll(".realm-row-part"));   // no engine, no fuel: a vehicle is a device tracker (D107)
        Assert.Equal("Updated 20 min ago", wagon.QuerySelector(".realm-row-detail-text")!.TextContent);
        Assert.Null(wagon.GetAttribute("aria-disabled"));
        Assert.Single(wagon.QuerySelectorAll(".realm-row-chevron"));

        var chariot = cut.Find("[data-testid='row-vehicle-chariot']");
        Assert.Null(chariot.GetAttribute("aria-disabled"));
        Assert.Contains("Double tap", chariot.GetAttribute("aria-label"), StringComparison.Ordinal);
        Assert.Single(chariot.QuerySelectorAll(".realm-row-chevron"));
        Assert.DoesNotContain("realm-row--placeholder", chariot.ClassName, StringComparison.Ordinal);
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
    public async Task Vehicles_TappingARow_SelectsTheVehicle()
    {
        var selected = new List<EntityRef>();
        var cut = Content(Section.Vehicles, selected.Add);

        await cut.Find("[data-testid='row-vehicle-chariot']").TriggerEventAsync("onclick", new MouseEventArgs());

        Assert.Equal([new EntityRef(EntityKind.Vehicle, DemoCast.Chariot.Id)], selected);
        Assert.Empty(cut.FindAll(".realm-row-info"));   // the maker-has-no-integration button is gone with the placeholder
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

    private const string LiveSel = "[data-testid='sheet-announce']";

    [Fact]
    public void TheLiveRegion_StartsEmpty_AndSaysWhateverThePageHandsIt()
    {
        var quiet = Content(Section.Drivers);
        Assert.Equal(string.Empty, quiet.Find(LiveSel).TextContent);

        var cut = Selected(null, SheetSize.Peek, LayoutMode.Compact, content => content.Add(p => p.Announcement, "Showing Cass"));
        Assert.Equal("Showing Cass", cut.Find(LiveSel).TextContent);

        cut.Render(content => content.Add(p => p.Announcement, "List opened"));
        Assert.Equal("List opened", cut.Find(LiveSel).TextContent);
    }

    [Theory]
    [InlineData(false, SheetSize.Peek, LayoutMode.Compact)]
    [InlineData(false, SheetSize.Tall, LayoutMode.Compact)]
    [InlineData(true, SheetSize.Peek, LayoutMode.Compact)]   // the selection header takes the tabs' place; the region stays
    [InlineData(true, SheetSize.Tall, LayoutMode.Compact)]   // the detail
    [InlineData(true, SheetSize.Peek, LayoutMode.Expanded)]  // the panel
    public void TheLiveRegion_IsTheOnlyOne_Polite_AndStaysWhateverTheBodyIs(bool selected, SheetSize size, LayoutMode mode)
    {
        var cut = Selected(selected ? Jester : null, size, mode, content => content.Add(p => p.Announcement, "Details opened"));

        var live = Assert.Single(cut.FindAll("[aria-live]"));
        Assert.Equal("sheet-announce", live.GetAttribute("data-testid"));
        Assert.Equal("polite", live.GetAttribute("aria-live"));
        Assert.Equal("status", live.GetAttribute("role"));
        Assert.Equal("true", live.GetAttribute("aria-atomic"));
        Assert.Equal("Details opened", live.TextContent);
    }

    [Fact]
    public void ASectionTabRequest_FromThePage_ReachesTheSegments()
    {
        var list = Selected(null, SheetSize.Peek, LayoutMode.Compact, content => content.Add(p => p.Focus, new FocusRequest(FocusTarget.SectionTab)));
        list.WaitForAssertion(() => JSInterop.VerifyFocusAsyncInvoke());
    }

    [Fact]
    public void TheSegmentsAreNotRendered_WhileTheHeaderTakesTheirPlace_SoARequestForTheTabWaitsForThem()
    {
        var header = Selected(Jester, SheetSize.Peek, LayoutMode.Compact, content => content.Add(p => p.Focus, new FocusRequest(FocusTarget.SectionTab)));

        Assert.Empty(header.FindAll(SegmentsSel));
        AssertNothingFocused();
    }

    // ---- the body: list, header, detail (D45, D46, 01 section 5.7) ---------------------------------------------------------------------------

    [Theory]
    [InlineData(SheetSize.Peek, LayoutMode.Compact)]
    [InlineData(SheetSize.Tall, LayoutMode.Compact)]
    [InlineData(SheetSize.Peek, LayoutMode.Expanded)]
    public void WithNoSelection_TheBodyIsTheList_AtEitherSizeAndInEitherLayout(SheetSize size, LayoutMode mode)
    {
        var cut = Selected(null, size, mode);

        Assert.NotEmpty(cut.FindAll(SegmentsSel));
        Assert.NotEmpty(cut.FindAll(ListSel));
        Assert.Empty(cut.FindAll(HeaderSel));
        Assert.Empty(cut.FindAll(BackSel));
        Assert.Empty(cut.FindAll(".realm-detail"));
    }

    [Fact]
    public void WithNoBodyGiven_TheComponentShowsTheList()
    {
        var cut = Content(Section.Drivers);

        Assert.NotEmpty(cut.FindAll(ListSel));
        Assert.Empty(cut.FindAll(HeaderSel));
    }

    [Theory(DisplayName = "[AC-22f] A selection at Peek shows the header in place of the segments, with nothing below it and no back button")]
    [InlineData(EntityKind.Member, "jester")]
    [InlineData(EntityKind.Vehicle, "wagon")]
    [InlineData(EntityKind.Place, "home")]
    public void ASelectionAtPeek_ShowsTheHeader_InPlaceOfTheSegments_AndNoBackButton(EntityKind kind, string id)
    {
        var entity = new EntityRef(kind, id);
        var cut = Selected(entity, SheetSize.Peek, LayoutMode.Compact);

        var expected = SelectionHeaderFormatter.For(entity, Demo.Members, Demo.Vehicles, Demo.Places, Facts);
        Assert.NotNull(expected);
        Assert.Equal(expected.AccessibleName, cut.Find(HeaderSel).GetAttribute("aria-label"));
        Assert.Empty(cut.FindAll(SegmentsSel));
        Assert.Empty(cut.FindAll(ListSel));
        Assert.Empty(cut.FindAll("[role='tabpanel']"));
        Assert.Empty(cut.FindAll(".realm-detail"));
        Assert.Empty(cut.FindAll(BackSel));
    }

    [Theory(DisplayName = "[AC-30d] A selection at Tall shows the segments and the detail with its back button, and neither the list nor the header")]
    [InlineData(EntityKind.Member, "jester", "realm-detail--member")]
    [InlineData(EntityKind.Vehicle, "wagon", "realm-detail--vehicle")]
    [InlineData(EntityKind.Place, "home", "realm-detail--place")]
    public void ASelectionAtTall_ShowsTheSegmentsAndTheDetail_AndNotTheListOrTheHeader(EntityKind kind, string id, string detailClass)
    {
        var cut = Selected(new EntityRef(kind, id), SheetSize.Tall, LayoutMode.Compact);

        Assert.NotEmpty(cut.FindAll(SegmentsSel));
        Assert.Empty(cut.FindAll(HeaderSel));
        Assert.Empty(cut.FindAll(ListSel));
        var panel = cut.Find("#" + SheetSegments.PanelId);
        Assert.Equal("tabpanel", panel.GetAttribute("role"));
        Assert.Equal(SheetSegments.TabId(Section.Drivers), panel.GetAttribute("aria-labelledby"));
        Assert.Single(panel.QuerySelectorAll("section." + detailClass));
        Assert.Single(cut.FindAll(BackSel));
    }

    [Theory]
    [InlineData(EntityKind.Member, "jester", "realm-detail--member")]
    [InlineData(EntityKind.Vehicle, "wagon", "realm-detail--vehicle")]
    [InlineData(EntityKind.Place, "home", "realm-detail--place")]
    public void InThePanel_ASelectionIsAlwaysTheDetail_EvenAtPeek(EntityKind kind, string id, string detailClass)
    {
        var cut = Selected(
            new EntityRef(kind, id),
            SheetSize.Peek,
            LayoutMode.Expanded,
            content => content
                .Add(p => p.Summary, "4 in the Realm · 1 driving")
                .Add(p => p.ShowSummary, true));

        Assert.Single(cut.FindAll("[data-testid='sheet-summary']"));
        Assert.NotEmpty(cut.FindAll(SegmentsSel));
        Assert.Empty(cut.FindAll(HeaderSel));
        Assert.Single(cut.FindAll("section." + detailClass));
        Assert.Single(cut.FindAll(BackSel));
        Assert.Empty(cut.FindAll(ListSel));
    }

    [Fact]
    public void TheDetail_NamesTheEntityInItsHeading_AndIsLabelledByIt()
    {
        var cut = Selected(Jester, SheetSize.Tall, LayoutMode.Compact);

        Assert.Equal(DemoCast.Jester.Name, cut.Find("h2#realm-detail-title").TextContent);
        Assert.Equal("realm-detail-title", cut.Find("section.realm-detail").GetAttribute("aria-labelledby"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AnEntityThatHasLeftTheLists_HasNoHeaderAndNoDetail_AndTheListShows(bool detail)
    {
        var gone = new EntityRef(EntityKind.Member, "nobody");
        SheetBody body = detail ? new SheetBody.Detail(gone) : new SheetBody.Header(gone);

        var cut = RenderWithProviders<SheetContent>(content => content
            .Add(p => p.Members, Demo.Members)
            .Add(p => p.Vehicles, Demo.Vehicles)
            .Add(p => p.Places, Demo.Places)
            .Add(p => p.Body, body)
            .Add(p => p.Now, (DateTimeOffset?)DemoNow)
            .Add(p => p.Zone, (TimeZoneInfo?)Session.Zone));

        Assert.NotEmpty(cut.FindAll(SegmentsSel));
        Assert.NotEmpty(cut.FindAll(ListSel));
        Assert.Empty(cut.FindAll(HeaderSel));
        Assert.Empty(cut.FindAll(BackSel));
    }

    [Fact]
    public void AnIdOfAnotherKind_IsNotTheEntity_AndTheListShows()
    {
        // A member's id under the kind Place resolves to nothing: a selection is by kind and id (02 section 1).
        var cut = Selected(new EntityRef(EntityKind.Place, DemoCast.Jester.Id), SheetSize.Peek, LayoutMode.Compact);

        Assert.Empty(cut.FindAll(HeaderSel));
        Assert.NotEmpty(cut.FindAll(ListSel));
    }

    [Fact(DisplayName = "[AC-23c] The header's body raises OnToggle once, and its X raises OnClear once and OnToggle never")]
    public async Task TheHeader_RaisesOnToggleForABodyTap_AndOnClearForTheX()
    {
        var toggles = 0;
        var clears = 0;
        var cut = Selected(
            Jester,
            SheetSize.Peek,
            LayoutMode.Compact,
            content => content
                .Add(p => p.OnToggle, () =>
                {
                    toggles++;
                    return Task.CompletedTask;
                })
                .Add(p => p.OnClear, () =>
                {
                    clears++;
                    return Task.CompletedTask;
                }));

        await cut.Find(".realm-sel-body").TriggerEventAsync("onclick", new MouseEventArgs());
        Assert.Equal(1, toggles);
        Assert.Equal(0, clears);

        await cut.Find(ClearSel).TriggerEventAsync("onclick", new MouseEventArgs());
        Assert.Equal(1, toggles);
        Assert.Equal(1, clears);
    }

    [Fact]
    public async Task TheDetailsBackArrow_RaisesOnBack_Once()
    {
        var backs = 0;
        var cut = Selected(
            Jester,
            SheetSize.Tall,
            LayoutMode.Compact,
            content => content.Add(p => p.OnBack, () =>
            {
                backs++;
                return Task.CompletedTask;
            }));

        await cut.Find(BackSel).TriggerEventAsync("onclick", new MouseEventArgs());

        Assert.Equal(1, backs);
    }

    [Fact(DisplayName = "[AC-31b] A person in the place detail's Here-now list raises OnHereNow with their id, and the row tap callback is not used")]
    public async Task APersonInThePlaceDetail_RaisesOnHereNow_WithTheirId()
    {
        var heard = new List<string>();
        var selected = new List<EntityRef>();
        var cut = Selected(
            HomePlace,
            SheetSize.Tall,
            LayoutMode.Compact,
            content => content
                .Add(p => p.OnHereNow, (string id) =>
                {
                    heard.Add(id);
                    return Task.CompletedTask;
                })
                .Add(p => p.OnSelect, (EntityRef entity) =>
                {
                    selected.Add(entity);
                    return Task.CompletedTask;
                }));

        await cut.Find("[data-testid='row-member-king']").TriggerEventAsync("onclick", new MouseEventArgs());

        Assert.Equal([DemoCast.King.Id], heard);
        Assert.Empty(selected);
    }

    [Fact]
    public void WhenAskedToFocus_ADetailThatJustOpenedTakesTheFocusOnItsBackButton()
    {
        var cut = Selected(Jester, SheetSize.Tall, LayoutMode.Compact, content => content.Add(p => p.FocusDetail, true));

        cut.WaitForAssertion(() => JSInterop.VerifyFocusAsyncInvoke());
        Assert.Single(cut.FindAll(BackSel));
    }

    [Fact]
    public void WithoutTheAsk_NoDetailTakesTheFocus_AndNeitherDoesTheHeader()
    {
        var detail = Selected(Jester, SheetSize.Tall, LayoutMode.Compact);
        Selected(Jester, SheetSize.Peek, LayoutMode.Compact, content => content.Add(p => p.FocusDetail, true));

        Assert.NotEmpty(detail.FindAll(BackSel));
        AssertNothingFocused();
    }

    [Fact]
    public void TheDetailsRows_AreTheSameStrings_AsTheListRowsOfTheSameEntity()
    {
        // The detail and its row come from one source (VmFactory): the vehicle's update line is the row's.
        var list = Content(Section.Vehicles);
        var detail = Selected(new EntityRef(EntityKind.Vehicle, DemoCast.Wagon.Id), SheetSize.Tall, LayoutMode.Compact);

        Assert.Empty(detail.FindAll("section.realm-detail--vehicle .realm-detail-updated"));
        var relative = list.Find("[data-testid='row-vehicle-wagon'] .realm-row-detail-text").TextContent.Replace("Updated ", string.Empty, StringComparison.Ordinal);
        Assert.Contains(relative, detail.Find("section.realm-detail--vehicle .realm-detail-rows").TextContent, StringComparison.Ordinal);
    }

    // ---- helpers ---------------------------------------------------------------------------------------------------------------------------

    // bUnit raises when the focus call it verifies was never made.
    private void AssertNothingFocused() => Assert.ThrowsAny<Exception>(() => JSInterop.VerifyFocusAsyncInvoke());

    // The Demo lists at the Demo clock and zone with the body that BodyOf derives from (selection, size, layout); more, when given, adds parameters.
    private IRenderedComponent<SheetContent> Selected(
        EntityRef? selection,
        SheetSize size,
        LayoutMode mode,
        Action<ComponentParameterCollectionBuilder<SheetContent>>? more = null) =>
        RenderWithProviders<SheetContent>(content =>
        {
            content
                .Add(p => p.Members, Demo.Members)
                .Add(p => p.Vehicles, Demo.Vehicles)
                .Add(p => p.Places, Demo.Places)
                .Add(p => p.Body, SheetBody.BodyOf(new SheetState(Section.Drivers, selection, size), mode))
                .Add(p => p.Now, (DateTimeOffset?)DemoNow)
                .Add(p => p.Zone, (TimeZoneInfo?)Session.Zone);
            more?.Invoke(content);
        });

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
