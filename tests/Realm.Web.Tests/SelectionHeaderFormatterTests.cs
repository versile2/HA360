using Realm.Demo;
using Realm.Domain;
using Realm.Web.Formatting;
using Realm.Web.Map;
using Realm.Web.Sheet;
using Xunit;

namespace Realm.Web.Tests;

/// <summary>
/// The Peek selection header's strings (<see cref="SelectionHeaderFormatter"/>, 01 section 3.4.2 and the 8.4 row "Peek selection header, line 2", D45) and the strings of the detail views, from the Demo cast:
/// Cass (name and lore, the status line and "Since", the low battery), the static Elio (no time part, no badge), the pickup ("At Hearth Haven · Updated 20 min ago", "Last heard" when
/// stale), the hatchback last heard long ago, a place with occupants (people plus vehicles) and an empty one, a null lore title and the accessible name. Every string a test compares is read from
/// <see cref="DemoCast"/> and <see cref="DemoPlaces"/> or from the Demo snapshot, never retyped; the header and its row come from one source, which the tests check by comparing them.
/// </summary>
public sealed class SelectionHeaderFormatterTests
{
    private static readonly IRealmSession Session = FullCast.Session(null);

    private static readonly RealmSnapshot Demo = Session.Current;

    private static readonly DateTimeOffset DemoNow = Session.Time.GetUtcNow();

    // Alden is the viewer, as the app resolves "me" when nobody was asked for.
    private static readonly RowFacts Facts = RowFacts.Create(Demo.Members, Demo.Places, null, DemoNow, Session.Zone);

    private static MemberVm MemberOf(string id) => Demo.Members.Single(member => member.Id == id);

    private static VehicleVm VehicleOf(string id) => Demo.Vehicles.Single(vehicle => vehicle.Id == id);

    private static PlaceVm PlaceOf(string id) => Demo.Places.Single(place => place.Id == id);

    private static SelectionHeaderVm MemberHeader(MemberVm member) => SelectionHeaderFormatter.Member(member, Facts);

    private static SelectionHeaderVm VehicleHeader(VehicleVm vehicle) => SelectionHeaderFormatter.Vehicle(vehicle, Facts);

    // ---- a person -------------------------------------------------------------------------------------------------------------------------

    [Fact(DisplayName = "[AC-22a] Cass's header: name and lore, the status line and Since 9:06 pm, and the 12% badge in the low style")]
    public void Cass_HasNameAndLore_TheStatusLineAndSince_AndTheLowBadge()
    {
        var header = MemberHeader(MemberOf(DemoCast.Jester.Id));

        Assert.Equal(new EntityRef(EntityKind.Member, DemoCast.Jester.Id), header.Entity);
        Assert.Equal(DemoCast.Jester.Name, header.Line1);
        Assert.Equal(DemoCast.Jester.Lore, header.Line1Lore);
        Assert.Equal("At " + DemoPlaces.JesterHall.Name, header.Line2Lead);
        Assert.Equal("Since 9:06 pm", header.Line2Tail);
        Assert.Equal("At " + DemoPlaces.JesterHall.Name + " · Since 9:06 pm", header.Line2);
        Assert.Equal("C", header.Initial);
        Assert.Equal(DemoCast.Jester.Color, header.Color);
        Assert.Null(header.Glyph);
        Assert.Null(header.ZoneKind);
        var battery = Assert.IsType<BatteryBadgeVm>(header.Battery);
        Assert.Equal("12%", battery.Text);
        Assert.True(battery.Low);
        Assert.False(battery.Charging);
    }

    [Fact]
    public void Alden_HasTheChargingBadge_AndNoDistanceInTheLine()
    {
        var header = MemberHeader(MemberOf(DemoCast.King.Id));

        Assert.Equal("At " + DemoPlaces.Home.Name + " · Since 5:52 pm", header.Line2);
        var battery = Assert.IsType<BatteryBadgeVm>(header.Battery);
        Assert.Equal("19%", battery.Text);
        Assert.True(battery.Charging);
        Assert.False(battery.Low);
        Assert.DoesNotContain("away", header.Line2, StringComparison.Ordinal);
    }

