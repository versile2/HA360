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
/// The three detail views of 01 sections 5.4 to 5.6 (<see cref="MemberDetail"/>, <see cref="VehicleDetail"/>, <see cref="PlaceDetail"/>), rendered from the Demo cast: Cass (header, status block,
/// the low battery chip, the address, the week's tiles and the link to his report), the stale Dara, a member with no fix, the static Elio (no week, a distance row and the sentence), the accuracy
/// chip, a week given by the parent, a week that cannot be read, a late answer and one that arrives after the detail has closed; the pickup (rows and their order, speed only while moving, red
/// fuel, the stale warning, a dash for what is unknown), the hatchback placeholder; the place's Here-now list, the empty place and the person row that selects. Every string a test compares is read from
/// <see cref="DemoCast"/>, <see cref="DemoPlaces"/> or the Demo snapshot, or is one of the spec's own examples that the formatter's tests pin.
/// </summary>
public sealed class DetailComponentTests : ComponentTestBase
{
    private const string Dash = "—";

    private const string BackSel = "[data-testid='detail-back']";

    private static readonly IRealmSession Session = new DemoRealmSessionFactory().Create(null);

    private static readonly RealmSnapshot Demo = Session.Current;

    private static readonly DateTimeOffset DemoNow = Session.Time.GetUtcNow();

    // Alden is the viewer, as the page resolves "me" when nobody was asked for.
    private static readonly RowFacts Facts = RowFacts.Create(Demo.Members, Demo.Places, null, DemoNow, Session.Zone);

    private static MemberVm MemberOf(string id) => Demo.Members.Single(member => member.Id == id);

    private static VehicleVm VehicleOf(string id) => Demo.Vehicles.Single(vehicle => vehicle.Id == id);

    private static PlaceVm PlaceOf(string id) => Demo.Places.Single(place => place.Id == id);

    private static MemberVm Cass() => MemberOf(DemoCast.Jester.Id);

    private static IReadOnlyList<string> TileValues(IRenderedComponent<MemberDetail> cut) =>
        [.. cut.FindAll(".realm-detail-tile-value").Select(value => value.TextContent)];

    private static IReadOnlyList<string> TileLabels(IRenderedComponent<MemberDetail> cut) =>
        [.. cut.FindAll(".realm-detail-tile-label").Select(label => label.TextContent)];

    // ---- a person: Cass ---------------------------------------------------------------------------------------------------------------------

    [Fact(DisplayName = "[AC-30e] Cass's detail: name, lore, Updated 3 min ago, the status block, the 12% low battery chip, the address, and the week's three tiles with the link to his report")]
    public void Cass_ShowsTheHeader_TheStatusBlock_TheChip_TheAddress_AndTheWeek()
    {
        var cass = Cass();
        var cut = RenderMember(cass, Session);

        Assert.Equal(DemoCast.Jester.Name, cut.Find("h2#realm-detail-title").TextContent);
        Assert.Equal("realm-detail-title", cut.Find("section.realm-detail--member").GetAttribute("aria-labelledby"));
        Assert.Equal(DemoCast.Jester.Lore, cut.Find(".realm-detail-heading .realm-detail-eyebrow").TextContent);
        Assert.Equal("Updated 3 min ago", cut.Find(".realm-detail-heading .realm-detail-updated").TextContent);
        Assert.Single(cut.FindAll(".realm-detail-dot--fresh"));
        Assert.Equal("C", cut.Find(".realm-detail-avatar .realm-row-initial").TextContent);
        Assert.Equal("--realm-row-color:" + DemoCast.Jester.Color, cut.Find(".realm-detail-avatar").GetAttribute("style"));

        Assert.Equal("At " + DemoPlaces.JesterHall.Name, cut.Find(".realm-detail-status-line").TextContent);
        Assert.Equal("Since 9:06 pm · 1.0 mi away", cut.Find(".realm-detail-status .realm-detail-line").TextContent);

        var chip = Assert.Single(cut.FindAll(".realm-detail-chip"));
        Assert.Equal("12% · Low battery", chip.TextContent.Trim());
        Assert.Equal("img", chip.GetAttribute("role"));
        Assert.Equal(VmFactory.Member(cass, Facts).Battery!.AccessibleName, chip.GetAttribute("aria-label"));
        Assert.Contains("realm-detail-chip--low", chip.ClassList);
        Assert.Equal("true", chip.GetAttribute("data-low"));

        Assert.False(string.IsNullOrWhiteSpace(cass.FullAddress));
        Assert.Equal(cass.FullAddress, cut.Find(".realm-detail-address").TextContent);

        cut.WaitForAssertion(() => Assert.Equal(["18", "202.6", "88 mph"], TileValues(cut)));
        Assert.Equal(["drives", "mi", "top"], TileLabels(cut));
        Assert.Equal(SelectionHeaderFormatter.ThisWeek, cut.Find("h3.realm-detail-week-title").TextContent);
    }

    [Fact]
    public void Cass_TheLinkToTheFullReport_IsARelativeAnchorToHisWeek()
    {
        var cut = RenderMember(Cass(), Session);

        var link = cut.Find("a.realm-detail-link");
        Assert.Equal(DrivingFormatter.DriverHref(DemoCast.Jester.Id, 0), link.GetAttribute("href"));
        Assert.StartsWith("driving/", link.GetAttribute("href"), StringComparison.Ordinal);
        Assert.StartsWith(SelectionHeaderFormatter.FullReport, link.TextContent, StringComparison.Ordinal);
        Assert.Equal("true", link.QuerySelector("span")!.GetAttribute("aria-hidden"));
    }

    [Fact]
    public void Cass_HasNoTrailButton_AndNoTimeline_TheBackArrowIsTheOnlyButton()
    {
        var cut = RenderMember(Cass(), Session);

        var back = Assert.Single(cut.FindAll("button"));
        Assert.Equal("detail-back", back.GetAttribute("data-testid"));
        Assert.Equal(SelectionHeaderFormatter.BackLabel, back.GetAttribute("aria-label"));
        Assert.Equal("button", back.GetAttribute("type"));
    }

    [Fact]
    public async Task ThePersonsBackArrow_RaisesOnBack_Once()
    {
        var backs = 0;
        var cut = RenderMember(Cass(), more: detail => detail.Add(p => p.OnBack, () =>
        {
            backs++;
            return Task.CompletedTask;
        }));

        await cut.Find(BackSel).TriggerEventAsync("onclick", new MouseEventArgs());

        Assert.Equal(1, backs);
    }

    [Fact]
    public void AHostileName_IsRenderedAsText_NeverAsMarkup()
    {
        const string hostile = "<img src=x onerror=alert(1)>";

        var cut = RenderMember(Cass() with { DisplayName = hostile, LoreTitle = hostile });

        Assert.Empty(cut.FindAll("img"));
        Assert.Equal(hostile, cut.Find("h2#realm-detail-title").TextContent);
        Assert.Equal(hostile, cut.Find(".realm-detail-heading .realm-detail-eyebrow").TextContent);
    }

    // ---- a person: the other states ----------------------------------------------------------------------------------------------------------

    [Fact(DisplayName = "[AC-30f] Dara is stale: the warning dot and line, the warning in the header, and the battery's accessible name says as of when")]
    public void Dara_IsStale_TheDotAndTheLineAreTheWarning_AndTheChipNamesTheAge()
    {
        var dara = MemberOf(DemoCast.Cryptid.Id);
        var row = VmFactory.Member(dara, Facts);
        var cut = RenderMember(dara);

        Assert.Single(cut.FindAll(".realm-detail-dot--stale"));
        Assert.Empty(cut.FindAll(".realm-detail-dot--fresh"));
        var updated = cut.Find(".realm-detail-heading .realm-detail-updated");
        Assert.Equal("The raven's late — last seen 42 min ago", updated.TextContent);
        Assert.Contains("realm-row-detail--warning", updated.ClassList);
        Assert.Equal(row.StatusLine, cut.Find(".realm-detail-status-line").TextContent);

        var chip = Assert.Single(cut.FindAll(".realm-detail-chip"));
        Assert.Equal(row.Battery!.AccessibleName, chip.GetAttribute("aria-label"));
        Assert.Contains("battery as of 42 min ago", chip.GetAttribute("aria-label"), StringComparison.Ordinal);
    }

    [Fact]
    public void AnOfflineMember_HasTheGreyDot_AndTheGoneDarkLine()
    {
        var offline = MemberOf(DemoCast.Cryptid.Id) with { Freshness = Freshness.Offline, LastUpdateUtc = DemoNow - TimeSpan.FromHours(30) };

        var cut = RenderMember(offline);

        Assert.Single(cut.FindAll(".realm-detail-dot--offline"));
        Assert.StartsWith("Gone dark — last seen ", cut.Find(".realm-detail-heading .realm-detail-updated").TextContent, StringComparison.Ordinal);
        Assert.Contains("realm-row-detail--stale", cut.Find(".realm-detail-heading .realm-detail-updated").ClassList);
    }