    [Fact]
    public void Cass_TheDistanceOfTheRowIsNotRepeatedInTheHeader()
    {
        // Cass's row says "Since 9:06 pm · 1.0 mi away"; his header leaves the distance to the row (01 section 3.4.2).
        var row = VmFactory.Member(MemberOf(DemoCast.Jester.Id), Facts);
        var header = MemberHeader(MemberOf(DemoCast.Jester.Id));

        Assert.EndsWith(" away", row.DetailLine, StringComparison.Ordinal);
        Assert.DoesNotContain("away", header.Line2, StringComparison.Ordinal);
    }

    [Fact]
    public void Briar_Driving_ReadsHerRowsStatusLine_ThenSince()
    {
        var header = MemberHeader(MemberOf(DemoCast.Queen.Id));

        Assert.Equal($"Driving · 54 mph on {DemoCast.Queen.Address}", header.Line2Lead);
        Assert.Equal("Since 9:12 pm", header.Line2Tail);
        Assert.Equal($"Driving · 54 mph on {DemoCast.Queen.Address} · Since 9:12 pm", header.Line2);
    }

    [Fact(DisplayName = "Dara is stale and far away: the street, the city and state, then Last seen 42 min ago")]
    public void Dara_IsStaleAndFar_TheLineEndsInLastSeen()
    {
        var dara = MemberOf(DemoCast.Cryptid.Id);

        var header = MemberHeader(dara);

        Assert.Equal($"{dara.Street} · {dara.City}, {dara.Region}", header.Line2Lead);
        Assert.Equal("Last seen 42 min ago", header.Line2Tail);
        Assert.Equal($"{dara.Street} · {dara.City}, {dara.Region} · Last seen 42 min ago", header.Line2);
    }

    [Fact]
    public void AnOfflineMember_ReadsLastSeen_AsTheStaleOneDoes()
    {
        var offline = MemberOf(DemoCast.Cryptid.Id) with { Freshness = Freshness.Offline, LastUpdateUtc = DemoNow - TimeSpan.FromHours(3) };

        Assert.Equal("Last seen 3 hr ago", MemberHeader(offline).Line2Tail);
    }

    [Fact]
    public void AFreshMemberWithoutAnArrivalTime_ReadsUpdated_AndOneWithNeitherHasNoTail()
    {
        var jester = MemberOf(DemoCast.Jester.Id);

        var updated = MemberHeader(jester with { SinceUtc = null });
        Assert.Equal("Updated 3 min ago", updated.Line2Tail);

        var neither = MemberHeader(jester with { SinceUtc = null, LastUpdateUtc = null });
        Assert.Null(neither.Line2Tail);
        Assert.Equal(neither.Line2Lead, neither.Line2);
    }

    [Fact]
    public void ANoFixMember_ReadsLocationUnavailable_WithNoTime()
    {
        var noFix = MemberOf(DemoCast.Jester.Id) with { Freshness = Freshness.NoFix, Lat = null, Lon = null, PlaceId = null };

        var header = MemberHeader(noFix);

        Assert.Equal("Location unavailable", header.Line2);
        Assert.Null(header.Line2Tail);
    }

    [Fact(DisplayName = "[AC-22b] The static Elio's header reads his label alone: no time part, no badge")]
    public void Elio_ReadsHisLabel_WithNoTimeAndNoBadge()
    {
        var header = MemberHeader(MemberOf(DemoCast.Prince.Id));

        Assert.Equal(DemoCast.Prince.Name, header.Line1);
        Assert.Equal(DemoCast.Prince.Lore, header.Line1Lore);
        Assert.Equal(DemoCast.Prince.StaticLabel, header.Line2);
        Assert.Null(header.Line2Tail);
        Assert.Null(header.Battery);
    }

    [Fact]
    public void ANullOrBlankLoreTitle_DropsTheLore_AndLeavesTheName()
    {
        var jester = MemberOf(DemoCast.Jester.Id);

        var none = MemberHeader(jester with { LoreTitle = null });
        Assert.Null(none.Line1Lore);
        Assert.Equal(DemoCast.Jester.Name, none.Line1);

        Assert.Null(MemberHeader(jester with { LoreTitle = "  " }).Line1Lore);
        Assert.Null(VehicleHeader(VehicleOf(DemoCast.Wagon.Id) with { LoreTitle = null }).Line1Lore);
    }

    [Fact]
    public void AMemberWithoutABatteryReading_HasNoBadge()
    {
        Assert.Null(MemberHeader(MemberOf(DemoCast.King.Id) with { BatteryPct = null }).Battery);
    }