    [Fact(DisplayName = "[AC-30g] A member with no fix has no dot and no Updated line; the status reads Location unavailable and The scouts have not reported")]
    public void ANoFixMember_HasNoDot_AndNoUpdatedLine_AndTheScoutsLines()
    {
        var noFix = Cass() with { Freshness = Freshness.NoFix, Lat = null, Lon = null, PlaceId = null };

        var cut = RenderMember(noFix);

        Assert.Empty(cut.FindAll(".realm-detail-dot"));
        Assert.Empty(cut.FindAll(".realm-detail-heading .realm-detail-updated"));
        Assert.Equal(MemberTextFormatter.LocationUnavailable, cut.Find(".realm-detail-status-line").TextContent);
        Assert.Equal(MemberTextFormatter.ScoutsHaveNotReported, cut.Find(".realm-detail-status .realm-detail-line").TextContent);
    }

    [Fact]
    public void AMemberWithNoBatteryReading_HasNoChip_AndNoChipRow()
    {
        var cut = RenderMember(Cass() with { BatteryPct = null });

        Assert.Empty(cut.FindAll(".realm-detail-chip"));
        Assert.Empty(cut.FindAll(".realm-detail-chips"));
    }

    [Fact]
    public void APoorFix_AddsTheApproximateChip_AfterTheBatteryChip()
    {
        var poor = Cass() with { AccuracyM = Facts.Options.PoorAccuracyMeters + 305 };
        var expected = SelectionHeaderFormatter.AccuracyChip(poor, VmFactory.Member(poor, Facts).Status, Facts);

        var cut = RenderMember(poor);

        var chips = cut.FindAll(".realm-detail-chip");
        Assert.Equal(2, chips.Count);
        Assert.Equal("Approximate · ± 0.5 mi", expected);
        Assert.Equal(expected, chips[1].TextContent.Trim());
        Assert.Equal("12% · Low battery", chips[0].TextContent.Trim());
    }

    [Fact]
    public void AGoodFix_HasNoApproximateChip()
    {
        var cut = RenderMember(Cass() with { AccuracyM = Facts.Options.PoorAccuracyMeters });

        Assert.Single(cut.FindAll(".realm-detail-chip"));
    }

    [Fact]
    public void WithNoFullAddress_NoAddressLineShows()
    {
        var cut = RenderMember(Cass() with { FullAddress = null });

        Assert.Empty(cut.FindAll(".realm-detail-address"));
    }

    // ---- the static prince -----------------------------------------------------------------------------------------------------------------

    [Fact(DisplayName = "[AC-30h] The static Elio has the header and the status only: his label, Location isn't shared, a distance row and the sentence; no battery, no week, no dot")]
    public async Task Elio_HasTheHeaderAndTheStatusOnly_WithTheDistanceAndTheSentence()
    {
        var elio = MemberOf(DemoCast.Prince.Id);
        await using var counted = new StubSession(Session);
        var cut = RenderMember(elio, counted);

        Assert.Equal(DemoCast.Prince.Name, cut.Find("h2#realm-detail-title").TextContent);
        Assert.Equal(DemoCast.Prince.Lore, cut.Find(".realm-detail-heading .realm-detail-eyebrow").TextContent);
        Assert.Empty(cut.FindAll(".realm-detail-heading .realm-detail-updated"));
        Assert.Empty(cut.FindAll(".realm-detail-dot"));

        Assert.Equal(DemoCast.Prince.StaticLabel, cut.Find(".realm-detail-status-line").TextContent);
        var distance = SelectionHeaderFormatter.StaticDistance(elio, Facts);
        Assert.NotNull(distance);
        Assert.EndsWith(" mi away", distance, StringComparison.Ordinal);
        Assert.Equal(
            [MemberTextFormatter.LocationNotShared, distance],
            cut.FindAll(".realm-detail-status .realm-detail-line").Select(line => line.TextContent));
        Assert.Equal($"The {DemoCast.Prince.Lore} keeps his own counsel.", cut.Find(".realm-detail-sentence").TextContent);

        Assert.Empty(cut.FindAll(".realm-detail-chip"));
        Assert.Empty(cut.FindAll(".realm-detail-week"));
        Assert.Empty(cut.FindAll(".realm-detail-tile"));
        Assert.Empty(cut.FindAll("a"));
        Assert.Empty(cut.FindAll(".realm-detail-address"));
        Assert.Equal(0, counted.DriverWeekCalls);
        Assert.Equal(0, counted.ReportCalls);
    }

    [Fact]
    public void Elio_ShowsAStreetAddress_OnlyWhenTheDataLayerSentOne()
    {
        var elio = MemberOf(DemoCast.Prince.Id);
        Assert.Null(elio.FullAddress);

        var shown = RenderMember(elio with { FullAddress = DemoCast.Jester.Address });

        Assert.Equal(DemoCast.Jester.Address, shown.Find(".realm-detail-address").TextContent);
    }

    // ---- the week -----------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task AWeekGivenByTheParent_IsShownAsItIs_AndNothingIsLoaded()
    {
        var week = DrivingFormatterTests.Driver(drives: 3, miles: 12.3, speeding: 0);
        await using var counted = new StubSession(Session);

        var cut = RenderMember(
            Cass(),
            counted,
            detail => detail.Add(p => p.Week, week).Add(p => p.TopSpeedMps, (double?)27.7));

        var expected = SelectionHeaderFormatter.WeekTiles(week, 27.7);
        Assert.Equal(expected.Select(tile => tile.Value), TileValues(cut));
        Assert.Equal(expected.Select(tile => tile.Label), TileLabels(cut));
        Assert.Equal(["3", "12.3", "62 mph"], TileValues(cut));
        Assert.Equal(0, counted.DriverWeekCalls);
        Assert.Equal(0, counted.ReportCalls);
    }

    [Fact]
    public void AWeekThatWasNotCovered_ReadsDashes_NotZeros()
    {
        var week = DrivingFormatterTests.Driver(covered: false, drives: null, miles: null, speeding: null, eventsTotal: null);

        var cut = RenderMember(Cass(), more: detail => detail.Add(p => p.Week, week).Add(p => p.TopSpeedMps, (double?)42.9));

        Assert.Equal([Dash, Dash, Dash], TileValues(cut));
        Assert.Equal(["drives", "mi", "top"], TileLabels(cut));
    }

    [Fact]
    public void WithNoSession_TheTilesReadDashes()
    {
        var cut = RenderMember(Cass());

        Assert.Equal([Dash, Dash, Dash], TileValues(cut));
        Assert.Equal(DemoCast.Jester.Name, cut.Find("h2#realm-detail-title").TextContent);
    }

    [Fact]
    public async Task WhenTheWeekCannotBeRead_TheTilesStayDashes_AndTheRestOfTheDetailIsUnaffected()
    {
        await using var failing = new StubSession(Session)
        {
            OnDriverWeek = (_, _, _, _) => throw new InvalidOperationException("The week is gone."),
        };

        var cut = RenderMember(Cass(), failing);

        Assert.Equal(1, failing.DriverWeekCalls);
        Assert.Equal([Dash, Dash, Dash], TileValues(cut));
        Assert.Equal(DemoCast.Jester.Name, cut.Find("h2#realm-detail-title").TextContent);
        Assert.Equal("Since 9:06 pm · 1.0 mi away", cut.Find(".realm-detail-status .realm-detail-line").TextContent);
        Assert.Single(cut.FindAll("a.realm-detail-link"));
    }

    [Fact]
    public async Task WhenOnlyTheReportCannotBeRead_TheTopSpeedIsADash_AndTheOtherTilesLoad()
    {
        await using var noReport = new StubSession(Session)
        {
            OnReport = (_, _, _) => throw new InvalidOperationException("The report is gone."),
        };

        var cut = RenderMember(Cass(), noReport);

        cut.WaitForAssertion(() => Assert.Equal(["18", "202.6", Dash], TileValues(cut)));
        Assert.Equal(1, noReport.ReportCalls);
    }

    [Fact]
    public async Task TheWeekThatArrivesLater_FillsTheTiles_AfterTheyReadDashes()
    {
        var gate = new TaskCompletionSource<DriverWeek?>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var gated = new StubSession(Session) { OnDriverWeek = (_, _, _, _) => new ValueTask<DriverWeek?>(gate.Task) };

        var cut = RenderMember(Cass(), gated);
        Assert.Equal([Dash, Dash, Dash], TileValues(cut));

        gate.SetResult(await Session.GetDriverWeekAsync(DemoCast.Jester.Id, 0, Demo.WeekStart, CancellationToken.None));

        cut.WaitForAssertion(() => Assert.Equal(["18", "202.6", "88 mph"], TileValues(cut)));
    }

    [Fact]
    public async Task AnAnswerThatArrivesAfterTheDetailClosed_IsDropped_TheLoadWasCancelledWhenItClosed()
    {
        var gate = new TaskCompletionSource<DriverWeek?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var seen = CancellationToken.None;
        await using var gated = new StubSession(Session)
        {
            OnDriverWeek = (_, _, _, token) =>
            {
                seen = token;
                return new ValueTask<DriverWeek?>(gate.Task);
            },
        };
        RenderMember(Cass(), gated);
        Assert.True(seen.CanBeCanceled);
        Assert.False(seen.IsCancellationRequested);

        await DisposeComponentsAsync();
        Assert.True(seen.IsCancellationRequested);

        gate.SetResult(await Session.GetDriverWeekAsync(DemoCast.Jester.Id, 0, Demo.WeekStart, CancellationToken.None));
        await Task.Yield();

        Assert.Equal(0, gated.ReportCalls);
    }