    [Fact]
    public void ThePhoto_AndTheUnsafeColour_AreTheRowsOwn()
    {
        var king = MemberOf(DemoCast.King.Id);

        var photo = MemberHeader(king with { AvatarUrl = "avatar/king" });
        Assert.Equal("avatar/king", photo.AvatarUrl);

        var hostile = MemberHeader(king with { Color = "red; background:url(x)" });
        Assert.Null(hostile.Color);
        Assert.Null(MemberHeader(king).AvatarUrl);
    }

    [Theory]
    [InlineData("jester")]
    [InlineData("king")]
    [InlineData("queen")]
    [InlineData("cryptid")]
    [InlineData("prince")]
    public void ThePersonsAccessibleName_IsTheRowsPinName(string id)
    {
        var member = MemberOf(id);

        Assert.Equal(VmFactory.Member(member, Facts).AccessibleName, MemberHeader(member).AccessibleName);
    }

    [Fact]
    public void Cass_AccessibleName_CarriesTheFullFacts()
    {
        var name = MemberHeader(MemberOf(DemoCast.Jester.Id)).AccessibleName;

        Assert.Equal(
            $"{DemoCast.Jester.Name}, {DemoCast.Jester.Lore}. At {DemoPlaces.JesterHall.Name} since 9:06 pm. Battery 12 percent, low. 1.0 mile away.",
            name);
    }

    [Fact]
    public void TheTail_IsTheOneThingTheRowsLineThreeSaysDifferently()
    {
        // The header's tail and the row's L3 share the facts: for a fresh member with an arrival time the tail is the row's own time part.
        var jester = MemberOf(DemoCast.Jester.Id);
        var row = VmFactory.Member(jester, Facts);

        Assert.Equal(MemberTextFormatter.TimePart(jester, row.Status, DemoNow, Session.Zone), MemberHeader(jester).Line2Tail);
    }

    // ---- a vehicle ------------------------------------------------------------------------------------------------------------------------

    [Fact(DisplayName = "[AC-28b] The pickup's header: Ford Pickup · The King's Wagon over At Hearth Haven · Updated 20 min ago, no badge")]
    public void TheWagon_ReadsLocationAndUpdate_WithNoBadge()
    {
        var header = VehicleHeader(VehicleOf(DemoCast.Wagon.Id));

        Assert.Equal(new EntityRef(EntityKind.Vehicle, DemoCast.Wagon.Id), header.Entity);
        Assert.Equal(DemoCast.Wagon.Name, header.Line1);
        Assert.Equal(DemoCast.Wagon.Lore, header.Line1Lore);
        Assert.Equal("At " + DemoPlaces.Home.Name, header.Line2Lead);
        Assert.Equal("Updated 20 min ago", header.Line2Tail);
        Assert.Equal("At " + DemoPlaces.Home.Name + " · Updated 20 min ago", header.Line2);
        Assert.Null(header.Battery);
        Assert.Equal(VehicleGlyph.Pickup, header.Glyph);
        Assert.Equal(string.Empty, header.Initial);
    }

    [Fact]
    public void AStaleWagon_ReadsLastHeard()
    {
        var stale = VehicleOf(DemoCast.Wagon.Id) with { Freshness = Freshness.Stale, LastUpdateUtc = DemoNow - TimeSpan.FromMinutes(80) };

        Assert.Equal("Last heard 1 hr ago", VehicleHeader(stale).Line2Tail);
    }

    [Fact]
    public void AWagonWithNoUpdateTime_HasNoTail()
    {
        var wagon = VehicleOf(DemoCast.Wagon.Id);

        var header = VehicleHeader(wagon with { LastUpdateUtc = null });

        Assert.Equal("At " + DemoPlaces.Home.Name, header.Line2);
        Assert.Null(header.Line2Tail);
    }

    [Fact]
    public void TheHatchback_ReadsLastHeard_AndTheCarGlyph()
    {
        var header = VehicleHeader(VehicleOf(DemoCast.Chariot.Id));

        Assert.Equal(DemoCast.Chariot.Name, header.Line1);
        Assert.StartsWith("Last heard", header.Line2Tail, StringComparison.Ordinal);
        Assert.Equal(VehicleGlyph.Car, header.Glyph);
    }

    [Fact]
    public void TheVehiclesAccessibleName_IsTheRowsName()
    {
        var wagon = VehicleOf(DemoCast.Wagon.Id);

        Assert.Equal(VmFactory.Vehicle(wagon, Facts).AccessibleName, VehicleHeader(wagon).AccessibleName);
    }

    // ---- a place --------------------------------------------------------------------------------------------------------------------------

    [Fact(DisplayName = "[AC-31a] Hearth Haven reads Here now (2): Alden and the pickup, people plus vehicles, where its row says 1 here")]
    public void Home_CountsPeopleAndVehicles()
    {
        var home = PlaceOf(DemoPlaces.Home.Id);

        var header = SelectionHeaderFormatter.Place(home);

        Assert.Equal(new EntityRef(EntityKind.Place, DemoPlaces.Home.Id), header.Entity);
        Assert.Equal(DemoPlaces.Home.Name, header.Line1);
        Assert.Null(header.Line1Lore);
        Assert.Equal("Here now (2)", header.Line2);
        Assert.Null(header.Line2Tail);
        Assert.Equal(PlaceKind.Home, header.ZoneKind);
        Assert.Null(header.Battery);
        Assert.Equal(DemoPlaces.Home.Name + ". Here now (2).", header.AccessibleName);
        Assert.Equal(2, SelectionHeaderFormatter.Occupants(home));
        Assert.Equal("1 here", VmFactory.Place(home, Facts).CountText);
    }

    // 01 section 5.3 (R2-009): the place header and the detail heading "Here now ({n})" count people and vehicles (5.6), where the row's "n here", the zone's occupied flag and the
    // "{m} occupied" summary count people only. A place that holds only the pickup therefore reads "Here now (1)" in its header, "Empty" in its row, and is not occupied.
    [Fact]
    public void APlaceWithOnlyThePickup_ReadsHereNowOne_WhereItsRowReadsEmptyAndItIsNotOccupied()
    {
        var garage = PlaceOf(DemoPlaces.Work.Id) with { MemberIdsInside = [], VehicleIdsInside = [DemoCast.Wagon.Id] };

        Assert.Equal(1, SelectionHeaderFormatter.Occupants(garage));
        Assert.Equal("Here now (1)", SelectionHeaderFormatter.Place(garage).Line2);
        Assert.Equal(PlaceTextFormatter.Empty, VmFactory.Place(garage, Facts).CountText);
        Assert.Equal("1 place · all quiet", HandleSummaryFormatter.Places([garage]));
    }

    [Fact]
    public void APlaceWithThreePeople_ReadsHereNowThree()
    {
        var crowded = PlaceOf(DemoPlaces.Home.Id) with { MemberIdsInside = ["king", "queen", "jester"], VehicleIdsInside = [] };

        Assert.Equal("Here now (3)", SelectionHeaderFormatter.Place(crowded).Line2);
    }

    [Fact]
    public void AnEmptyPlace_ReadsEmpty()
    {
        var header = SelectionHeaderFormatter.Place(PlaceOf(DemoPlaces.Vet.Id));

        Assert.Equal("Empty", header.Line2);
        Assert.Equal(DemoPlaces.Vet.Name + ". Empty.", header.AccessibleName);
        Assert.Equal(PlaceKind.Vet, header.ZoneKind);
        Assert.Equal(0, SelectionHeaderFormatter.Occupants(PlaceOf(DemoPlaces.Vet.Id)));
    }

    [Fact]
    public void ARepeatedPlaceName_IsTheRowsOwnName()
    {
        // The header says the display name, as the list row does (01 section 3.4.2): the two never disagree.
        var work2 = PlaceOf(DemoPlaces.Work2.Id);

        var header = SelectionHeaderFormatter.Place(work2);

        Assert.Equal(VmFactory.Place(work2, Facts).Name, header.Line1);
        Assert.Equal(DemoPlaces.Work2.Name, header.Line1);
    }

    // ---- For ------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void For_FindsTheEntityByKindAndId()
    {
        var member = SelectionHeaderFormatter.For(new EntityRef(EntityKind.Member, DemoCast.Jester.Id), Demo.Members, Demo.Vehicles, Demo.Places, Facts);
        var vehicle = SelectionHeaderFormatter.For(new EntityRef(EntityKind.Vehicle, DemoCast.Wagon.Id), Demo.Members, Demo.Vehicles, Demo.Places, Facts);
        var place = SelectionHeaderFormatter.For(new EntityRef(EntityKind.Place, DemoPlaces.Home.Id), Demo.Members, Demo.Vehicles, Demo.Places, Facts);

        Assert.Equal(DemoCast.Jester.Name, member?.Line1);
        Assert.Equal(DemoCast.Wagon.Name, vehicle?.Line1);
        Assert.Equal(DemoPlaces.Home.Name, place?.Line1);
    }