    // ---- focus ---------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Member_FocusOnOpen_MovesTheFocusToTheBackButton()
    {
        var cut = RenderMember(Cass(), more: detail => detail.Add(p => p.FocusOnOpen, true));

        cut.WaitForAssertion(() => JSInterop.VerifyFocusAsyncInvoke());
    }

    [Fact]
    public void Member_WithoutFocusOnOpen_NothingTakesTheFocus()
    {
        RenderMember(Cass());

        AssertNothingFocused();
    }

    // ---- a vehicle ---------------------------------------------------------------------------------------------------------------------------

    [Fact(DisplayName = "[AC-28d] The pickup's detail: Ford Pickup, THE KING'S WAGON, Updated 20 min ago, and the rows Location, Engine, Fuel, Odometer, Last update (no Speed while parked)")]
    public void TheWagon_ShowsItsHeader_AndItsRowsInOrder()
    {
        var cut = RenderVehicle(VehicleOf(DemoCast.Wagon.Id));

        Assert.Equal(DemoCast.Wagon.Name, cut.Find("h2#realm-detail-title").TextContent);
        Assert.Equal(DemoCast.Wagon.Lore, cut.Find(".realm-detail-eyebrow").TextContent);
        Assert.Equal("Updated 20 min ago", cut.Find(".realm-detail-updated").TextContent);
        Assert.Empty(cut.FindAll(".realm-detail-warning"));

        Assert.Equal(["Location", "Engine", "Fuel", "Odometer", "Last update"], cut.FindAll(".realm-detail-row dt").Select(label => label.TextContent));
        var values = Values(cut);
        Assert.Equal("At " + DemoPlaces.Home.Name, values["Location"]);
        Assert.Equal("Off", values["Engine"]);
        Assert.Equal("71%", cut.Find(".realm-detail-fuel-text").TextContent);
        Assert.Equal("18,432 mi", values["Odometer"]);
        Assert.Equal("9:05 pm (20 min ago)", values["Last update"]);
    }

    [Fact]
    public void TheFuelRow_IsAnEightPixelBar_WithTheValueAndNoRedAbove15Percent()
    {
        var cut = RenderVehicle(VehicleOf(DemoCast.Wagon.Id));

        var bar = cut.Find(".realm-detail-bar");
        Assert.Equal("progressbar", bar.GetAttribute("role"));
        Assert.Equal("71", bar.GetAttribute("aria-valuenow"));
        Assert.Equal("0", bar.GetAttribute("aria-valuemin"));
        Assert.Equal("100", bar.GetAttribute("aria-valuemax"));
        Assert.Equal("width:71%", cut.Find(".realm-detail-bar-fill").GetAttribute("style"));
        Assert.DoesNotContain("realm-detail-fuel--low", cut.Find("dd.realm-detail-fuel").ClassList);
    }

    [Fact]
    public void LowFuel_IsRed()
    {
        var cut = RenderVehicle(VehicleOf(DemoCast.Wagon.Id) with { FuelPct = 10 });

        Assert.Contains("realm-detail-fuel--low", cut.Find("dd.realm-detail-fuel").ClassList);
        Assert.Equal("10%", cut.Find(".realm-detail-fuel-text").TextContent);
        Assert.Equal("width:10%", cut.Find(".realm-detail-bar-fill").GetAttribute("style"));
    }

    [Fact]
    public void AMovingWagon_AddsTheSpeedRow_AfterTheEngine()
    {
        var cut = RenderVehicle(VehicleOf(DemoCast.Wagon.Id) with { IsMoving = true, SpeedMps = 27.7 });

        Assert.Equal(["Location", "Engine", "Speed", "Fuel", "Odometer", "Last update"], cut.FindAll(".realm-detail-row dt").Select(label => label.TextContent));
        Assert.Equal("62 mph", Values(cut)["Speed"]);
        Assert.Equal("At " + DemoPlaces.Home.Name, Values(cut)["Location"]);
    }

    [Fact]
    public void AMovingWagonWithNoReportedSpeed_HasNoSpeedRow()
    {
        var cut = RenderVehicle(VehicleOf(DemoCast.Wagon.Id) with { IsMoving = true, SpeedMps = null });

        Assert.DoesNotContain("Speed", cut.FindAll(".realm-detail-row dt").Select(label => label.TextContent));
    }