    [Theory]
    [InlineData(EntityKind.Member)]
    [InlineData(EntityKind.Vehicle)]
    [InlineData(EntityKind.Place)]
    public void For_ReturnsNull_ForAnEntityThatIsNoLongerInTheLists(EntityKind kind)
    {
        var header = SelectionHeaderFormatter.For(new EntityRef(kind, "nobody"), Demo.Members, Demo.Vehicles, Demo.Places, Facts);

        Assert.Null(header);
    }

    [Fact]
    public void For_AKindIdIsNotAnotherKindsId()
    {
        // A member id is not a place id: the same text under another kind does not resolve.
        var header = SelectionHeaderFormatter.For(new EntityRef(EntityKind.Place, DemoCast.Jester.Id), Demo.Members, Demo.Vehicles, Demo.Places, Facts);

        Assert.Null(header);
    }

    // ---- the detail strings ---------------------------------------------------------------------------------------------------------------

    [Fact]
    public void MemberCard_AddsUpdated_ForAFreshMember_AfterTheArrivalTime_AndOnTheTitleWhileDriving()
    {
        var jester = MemberOf(DemoCast.Jester.Id);
        var card = SelectionHeaderFormatter.MemberCard(jester, VmFactory.Member(jester, Facts), Facts);
        Assert.Equal("Since 9:06 pm · updated 3 min ago · 1.0 mi away", card.Detail);

        var queen = MemberOf(DemoCast.Queen.Id);
        var drive = VmFactory.Member(queen, Facts);
        var driving = SelectionHeaderFormatter.MemberCard(queen, drive, Facts);
        Assert.StartsWith(drive.StatusLine + " · updated ", driving.Title, StringComparison.Ordinal);
        Assert.Equal(drive.DetailLine, driving.Detail);
    }