    [Fact]
    public void AStaleWagon_ShowsTheWarningLineAtTheTop_AndTheUpdateLineInTheWarningTone()
    {
        var stale = VehicleOf(DemoCast.Wagon.Id) with { Freshness = Freshness.Stale, LastUpdateUtc = DemoNow - TimeSpan.FromMinutes(80) };

        var cut = RenderVehicle(stale);

        var warning = cut.Find(".realm-detail-warning");
        Assert.Equal(SelectionHeaderFormatter.VehicleStaleWarning(stale, Facts), warning.TextContent.Trim());
        Assert.Equal("Last heard 1 hr ago.", warning.TextContent.Trim());
        Assert.Contains("realm-detail-warning", cut.Find(".realm-detail-body").FirstElementChild!.ClassList);
        Assert.Contains("realm-row-detail--warning", cut.Find(".realm-detail-updated").ClassList);
    }

    [Fact]
    public void WhatIsUnknown_ReadsADash_AndTheLocationRowStays()
    {
        var unknown = VehicleOf(DemoCast.Wagon.Id) with { Ignition = null, FuelPct = null, OdometerM = null, LastUpdateUtc = null };

        var cut = RenderVehicle(unknown);

        Assert.Equal(["Location", "Engine", "Fuel", "Odometer", "Last update"], cut.FindAll(".realm-detail-row dt").Select(label => label.TextContent));
        var values = Values(cut);
        Assert.Equal("At " + DemoPlaces.Home.Name, values["Location"]);
        Assert.Equal(Dash, values["Engine"]);
        Assert.Equal(Dash, values["Fuel"]);
        Assert.Equal(Dash, values["Odometer"]);
        Assert.Equal(Dash, values["Last update"]);
        Assert.Empty(cut.FindAll(".realm-detail-bar"));
        Assert.Empty(cut.FindAll(".realm-detail-updated"));
    }

    [Fact]
    public void ThePlaceholder_ShowsItsNoteUnderLocation_AndNoOtherRow()
    {
        var cut = RenderVehicle(VehicleOf(DemoCast.Chariot.Id));

        Assert.Equal(DemoCast.Chariot.Name, cut.Find("h2#realm-detail-title").TextContent);
        Assert.Equal(["Location"], cut.FindAll(".realm-detail-row dt").Select(label => label.TextContent));
        Assert.Equal(DemoCast.ChariotNote, Values(cut)["Location"]);
        Assert.Empty(cut.FindAll(".realm-detail-warning"));
        Assert.Empty(cut.FindAll(".realm-detail-bar"));
    }

    [Fact]
    public void TheAvatar_IsTheTruckSilhouetteForThePickup_AndTheCarIconForTheHatchback()
    {
        var truck = RenderVehicle(VehicleOf(DemoCast.Wagon.Id)).Find(".realm-detail-avatar");
        var car = RenderVehicle(VehicleOf(DemoCast.Chariot.Id)).Find(".realm-detail-avatar");

        Assert.Equal(4, truck.InnerHtml.Split("<path", StringSplitOptions.None).Length - 1);
        Assert.NotEqual(truck.InnerHtml, car.InnerHtml);
        Assert.Equal("true", truck.GetAttribute("aria-hidden"));
    }

    [Fact]
    public async Task TheVehiclesBackArrow_RaisesOnBack_Once()
    {
        var backs = 0;
        var cut = RenderVehicle(
            VehicleOf(DemoCast.Wagon.Id),
            detail => detail.Add(p => p.OnBack, () =>
            {
                backs++;
                return Task.CompletedTask;
            }));

        await cut.Find(BackSel).TriggerEventAsync("onclick", new MouseEventArgs());

        Assert.Equal(1, backs);
        Assert.Single(cut.FindAll("button"));
    }

    [Fact]
    public void Vehicle_FocusOnOpen_MovesTheFocusToTheBackButton_AndOtherwiseNothingDoes()
    {
        RenderVehicle(VehicleOf(DemoCast.Wagon.Id));
        AssertNothingFocused();

        var cut = RenderVehicle(VehicleOf(DemoCast.Wagon.Id), detail => detail.Add(p => p.FocusOnOpen, true));

        cut.WaitForAssertion(() => JSInterop.VerifyFocusAsyncInvoke());
    }

    // ---- a place -----------------------------------------------------------------------------------------------------------------------------

    [Fact(DisplayName = "[AC-31c] Hearth Haven's detail: the name, Home, Radius 328 ft, Here now (2) over Alden's row and the pickup's, and the caption")]
    public void Home_ShowsItsHeader_TheHereNowList_AndTheCaption()
    {
        var cut = RenderPlace(PlaceOf(DemoPlaces.Home.Id));

        Assert.Equal(DemoPlaces.Home.Name, cut.Find("h2#realm-detail-title").TextContent);
        Assert.Equal(
            [DemoPlaces.Home.Subtitle, "Radius 328 ft"],
            cut.FindAll(".realm-detail-heading .realm-detail-updated").Select(line => line.TextContent));
        Assert.Equal("Here now (2)", cut.Find("h3.realm-detail-here-title").TextContent);

        var items = cut.FindAll("ul.realm-detail-here > li");
        Assert.Equal(2, items.Count);

        var alden = items[0].QuerySelector("button[data-testid='row-member-king']");
        Assert.NotNull(alden);
        Assert.Equal(DemoCast.King.Name, alden.QuerySelector(".realm-here-name")!.TextContent);
        Assert.Equal("Since 5:52 pm", alden.QuerySelector(".realm-here-line")!.TextContent);
        Assert.Equal("A", alden.QuerySelector(".realm-row-initial")!.TextContent);

        var wagon = items[1].QuerySelector(".realm-here-row--static");
        Assert.NotNull(wagon);
        Assert.Equal(DemoCast.Wagon.Name, wagon.QuerySelector(".realm-here-name")!.TextContent);
        Assert.Equal(DemoCast.Wagon.Lore, wagon.QuerySelector(".realm-here-line")!.TextContent);
        Assert.Empty(items[1].QuerySelectorAll("button"));

        Assert.Equal("Places are set up in Home Assistant.", cut.Find("p.realm-detail-caption").TextContent);
        Assert.Empty(cut.FindAll(".realm-detail-empty"));
    }

    [Fact(DisplayName = "[AC-31d] Tapping a person in the Here-now list raises OnSelect with that member")]
    public async Task ThePersonsRow_RaisesOnSelect_WithTheMember()
    {
        var selected = new List<EntityRef>();
        var cut = RenderPlace(
            PlaceOf(DemoPlaces.Home.Id),
            detail => detail.Add(p => p.OnSelect, (EntityRef entity) =>
            {
                selected.Add(entity);
                return Task.CompletedTask;
            }));

        await cut.Find("[data-testid='row-member-king']").TriggerEventAsync("onclick", new MouseEventArgs());

        Assert.Equal([new EntityRef(EntityKind.Member, DemoCast.King.Id)], selected);
    }

    [Fact]
    public void TheVehiclesRow_IsNotAButton_AndHasNeitherARoleNorATabStop()
    {
        var cut = RenderPlace(PlaceOf(DemoPlaces.Home.Id));

        var row = cut.Find(".realm-here-row--static");
        Assert.Equal("div", row.LocalName);
        Assert.Null(row.GetAttribute("role"));
        Assert.Null(row.GetAttribute("tabindex"));
        Assert.Null(row.GetAttribute("blazor:onclick"));
        Assert.Single(cut.FindAll("button[data-testid^='row-member-']"));
    }

    [Fact]
    public void AnEmptyPlace_SaysTheHallStandsEmpty_AndHasNoList()
    {
        var cut = RenderPlace(PlaceOf(DemoPlaces.Vet.Id));

        Assert.Equal(SelectionHeaderFormatter.HereNow(0), cut.Find("h3.realm-detail-here-title").TextContent);
        Assert.Equal("Nobody's here. The hall stands empty.", cut.Find("p.realm-detail-empty").TextContent);
        Assert.Empty(cut.FindAll("ul"));
        Assert.Equal("Places are set up in Home Assistant.", cut.Find("p.realm-detail-caption").TextContent);
        Assert.Equal(DemoPlaces.Vet.Name, cut.Find("h2#realm-detail-title").TextContent);
    }

    [Fact]
    public void ABlankSubtitle_IsOmitted_AndTheRadiusStays()
    {
        var cut = RenderPlace(PlaceOf(DemoPlaces.Home.Id) with { Subtitle = "  " });

        Assert.Equal(["Radius 328 ft"], cut.FindAll(".realm-detail-heading .realm-detail-updated").Select(line => line.TextContent));
    }

    [Fact]
    public void AnIdThatIsNoLongerInTheLists_HasNoRow_ButStillCounts()
    {
        var ghost = PlaceOf(DemoPlaces.Home.Id) with { MemberIdsInside = [DemoCast.King.Id, "ghost"] };

        var cut = RenderPlace(ghost);

        Assert.Equal("Here now (3)", cut.Find("h3.realm-detail-here-title").TextContent);
        Assert.Equal(2, cut.FindAll("ul.realm-detail-here > li").Count);
    }