    [Fact]
    public void MemberCard_ReadsJustNow_UnderAMinute_AndKeepsTheArrivalLessMemberOnItsOwnUpdatedLine()
    {
        var fresh = MemberOf(DemoCast.King.Id) with { LastUpdateUtc = DemoNow };
        Assert.Contains("updated just now", SelectionHeaderFormatter.MemberCard(fresh, VmFactory.Member(fresh, Facts), Facts).Detail, StringComparison.Ordinal);

        var noSince = MemberOf(DemoCast.Jester.Id) with { SinceUtc = null };
        var row = VmFactory.Member(noSince, Facts);
        var card = SelectionHeaderFormatter.MemberCard(noSince, row, Facts);
        Assert.Equal(row.DetailLine, card.Detail);
        Assert.StartsWith("Updated 3 min ago", card.Detail, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(MemberStatus.Stale)]
    [InlineData(MemberStatus.Offline)]
    [InlineData(MemberStatus.NoFix)]
    [InlineData(MemberStatus.Static)]
    public void MemberCard_IsTheRowsOwn_ForStaleOfflineNoFixAndStaticMembers(MemberStatus status)
    {
        var member = MemberOf(DemoCast.Cryptid.Id);
        var row = VmFactory.Member(member, Facts) with { Status = status };
        var card = SelectionHeaderFormatter.MemberCard(member, row, Facts);

        Assert.Equal(row.StatusLine, card.Title);
        Assert.Equal(row.DetailLine, card.Detail);
    }

    [Fact(DisplayName = "[AC-30b] The battery chip reads 12% · Low battery and 19% · Charging, and both parts when both apply")]
    public void BatteryChip_ReadsTheStateAfterThePercentage()
    {
        var low = VmFactory.Member(MemberOf(DemoCast.Jester.Id), Facts).Battery!;
        var charging = VmFactory.Member(MemberOf(DemoCast.King.Id), Facts).Battery!;
        var plain = VmFactory.Member(MemberOf(DemoCast.Queen.Id), Facts).Battery!;

        Assert.Equal("12% · Low battery", SelectionHeaderFormatter.BatteryChip(low));
        Assert.Equal("19% · Charging", SelectionHeaderFormatter.BatteryChip(charging));
        Assert.Equal("62%", SelectionHeaderFormatter.BatteryChip(plain));
        Assert.Equal("9% · Charging · Low battery", SelectionHeaderFormatter.BatteryChip(low with { Text = "9%", Charging = true }));
    }

    [Fact]
    public void AccuracyChip_AppearsOnlyAboveTheThreshold_AndOnlyWithAFix()
    {
        var jester = MemberOf(DemoCast.Jester.Id);
        var poor = Facts.Options.PoorAccuracyMeters + 305;

        Assert.Equal("Approximate · ± 0.5 mi", SelectionHeaderFormatter.AccuracyChip(jester with { AccuracyM = poor }, MemberStatus.Out, Facts));
        Assert.Null(SelectionHeaderFormatter.AccuracyChip(jester with { AccuracyM = Facts.Options.PoorAccuracyMeters }, MemberStatus.Out, Facts));
        Assert.Null(SelectionHeaderFormatter.AccuracyChip(jester with { AccuracyM = null }, MemberStatus.Out, Facts));
        Assert.Null(SelectionHeaderFormatter.AccuracyChip(jester with { AccuracyM = poor }, MemberStatus.NoFix, Facts));
        Assert.Null(SelectionHeaderFormatter.AccuracyChip(jester with { AccuracyM = poor }, MemberStatus.Static, Facts));
    }

    [Fact]
    public void StaticDistance_IsMeasuredFromMe_AndIsNullForMe()
    {
        var elio = MemberOf(DemoCast.Prince.Id);

        var distance = SelectionHeaderFormatter.StaticDistance(elio, Facts);

        Assert.NotNull(distance);
        Assert.EndsWith(" mi away", distance, StringComparison.Ordinal);
        Assert.Null(SelectionHeaderFormatter.StaticDistance(MemberOf(DemoCast.King.Id), Facts));
        Assert.Null(SelectionHeaderFormatter.StaticDistance(elio with { Lat = null }, Facts));
    }

    [Fact]
    public void StaticSentence_IsBuiltFromTheLoreTitle_WithoutASecondThe()
    {
        var elio = MemberOf(DemoCast.Prince.Id);

        Assert.Equal($"The {DemoCast.Prince.Lore} keeps his own counsel.", SelectionHeaderFormatter.StaticSentence(elio));
        Assert.Equal("The Hermit keeps his own counsel.", SelectionHeaderFormatter.StaticSentence(elio with { LoreTitle = "The Hermit" }));
        Assert.Equal("The Hermit keeps his own counsel.", SelectionHeaderFormatter.StaticSentence(elio with { LoreTitle = " The Hermit " }));
        Assert.Null(SelectionHeaderFormatter.StaticSentence(elio with { LoreTitle = null }));
        Assert.Null(SelectionHeaderFormatter.StaticSentence(elio with { LoreTitle = " " }));
    }

    [Fact(DisplayName = "[AC-30c] The week tiles read 18 drives, 202.6 mi, 88 mph top for Cass and a dash for what is unknown")]
    public async Task WeekTiles_ReadTheDriversWeek_AndADashForUnknownData()
    {
        var week = await Session.GetDriverWeekAsync(DemoCast.Jester.Id, 0, Demo.WeekStart, CancellationToken.None);
        var report = await Session.GetWeekReportAsync(0, Demo.WeekStart, CancellationToken.None);
        var summary = Assert.IsType<DriverWeek>(week).Summary;
        var top = DrivingFormatter.TopSpeedOf(report, DemoCast.Jester.Id);

        var tiles = SelectionHeaderFormatter.WeekTiles(summary, top);
        Assert.Equal(["18 drives", "202.6 mi", "88 mph top"], tiles.Select(tile => tile.Value + " " + tile.Label));

        Assert.Equal(["— drives", "— mi", "— top"], SelectionHeaderFormatter.WeekTiles(null, null).Select(tile => tile.Value + " " + tile.Label));
        Assert.Equal(["— drives", "— mi", "— top"], SelectionHeaderFormatter.WeekTiles(summary with { Covered = false }, top).Select(tile => tile.Value + " " + tile.Label));
        Assert.Equal("— top", SelectionHeaderFormatter.WeekTiles(summary, null).Select(tile => tile.Value + " " + tile.Label).Last());
        Assert.Equal("1 drive", SelectionHeaderFormatter.WeekTiles(summary with { Drives = 1 }, top).Select(tile => tile.Value + " " + tile.Label).First());
    }

    [Fact]
    public void VehicleLocation_NeverReadsDriving_AndKeepsThePlaceWhileTheVehicleMoves()
    {
        var moving = VehicleOf(DemoCast.Wagon.Id) with { IsMoving = true, SpeedMps = 27.7 };

        Assert.Equal("At " + DemoPlaces.Home.Name, SelectionHeaderFormatter.VehicleLocation(moving, Facts));
        Assert.StartsWith("Driving", VehicleTextFormatter.Location(moving, DemoPlaces.Home.Name), StringComparison.Ordinal);
        Assert.Equal(
            VehicleTextFormatter.LocationUnavailable,
            SelectionHeaderFormatter.VehicleLocation(VehicleOf(DemoCast.Wagon.Id) with { Freshness = Freshness.NoFix, Lat = null, Lon = null }, Facts));
    }

    [Fact]
    public void VehicleSpeed_IsOnlyForAMovingVehicleWithAKnownSpeed()
    {
        var wagon = VehicleOf(DemoCast.Wagon.Id);

        Assert.Equal("62 mph", SelectionHeaderFormatter.VehicleSpeed(wagon with { IsMoving = true, SpeedMps = 27.7 }, Facts));
        Assert.Null(SelectionHeaderFormatter.VehicleSpeed(wagon with { IsMoving = true, SpeedMps = null }, Facts));
        Assert.Null(SelectionHeaderFormatter.VehicleSpeed(wagon with { IsMoving = false, SpeedMps = 27.7 }, Facts));
        Assert.Null(SelectionHeaderFormatter.VehicleSpeed(wagon, Facts));
    }

    [Fact(DisplayName = "[AC-28c] The pickup's last update reads 9:05 pm with the relative age in parentheses")]
    public void VehicleLastUpdate_ReadsAsTheSpecWordsIt()
    {
        var wagon = VehicleOf(DemoCast.Wagon.Id);

        Assert.Equal("9:05 pm (20 min ago)", SelectionHeaderFormatter.VehicleLastUpdate(wagon, Facts));
        Assert.Null(SelectionHeaderFormatter.VehicleLastUpdate(wagon with { LastUpdateUtc = null }, Facts));
    }

    [Fact]
    public void VehicleLastUpdate_ADayOldReadingCarriesItsDay_WithoutRepeatingTheAge()
    {
        var old = VehicleOf(DemoCast.Wagon.Id) with { LastUpdateUtc = DemoNow - TimeSpan.FromDays(3) };

        var text = SelectionHeaderFormatter.VehicleLastUpdate(old, Facts);

        Assert.NotNull(text);
        Assert.DoesNotContain("(", text, StringComparison.Ordinal);
        Assert.Equal(TimeFormatter.When(old.LastUpdateUtc!.Value, DemoNow, Session.Zone), text);
    }

    [Fact]
    public void VehicleStaleWarning_IsOnlyForAStaleVehicle_AndHasAFullStop()
    {
        var wagon = VehicleOf(DemoCast.Wagon.Id);
        var stale = wagon with { Freshness = Freshness.Stale, LastUpdateUtc = DemoNow - TimeSpan.FromMinutes(80) };

        Assert.Equal("Last heard 1 hr ago.", SelectionHeaderFormatter.VehicleStaleWarning(stale, Facts));
        Assert.Null(SelectionHeaderFormatter.VehicleStaleWarning(wagon, Facts));
        Assert.Null(SelectionHeaderFormatter.VehicleStaleWarning(stale with { LastUpdateUtc = null }, Facts));
    }

    [Fact]
    public void HereSince_ReadsTheArrivalTime_AndIsEmptyWithout()
    {
        var king = MemberOf(DemoCast.King.Id);

        Assert.Equal("Since 5:52 pm", SelectionHeaderFormatter.HereSince(king, Facts));
        Assert.Equal(string.Empty, SelectionHeaderFormatter.HereSince(king with { SinceUtc = null }, Facts));
    }

    [Fact]
    public void TheJoin_IsTheMiddleDotOfTheCopyDeck()
    {
        Assert.Equal(" · ", SelectionHeaderFormatter.Separator);
        Assert.Equal("Clear selection", SelectionHeaderFormatter.ClearLabel);
        Assert.Equal("Here now (7)", SelectionHeaderFormatter.HereNow(7));
    }
}