    [Fact]
    public async Task ThePlacesBackArrow_RaisesOnBack_Once()
    {
        var backs = 0;
        var cut = RenderPlace(
            PlaceOf(DemoPlaces.Home.Id),
            detail => detail.Add(p => p.OnBack, () =>
            {
                backs++;
                return Task.CompletedTask;
            }));

        await cut.Find(BackSel).TriggerEventAsync("onclick", new MouseEventArgs());

        Assert.Equal(1, backs);
    }

    [Fact]
    public void Place_FocusOnOpen_MovesTheFocusToTheBackButton_AndOtherwiseNothingDoes()
    {
        RenderPlace(PlaceOf(DemoPlaces.Home.Id));
        AssertNothingFocused();

        var cut = RenderPlace(PlaceOf(DemoPlaces.Home.Id), detail => detail.Add(p => p.FocusOnOpen, true));

        cut.WaitForAssertion(() => JSInterop.VerifyFocusAsyncInvoke());
    }

    // ---- helpers ---------------------------------------------------------------------------------------------------------------------------

    // bUnit raises when the focus call it verifies was never made.
    private void AssertNothingFocused() => Assert.ThrowsAny<Exception>(() => JSInterop.VerifyFocusAsyncInvoke());

    // dt text to dd text of the vehicle's rows.
    private static Dictionary<string, string> Values(IRenderedComponent<VehicleDetail> cut) =>
        cut.FindAll(".realm-detail-row").ToDictionary(row => row.QuerySelector("dt")!.TextContent, row => row.QuerySelector("dd")!.TextContent.Trim());

    // The member detail at the Demo clock and zone; the session, when given, is the one RealmShell would cascade; more adds parameters.
    private IRenderedComponent<MemberDetail> RenderMember(
        MemberVm member,
        IRealmSession? session = null,
        Action<ComponentParameterCollectionBuilder<MemberDetail>>? more = null) =>
        RenderWithProviders<MemberDetail>(detail =>
        {
            detail
                .Add(p => p.Member, member)
                .Add(p => p.Facts, Facts);
            if (session is not null)
            {
                detail.AddCascadingValue(session);
            }

            more?.Invoke(detail);
        });

    private IRenderedComponent<VehicleDetail> RenderVehicle(VehicleVm vehicle, Action<ComponentParameterCollectionBuilder<VehicleDetail>>? more = null) =>
        RenderWithProviders<VehicleDetail>(detail =>
        {
            detail
                .Add(p => p.Vehicle, vehicle)
                .Add(p => p.Facts, Facts);
            more?.Invoke(detail);
        });

    private IRenderedComponent<PlaceDetail> RenderPlace(PlaceVm place, Action<ComponentParameterCollectionBuilder<PlaceDetail>>? more = null) =>
        RenderWithProviders<PlaceDetail>(detail =>
        {
            detail
                .Add(p => p.Place, place)
                .Add(p => p.Vehicles, Demo.Vehicles)
                .Add(p => p.Facts, Facts);
            more?.Invoke(detail);
        });

    // The Demo session with a read or two replaced and every read counted, to say what the detail does when one of them fails, is late, or is never asked for.
    private sealed class StubSession(IRealmSession inner) : IRealmSession
    {
        public Func<string, int, DayOfWeek, CancellationToken, ValueTask<DriverWeek?>>? OnDriverWeek { get; init; }

        public Func<int, DayOfWeek, CancellationToken, ValueTask<WeekReportVm>>? OnReport { get; init; }

        public int DriverWeekCalls { get; private set; }

        public int ReportCalls { get; private set; }

        public RealmSnapshot Current => inner.Current;

        public event Action? Changed
        {
            add => inner.Changed += value;
            remove => inner.Changed -= value;
        }

        public TimeProvider Time => inner.Time;

        public TimeZoneInfo Zone => inner.Zone;

        public ValueTask<WeekReportVm> GetWeekReportAsync(int weekOffset, DayOfWeek weekStart, CancellationToken ct)
        {
            ReportCalls++;
            return OnReport is null ? inner.GetWeekReportAsync(weekOffset, weekStart, ct) : OnReport(weekOffset, weekStart, ct);
        }

        public ValueTask<DriverWeek?> GetDriverWeekAsync(string memberId, int weekOffset, DayOfWeek weekStart, CancellationToken ct)
        {
            DriverWeekCalls++;
            return OnDriverWeek is null ? inner.GetDriverWeekAsync(memberId, weekOffset, weekStart, ct) : OnDriverWeek(memberId, weekOffset, weekStart, ct);
        }

        public string? ResolveMe(string? haUserId) => inner.ResolveMe(haUserId);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
