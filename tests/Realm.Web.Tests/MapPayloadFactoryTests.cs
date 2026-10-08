using Realm.Demo;
using Realm.Domain;
using Realm.Web.Formatting;
using Realm.Web.Map;
using Xunit;

namespace Realm.Web.Tests;

/// <summary>
/// <see cref="MapPayloadFactory"/> over the Demo snapshot (02 section 9.3) and over small hand-built view models: the status ladder and the ring,
/// badge and draw-order class of each state (01 sections 4.2 and 4.3), the accuracy halo, the zone exclusion above 5 km and of the arrival zone
/// (01 section 4.6), the static and vehicle pins (01 section 4.7), the "Here for" chip (01 section 4.4) and the default view (01 section 4.9).
/// </summary>
public sealed class MapPayloadFactoryTests
{
    private static readonly MapPayloadOptions Options = MapPayloadOptions.Default;

    // The viewer of every hand-built case sits here, at the Demo's home (02 section 9.2).
    private const double MeLat = 31.0990;
    private const double MeLon = -85.3410;

    // 0.001 degree of latitude is about 111 m.
    private const double Step = 0.001;

    private static readonly DateTimeOffset Now = DemoDataSource.Anchor;

    private static readonly RealmSnapshot Demo = LoadDemo();

    // ---- the status ladder ------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Demo_RingsBadgesAndDrawOrder_FollowTheLadder()
    {
        var members = DemoMembers();

        // AC-16's ring colours: Briar driving, Alden and Cass at a place, Dara stale, Elio static.
        AssertPin(members[DemoCast.King.Id], MemberStatus.AtPlace, MapPalette.RingAtPlace, dashed: false, badge: null, zClass: 1);
        AssertPin(members[DemoCast.Queen.Id], MemberStatus.Driving, MapPalette.RingDriving, dashed: false, badge: PinBadge.Driving, zClass: 3);
        AssertPin(members[DemoCast.Jester.Id], MemberStatus.AtPlace, MapPalette.RingAtPlace, dashed: false, badge: null, zClass: 1);
        AssertPin(members[DemoCast.Cryptid.Id], MemberStatus.Stale, MapPalette.RingStale, dashed: true, badge: PinBadge.Stale, zClass: 0);
        AssertPin(members[DemoCast.Prince.Id], MemberStatus.Static, MapPalette.RingStatic, dashed: true, badge: PinBadge.Home, zClass: 0);
        Assert.All(members.Values, member => Assert.Equal(4, member.Ring.WidthPx));
    }

    [Theory]
    [InlineData(Freshness.Fresh, MemberKind.Live, false, null, MemberStatus.Out, MapPalette.RingOut, false, null, 2)]
    [InlineData(Freshness.Fresh, MemberKind.Live, false, "park", MemberStatus.AtPlace, MapPalette.RingAtPlace, false, null, 1)]
    [InlineData(Freshness.Fresh, MemberKind.Live, true, null, MemberStatus.Driving, MapPalette.RingDriving, false, PinBadge.Driving, 3)]
    [InlineData(Freshness.Fresh, MemberKind.Live, true, "park", MemberStatus.Driving, MapPalette.RingDriving, false, PinBadge.Driving, 3)]
    [InlineData(Freshness.Stale, MemberKind.Live, true, "park", MemberStatus.Stale, MapPalette.RingStale, true, PinBadge.Stale, 0)]
    [InlineData(Freshness.Stale, MemberKind.Live, false, null, MemberStatus.Stale, MapPalette.RingStale, true, PinBadge.Stale, 0)]
    [InlineData(Freshness.Offline, MemberKind.Live, false, "park", MemberStatus.Offline, MapPalette.RingStale, true, PinBadge.Offline, 0)]
    [InlineData(Freshness.Static, MemberKind.Static, false, null, MemberStatus.Static, MapPalette.RingStatic, true, PinBadge.Home, 0)]
    [InlineData(Freshness.Fresh, MemberKind.Static, false, null, MemberStatus.Static, MapPalette.RingStatic, true, PinBadge.Home, 0)]
    public void StatusLadder_FirstMatchingRowWins(
        Freshness freshness, MemberKind kind, bool isDriving, string? placeId, MemberStatus status, string ring, bool dashed, PinBadge? badge, int zClass)
    {
        var member = Member("a", freshness: freshness, kind: kind, isDriving: isDriving, placeId: placeId);

        var item = Assert.Single(Members([member], [Place("park", 200)], meId: "a").Members);

        AssertPin(item, status, ring, dashed, badge, zClass);
        Assert.Equal(isDriving && freshness == Freshness.Fresh, item.DrivingFresh);
    }

    [Fact]
    public void NoFix_HasNoCoordinatesAndNoAccuracy()
    {
        var noFix = Member("a", freshness: Freshness.NoFix, lat: null, lon: null, accuracyM: 20);

        var item = Assert.Single(Members([noFix], [], meId: "a").Members);

        Assert.Equal(MemberStatus.NoFix, item.Status);
        Assert.Null(item.Lat);
        Assert.Null(item.Lon);
        Assert.Null(item.AccuracyM);
        Assert.False(item.PoorAccuracy);
        Assert.Equal(0, item.ZClass);
    }

    [Fact]
    public void HalfAPosition_IsNoFix_SoLatAndLonAreBothNullOrBothSet()
    {
        var half = Member("a", lat: 31.1, lon: null);

        var item = Assert.Single(Members([half], [], meId: "a").Members);

        Assert.Equal(MemberStatus.NoFix, item.Status);
        Assert.Null(item.Lat);
        Assert.Null(item.Lon);
    }

    [Fact]
    public void AtAPlace_MeansInsideAZoneTheMapDraws_NotTheArrivalZone()
    {
        var inArrivalZone = Member("a", placeId: "approach");
        var places = new[] { Place("approach", 32_187), Place("park", 200) };

        var item = Assert.Single(Members([inArrivalZone], places, meId: "a").Members);

        Assert.Equal(MemberStatus.Out, item.Status);
    }

    // ---- the accuracy halo and the other flags ------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(null, false)]
    [InlineData(18.0, false)]
    [InlineData(500.0, false)]
    [InlineData(500.5, true)]
    [InlineData(800.0, true)]
    public void PoorAccuracy_IsAnAccuracyAboveTheThreshold(double? accuracyM, bool poor)
    {
        var item = Assert.Single(Members([Member("a", accuracyM: accuracyM)], [], meId: "a").Members);

        Assert.Equal(poor, item.PoorAccuracy);
        Assert.Equal(accuracyM, item.AccuracyM);
    }

    [Fact]
    public void PoorAccuracy_UsesTheConfiguredThreshold()
    {
        var item = Assert.Single(MapPayloadFactory.Members([Member("a", accuracyM: 120)], [], "a", Now, Options with { PoorAccuracyMeters = 100 }, 1).Members);

        Assert.True(item.PoorAccuracy);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData(14, true)]
    [InlineData(15, false)]
    [InlineData(100, false)]
    public void LowBattery_IsBelowTheThreshold(int? battery, bool low)
    {
        var item = Assert.Single(Members([Member("a", battery: battery)], [], meId: "a").Members);

        Assert.Equal(low, item.LowBattery);
    }

    [Fact]
    public void TheStaticPin_NeverShowsALowBattery()
    {
        var item = Assert.Single(Members([Member("a", freshness: Freshness.Static, kind: MemberKind.Static, battery: 3)], [], meId: "a").Members);

        Assert.False(item.LowBattery);
    }

    [Fact]
    public void Demo_FarMeansMoreThanEightyKilometresFromMe_AndMeIsNeverFar()
    {
        var members = DemoMembers();

        Assert.False(members[DemoCast.King.Id].Far);
        Assert.False(members[DemoCast.Queen.Id].Far);
        Assert.False(members[DemoCast.Jester.Id].Far);
        Assert.True(members[DemoCast.Cryptid.Id].Far);
        Assert.True(members[DemoCast.Prince.Id].Far);
    }

    [Fact]
    public void Far_UsesTheConfiguredDistance()
    {
        var near = Member("b", lat: MeLat + (10 * Step));

        var payload = MapPayloadFactory.Members([Member("a", lat: MeLat, lon: MeLon), near], [], "a", Now, Options with { FarAwayKm = 1 }, 1);

        Assert.True(payload.Members[1].Far);
    }

    [Fact]
    public void WithoutAPositionForMe_NobodyIsFar()
    {
        var me = Member("a", freshness: Freshness.NoFix, lat: null, lon: null);
        var other = Member("b", lat: 38.8, lon: -92.8);

        var payload = Members([me, other], [], meId: "a");

        Assert.False(payload.Members[1].Far);
    }

    // ---- who is me ----------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Me_IsTheRequestedMember_ElseTheFirstLiveMemberInSortOrder()
    {
        var staticFirst = Member("s", kind: MemberKind.Static, freshness: Freshness.Static, sortOrder: 0);
        var second = Member("b", sortOrder: 2);
        var first = Member("a", sortOrder: 1);
        IReadOnlyList<MemberVm> members = [staticFirst, second, first];

        Assert.Equal("b", MapPayloadFactory.ResolveMeId(members, "b"));
        Assert.Equal("a", MapPayloadFactory.ResolveMeId(members, null));
        Assert.Equal("a", MapPayloadFactory.ResolveMeId(members, "nobody"));
        Assert.Null(MapPayloadFactory.ResolveMeId([staticFirst], null));
        Assert.Null(MapPayloadFactory.ResolveMeId([], "b"));
    }

    [Fact]
    public void Members_MarksExactlyOneMemberAsMe_AndCarriesTheIdOnThePayload()
    {
        var payload = Members(Demo.Members, Demo.Places, meId: DemoCast.Queen.Id);

        Assert.Equal(DemoCast.Queen.Id, payload.MeId);
        Assert.Equal([DemoCast.Queen.Id], payload.Members.Where(member => member.IsMe).Select(member => member.Id));
    }

    [Fact]
    public void WithoutAnyMember_TheMeIdIsEmpty()
    {
        var payload = Members([], [], meId: null);

        Assert.Equal(string.Empty, payload.MeId);
        Assert.Empty(payload.Members);
    }

    // ---- the chip -----------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Demo_OnlyMyChipIsShown_AndItIsHereFor3Hours33Minutes()
    {
        // AC-17: with nothing selected the viewer at a place has the chip, and nobody else does.
        var members = DemoMembers();

        Assert.Equal("Here for 3 hrs, 33 mins", members[DemoCast.King.Id].Chip);
        Assert.All(members.Values.Where(member => member.Id != DemoCast.King.Id), member => Assert.Null(member.Chip));
        Assert.Equal(Now.ToUnixTimeSeconds() / 60, members[DemoCast.King.Id].ChipMinute);
    }

    [Fact]
    public void Chip_NeedsMeAtAPlaceWithAKnownArrival()
    {
        var since = Now.AddMinutes(-5);
        var places = new[] { Place("park", 200) };

        Assert.Equal("Here for 5 mins", Chip(Member("a", placeId: "park", since: since), places));
        Assert.Null(Chip(Member("a", placeId: "park", since: null), places));
        Assert.Null(Chip(Member("a", placeId: null, since: since), places));
        Assert.Null(Chip(Member("a", placeId: "park", since: since, isDriving: true), places));
        Assert.Null(Chip(Member("a", placeId: "park", since: since, freshness: Freshness.Stale), places));
        Assert.Equal("Just arrived", Chip(Member("a", placeId: "park", since: Now.AddSeconds(-20)), places));
    }

    private static string? Chip(MemberVm me, IReadOnlyList<PlaceVm> places) => Assert.Single(Members([me], places, meId: me.Id).Members).Chip;

    // The chip follows the selection (01 section 4.4, review R1-03). [AC-17b] is AC-17's second sentence: "Selecting Briar shows `Driving · 54 mph` above her pin and removes Alden's chip."
    private static readonly EntityRef King = new(EntityKind.Member, DemoCast.King.Id);
    private static readonly EntityRef Queen = new(EntityKind.Member, DemoCast.Queen.Id);

    [Fact(DisplayName = "[AC-17b] Selecting Briar gives her \"Driving · 54 mph\" and removes Alden's chip")]
    public void Demo_SelectingBriar_ShowsHerDrivingChip_AndRemovesTheKingsChip()
    {
        var members = DemoMembersSelecting(Queen);

        Assert.Equal("Driving · 54 mph", members[DemoCast.Queen.Id].Chip);
        Assert.Equal([DemoCast.Queen.Id], members.Values.Where(member => member.Chip is not null).Select(member => member.Id));
    }

    [Fact(DisplayName = "[AC-17b] Every selected Demo member has the chip of the table in 01 section 4.4, and nobody else has one")]
    public void Demo_ASelectedMember_HasTheChipOfTheTable_AndIsTheOnlyChip()
    {
        (EntityRef Who, string Chip)[] rows =
        [
            (King, "Here for 3 hrs, 33 mins"),
            (Queen, "Driving · 54 mph"),
            (new EntityRef(EntityKind.Member, DemoCast.Jester.Id), "Here for 19 mins"),
            (new EntityRef(EntityKind.Member, DemoCast.Cryptid.Id), "Last seen 42 min ago"),
            (new EntityRef(EntityKind.Member, DemoCast.Prince.Id), DemoCast.Prince.StaticLabel!),
        ];

        foreach (var (who, chip) in rows)
        {
            var members = DemoMembersSelecting(who);

            Assert.Equal(chip, members[who.Id].Chip);
            Assert.Equal([who.Id], members.Values.Where(member => member.Chip is not null).Select(member => member.Id));
        }
    }

    [Fact]
    public void Demo_TheStaticPrincesChip_IsHisLabel()
    {
        Assert.Equal("Home · Highmeadow", DemoMembersSelecting(new EntityRef(EntityKind.Member, DemoCast.Prince.Id))[DemoCast.Prince.Id].Chip);
    }

    [Fact]
    public void MyChip_StaysWhileNothingIsSelected_AndWhileIAmTheSelection_ButGoesWhenAnythingElseIsSelected()
    {
        var since = Now.AddMinutes(-5);
        var places = new[] { Place("park", 200) };
        var me = Member("a", placeId: "park", since: since);
        var other = Member("b", placeId: "park", since: since);

        Assert.Equal("Here for 5 mins", ChipOf("a", [me, other], places, selection: null));
        Assert.Equal("Here for 5 mins", ChipOf("a", [me, other], places, new EntityRef(EntityKind.Member, "a")));
        Assert.Null(ChipOf("a", [me, other], places, new EntityRef(EntityKind.Member, "b")));
        Assert.Null(ChipOf("a", [me, other], places, new EntityRef(EntityKind.Vehicle, "v")));
        Assert.Null(ChipOf("a", [me, other], places, new EntityRef(EntityKind.Place, "park")));
        Assert.Equal("Here for 5 mins", ChipOf("b", [me, other], places, new EntityRef(EntityKind.Member, "b")));
    }

    [Fact]
    public void AnEntityOfAnotherKindWithTheSameId_IsNotTheSelectedMember()
    {
        var places = new[] { Place("park", 200) };
        var me = Member("a", placeId: "park", since: Now.AddMinutes(-5));
        var vehicle = Member("v", placeId: "park", since: Now.AddMinutes(-9));

        Assert.Null(ChipOf("v", [me, vehicle], places, new EntityRef(EntityKind.Vehicle, "v")));
        Assert.Equal("Here for 9 mins", ChipOf("v", [me, vehicle], places, new EntityRef(EntityKind.Member, "v")));
    }

    [Fact]
    public void MyChip_WhileNothingIsSelected_IsTheAtAPlaceOneAndNoOther()
    {
        // 01 section 4.4: "Me, when nothing else is selected and I am at a place". Driving, out, stale or static, I carry no chip until I am selected myself.
        var places = new[] { Place("park", 200) };

        Assert.Null(ChipOf("a", [Member("a", isDriving: true, speedMps: 24, since: Now.AddMinutes(-5))], places, selection: null));
        Assert.Null(ChipOf("a", [Member("a", since: Now.AddMinutes(-5))], places, selection: null));
        Assert.Null(ChipOf("a", [Member("a", freshness: Freshness.Stale, lastUpdate: Now.AddMinutes(-42))], places, selection: null));
        Assert.Equal("Driving · 54 mph", ChipOf("a", [Member("a", isDriving: true, speedMps: 24.1, since: Now.AddMinutes(-5))], places, new EntityRef(EntityKind.Member, "a")));
    }

    [Theory]
    [InlineData(24.1, "Driving · 54 mph")]
    [InlineData(0.4, "Driving · 1 mph")]
    [InlineData(null, "Driving")]
    public void ASelectedDrivingMember_ShowsOnlyAReportedSpeed(double? speedMps, string expected)
    {
        // R-112: "a speed implied from two fixes is never shown"; the view model's SpeedMps is the reported one, and null reads plain "Driving".
        var member = Member("a", isDriving: true, speedMps: speedMps, placeId: "park");

        Assert.Equal(expected, ChipOf("a", [member], [Place("park", 200)], new EntityRef(EntityKind.Member, "a")));
    }

    [Theory]
    [InlineData(Freshness.Stale, 42, "Last seen 42 min ago")]
    [InlineData(Freshness.Offline, 42, "Last seen 42 min ago")]
    [InlineData(Freshness.Stale, 180, "Last seen 3 hr ago")]
    [InlineData(Freshness.Offline, 20, "Last seen 20 min ago")]
    public void ASelectedStaleOrOfflineMember_SaysWhenItWasLastSeen_WhateverItsPlaceOrDriving(Freshness freshness, int minutesAgo, string expected)
    {
        var member = Member("a", freshness: freshness, lastUpdate: Now.AddMinutes(-minutesAgo), isDriving: true, placeId: "park");

        Assert.Equal(expected, ChipOf("a", [member], [Place("park", 200)], new EntityRef(EntityKind.Member, "a")));
    }

    [Fact]
    public void ASelectedMember_WithoutTheFactItsChipNeeds_HasNoChip()
    {
        var places = new[] { Place("park", 200) };
        var select = new EntityRef(EntityKind.Member, "a");

        Assert.Null(ChipOf("a", [Member("a", placeId: "park", since: null)], places, select));                                      // at a place, arrival unknown
        Assert.Null(ChipOf("a", [Member("a", freshness: Freshness.Stale, lastUpdate: null)], places, select));                         // stale, never heard
        Assert.Null(ChipOf("a", [Member("a", placeId: null, since: Now.AddMinutes(-5))], places, select));                             // out and not driving
        Assert.Null(ChipOf("a", [Member("a", freshness: Freshness.NoFix, lat: null, lon: null)], places, select));                    // no fix, no pin
    }

    [Fact]
    public void ASelectedStaticMember_WithoutALabel_ReadsFixedPosition()
    {
        var prince = Member("p", kind: MemberKind.Static, freshness: Freshness.Static);

        Assert.Equal("Fixed position", ChipOf("p", [prince], [], new EntityRef(EntityKind.Member, "p")));
        Assert.Equal("Home · Highmeadow", ChipOf("p", [prince with { StaticLabel = "Home · Highmeadow" }], [], new EntityRef(EntityKind.Member, "p")));
    }

    [Fact]
    public void TheChipsRelativeTime_FollowsTheSessionsClockAndZone_AtADayOrMore()
    {
        // Past 24 hours the relative time is a weekday and a clock time, in the session's zone and never the browser's (01 section 8.3).
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");
        var seen = Now.AddHours(-30);
        var member = Member("a", freshness: Freshness.Offline, lastUpdate: seen);

        var chip = MapPayloadFactory.MemberChip(member, MemberStatus.Offline, isMe: true, new EntityRef(EntityKind.Member, "a"), Now, zone);

        Assert.Equal("Last seen " + TimeFormatter.Relative(seen, Now, zone), chip);
        Assert.NotEqual("Last seen " + TimeFormatter.Relative(seen, Now, TimeZoneInfo.Utc), chip);
    }

    // ---- the chip of a vehicle ---------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Demo_TheSelectedWagon_ReadsParked_AndOnlyTheSelectedVehicleHasAChip()
    {
        var wagon = new EntityRef(EntityKind.Vehicle, DemoCast.Wagon.Id);

        var selected = MapPayloadFactory.Vehicles(Demo.Vehicles, Demo.Places, 1, wagon, Now, TimeZoneInfo.Utc).Vehicles;
        var none = MapPayloadFactory.Vehicles(Demo.Vehicles, Demo.Places, 1, selection: null, Now, TimeZoneInfo.Utc).Vehicles;

        Assert.Equal("Parked", selected.Single(vehicle => vehicle.Id == DemoCast.Wagon.Id).Chip);
        Assert.Equal([DemoCast.Wagon.Id], selected.Where(vehicle => vehicle.Chip is not null).Select(vehicle => vehicle.Id));
        Assert.All(none, vehicle => Assert.Null(vehicle.Chip));
    }

    [Theory]
    [InlineData(Freshness.Stale, true, 27.7, 60, "Last heard 1 hr ago")]    // "never Driving, whatever the speed says"
    [InlineData(Freshness.Stale, false, null, 45, "Last heard 45 min ago")]
    [InlineData(Freshness.Fresh, true, 27.7165, 0, "Driving · 62 mph")]
    [InlineData(Freshness.Fresh, true, null, 0, "Driving")]
    [InlineData(Freshness.Fresh, false, null, 0, "Parked")]
    public void ASelectedVehicle_HasTheChipOfTheTable(Freshness freshness, bool isMoving, double? speedMps, int minutesAgo, string expected)
    {
        var vehicle = Vehicle("v", freshness: freshness, isMoving: isMoving, speedMps: speedMps, lastUpdate: Now.AddMinutes(-minutesAgo));

        var item = Assert.Single(MapPayloadFactory.Vehicles([vehicle], [], 1, new EntityRef(EntityKind.Vehicle, "v"), Now, TimeZoneInfo.Utc).Vehicles);

        Assert.Equal(expected, item.Chip);
    }

    [Fact]
    public void AVehicleWithoutAPin_OrAStaleOneNeverHeardFrom_HasNoChip()
    {
        var noFix = Vehicle("n", freshness: Freshness.NoFix, lat: null, lon: null);
        var stale = Vehicle("s", freshness: Freshness.Stale, lastUpdate: null);

        Assert.Null(MapPayloadFactory.VehicleChip(noFix, Now, TimeZoneInfo.Utc));
        Assert.Null(MapPayloadFactory.VehicleChip(stale, Now, TimeZoneInfo.Utc));
    }

    [Fact]
    public void AMemberSelectionDoesNotGiveAVehicleAChip_AndAVehicleSelectionDoesNotGiveAMemberOne()
    {
        var vehicle = Vehicle("v");

        var vehicles = MapPayloadFactory.Vehicles([vehicle], [], 1, new EntityRef(EntityKind.Member, "v"), Now, TimeZoneInfo.Utc).Vehicles;

        Assert.Null(Assert.Single(vehicles).Chip);
    }

    // One member's chip through the whole payload: "me" is the first id, the selection is as given.
    private static string? ChipOf(string id, IReadOnlyList<MemberVm> members, IReadOnlyList<PlaceVm> places, EntityRef? selection) =>
        MapPayloadFactory.Members(members, places, members[0].Id, Now, Options, 1, selection).Members.Single(item => item.Id == id).Chip;

    // ---- the static pin, avatars and strings ----------------------------------------------------------------------------------------------

    [Fact]
    public void Demo_TheStaticPin_IsADashedHomePinWithAPositionAndNoLowBattery()
    {
        var prince = DemoMembers()[DemoCast.Prince.Id];

        Assert.True(prince.IsStatic);
        Assert.Equal(MemberStatus.Static, prince.Status);
        Assert.NotNull(prince.Lat);
        Assert.NotNull(prince.Lon);
        Assert.False(prince.LowBattery);
        Assert.False(prince.DrivingFresh);
        // The pin name is the accessible name of 01 section 10.3, which reads the middle dots as commas.
        Assert.Contains(DemoCast.Prince.StaticLabel!.Replace(" · ", ", ", StringComparison.Ordinal), prince.AriaLabel);
    }

    [Fact]
    public void Demo_EverythingElseAboutAMember_ComesFromTheViewModel()
    {
        var members = DemoMembers();
        var king = members[DemoCast.King.Id];

        Assert.Equal(DemoCast.King.Name, king.Name);
        Assert.Equal("A", king.Initial);
        Assert.Equal(DemoCast.King.Color, king.Color);
        Assert.Null(king.AvatarUrl);
        Assert.True(king.IsMe);
        Assert.Equal($"{DemoCast.King.Name} · {DemoCast.King.Lore}", king.Tooltip);
        Assert.Equal(DemoCast.Queen.Color, members[DemoCast.Queen.Id].Color);
    }

    [Fact]
    public void Avatar_IsPassedThroughAsTheRelativeUrl_AndEmptyMeansInitials()
    {
        var withPhoto = Assert.Single(Members([Member("king", avatarUrl: "avatars/king")], [], meId: "king").Members);
        var empty = Assert.Single(Members([Member("king", avatarUrl: string.Empty)], [], meId: "king").Members);

        Assert.Equal("avatars/king", withPhoto.AvatarUrl);
        Assert.Null(empty.AvatarUrl);
    }

    [Fact]
    public void Demo_PinNames_FollowTheAccessibleNameOf01Section10_3()
    {
        var members = DemoMembers();
        var home = Demo.Places.Single(place => place.Id == DemoPlaces.Home.Id).DisplayName;
        var hall = Demo.Places.Single(place => place.Id == DemoPlaces.JesterHall.Id).DisplayName;

        // The time part ("since 9:06 pm") and the distance ("1.0 mile away") come from the same formatter as the row, so the parts that depend on the clock are matched loosely here and
        // exactly by the E2E test of AC-46.
        var king = members[DemoCast.King.Id].AriaLabel;
        Assert.StartsWith($"{DemoCast.King.Name}, {DemoCast.King.Lore}. At {home}", king);
        Assert.Contains(" Battery 19 percent, charging.", king);
        var jester = members[DemoCast.Jester.Id].AriaLabel;
        Assert.StartsWith($"{DemoCast.Jester.Name}, {DemoCast.Jester.Lore}. At {hall} since ", jester);
        Assert.Contains(" Battery 12 percent, low.", jester);
        Assert.EndsWith(" away.", jester);
        var queen = members[DemoCast.Queen.Id].AriaLabel;
        Assert.StartsWith($"{DemoCast.Queen.Name}, {DemoCast.Queen.Lore}. Driving, ", queen);   // "Driving, 54 mph on I-65": the speed and the street, the dot read as a comma
        Assert.Contains(" on I-65", queen);
    }

    [Fact]
    public void Demo_BubbleNames_GiveTheDistanceAndTheCompassWordFromMe()
    {
        // 01 section 10.3 and 02 section 9.3: Dara is 155 miles east of the king, Elio 681 miles north-west.
        var members = DemoMembers();

        Assert.Equal($"{DemoCast.Cryptid.Name}, 155 miles east, off screen. Double tap to include on the map.", members[DemoCast.Cryptid.Id].BubbleLabel);
        Assert.Equal($"{DemoCast.Cryptid.Name} · 155 mi east · tap to include on the map", members[DemoCast.Cryptid.Id].BubbleTooltip);
        Assert.Equal($"{DemoCast.Prince.Name}, 681 miles north-west, off screen. Double tap to include on the map.", members[DemoCast.Prince.Id].BubbleLabel);
        Assert.Equal($"{DemoCast.King.Name}, off screen. Double tap to include on the map.", members[DemoCast.King.Id].BubbleLabel);
    }

    [Fact]
    public void Demo_BubbleStrings_AreTheBubbleTextFormatters()
    {
        // The payload carries the strings of BubbleTextFormatter (S9b): the same legs as the row above, 155 miles east and 681 miles north-west.
        var members = DemoMembers();

        Assert.Equal(BubbleTextFormatter.Label(DemoCast.Cryptid.Name, (249_790, 90)), members[DemoCast.Cryptid.Id].BubbleLabel);
        Assert.Equal(BubbleTextFormatter.Tooltip(DemoCast.Cryptid.Name, (249_790, 90)), members[DemoCast.Cryptid.Id].BubbleTooltip);
        Assert.Equal(BubbleTextFormatter.Label(DemoCast.Prince.Name, (1_095_947, 315)), members[DemoCast.Prince.Id].BubbleLabel);
        Assert.Equal(BubbleTextFormatter.Tooltip(DemoCast.Prince.Name, (1_095_947, 315)), members[DemoCast.Prince.Id].BubbleTooltip);
        Assert.Equal(BubbleTextFormatter.Label(DemoCast.King.Name, null), members[DemoCast.King.Id].BubbleLabel);
        Assert.Equal(BubbleTextFormatter.Tooltip(DemoCast.King.Name, null), members[DemoCast.King.Id].BubbleTooltip);
    }

    [Fact]
    public void Demo_BubbleFacts_AreInThePayload_ForTheScriptThatCannotKnowThem()
    {
        // 03 section 4.6: C# contributes the facts layoutBubbles cannot know: who is far away, who is me, who is the static member with the Home badge.
        var members = DemoMembers();

        Assert.Equal([DemoCast.Cryptid.Id, DemoCast.Prince.Id], members.Values.Where(item => item.Far).Select(item => item.Id).Order(StringComparer.Ordinal));
        Assert.True(members[DemoCast.King.Id].IsMe);
        Assert.Equal([DemoCast.King.Id], members.Values.Where(item => item.IsMe).Select(item => item.Id));
        Assert.True(members[DemoCast.Prince.Id].IsStatic);
        Assert.Equal(PinBadge.Home, members[DemoCast.Prince.Id].Badge);
        Assert.All(members.Values, item => Assert.Contains("off screen", item.BubbleLabel, StringComparison.Ordinal));
        Assert.All(members.Values, item => Assert.EndsWith("tap to include on the map", item.BubbleTooltip, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0, "0 feet", "0 ft")]
    [InlineData(100, "330 feet", "330 ft")]
    [InlineData(160, "520 feet", "520 ft")]
    [InlineData(1609.344, "1.0 mile", "1.0 mi")]
    [InlineData(1700, "1.1 miles", "1.1 mi")]
    [InlineData(15_000, "9.3 miles", "9.3 mi")]
    [InlineData(16_093.44, "10 miles", "10 mi")]
    [InlineData(249_790, "155 miles", "155 mi")]
    public void DistanceText_FollowsTheRulesOf01Section8_3(double meters, string words, string brief)
    {
        // The bubble text of a member the given distance north of me.
        var degrees = meters / (Geo.EarthRadiusM * Math.PI / 180);
        var far = Member("b", lat: MeLat + degrees, lon: MeLon);

        var payload = Members([Member("a", lat: MeLat, lon: MeLon), far], [], meId: "a");

        Assert.Equal($"Pat, {words} north, off screen. Double tap to include on the map.", payload.Members[1].BubbleLabel);
        Assert.Equal($"Pat · {brief} north · tap to include on the map", payload.Members[1].BubbleTooltip);
    }

    [Theory]
    [InlineData(0, 1, "north")]
    [InlineData(1, 1, "north-east")]
    [InlineData(1, 0, "east")]
    [InlineData(1, -1, "south-east")]
    [InlineData(0, -1, "south")]
    [InlineData(-1, -1, "south-west")]
    [InlineData(-1, 0, "west")]
    [InlineData(-1, 1, "north-west")]
    public void CompassWord_IsTheNearestOfEightDirections(int east, int north, string word)
    {
        var other = Member("b", lat: MeLat + (north * 0.05), lon: MeLon + (east * 0.05));

        var payload = Members([Member("a", lat: MeLat, lon: MeLon), other], [], meId: "a");

        Assert.Contains($", {word}, off screen", payload.Members[1].BubbleLabel.Replace(" miles ", ", ").Replace(" mile ", ", "));
    }

    // ---- vehicles -----------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Demo_TheWagonIsAParkedPickup()
    {
        var payload = MapPayloadFactory.Vehicles(Demo.Vehicles, Demo.Places, 1);
        var wagon = payload.Vehicles.Single(vehicle => vehicle.Id == DemoCast.Wagon.Id);

        Assert.Equal(MapGlyph.Pickup, wagon.Glyph);
        Assert.Equal(DemoCast.Wagon.Name, wagon.Name);
        Assert.NotNull(wagon.Lat);
        Assert.NotNull(wagon.Lon);
        Assert.Equal(new Ring(MapPalette.RingParked, Dashed: false, 3), wagon.Ring);
        Assert.False(wagon.Stale);
        Assert.Null(wagon.Chip);
        Assert.Equal($"{DemoCast.Wagon.Name}, {DemoCast.Wagon.Lore}. At {Demo.Places.Single(p => p.Id == DemoPlaces.Home.Id).DisplayName}.", wagon.AriaLabel);
    }

    [Theory]
    [InlineData(Freshness.Fresh, true, MapPalette.RingDriving, false, false)]
    [InlineData(Freshness.Fresh, false, MapPalette.RingParked, false, false)]
    [InlineData(Freshness.Stale, true, MapPalette.RingStale, true, true)]
    [InlineData(Freshness.Stale, false, MapPalette.RingStale, true, true)]
    public void VehicleRing_ChoosesFreshnessFirstAndMovementSecond(Freshness freshness, bool isMoving, string color, bool dashed, bool stale)
    {
        var vehicle = Vehicle("v", freshness: freshness, isMoving: isMoving);

        var item = Assert.Single(MapPayloadFactory.Vehicles([vehicle], [], 1).Vehicles);

        Assert.Equal(new Ring(color, dashed, 3), item.Ring);
        Assert.Equal(stale, item.Stale);
        Assert.NotNull(item.Lat);
    }

    [Fact]
    public void AVehicleWithoutAFix_HasNoCoordinates()
    {
        var noFix = Vehicle("v", freshness: Freshness.NoFix, lat: null, lon: null);

        var item = Assert.Single(MapPayloadFactory.Vehicles([noFix], [], 1).Vehicles);

        Assert.Null(item.Lat);
        Assert.Null(item.Lon);
    }

    // ---- zones ----------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Zones_LeaveOutAnythingAboveFiveKilometres_AndTheArrivalZone()
    {
        var places = new[]
        {
            Place("small", 100), Place("edge", 5_000), Place("just_over", 5_000.5), Place("approach", 32_187), Place("zero", 0),
        };

        var payload = MapPayloadFactory.Zones(places, show: true, Options, 1);

        Assert.Equal(["small", "edge"], payload.Zones.Select(zone => zone.Id));
    }

    [Fact]
    public void Zones_UseTheConfiguredMaximum()
    {
        var places = new[] { Place("a", 800), Place("b", 1_500) };

        var payload = MapPayloadFactory.Zones(places, show: true, Options with { MaxZoneRadiusKm = 1 }, 1);

        Assert.Equal(["a"], payload.Zones.Select(zone => zone.Id));
    }

    [Fact]
    public void Demo_Zones_AreTheFourteenDrawnOnes_TwoOfThemOccupied()
    {
        var payload = MapPayloadFactory.Zones(Demo.Places, show: true, Options, 7);

        Assert.Equal(7, payload.Version);
        Assert.True(payload.Show);
        Assert.Equal(14, payload.Zones.Count);
        Assert.Equal([DemoPlaces.Home.Id, DemoPlaces.JesterHall.Id], payload.Zones.Where(zone => zone.Occupied).Select(zone => zone.Id));
        Assert.DoesNotContain(payload.Zones, zone => zone.Id == DemoPlaces.Approach.Id);
        var home = payload.Zones.Single(zone => zone.Id == DemoPlaces.Home.Id);
        Assert.Equal(new ZoneItem(DemoPlaces.Home.Id, DemoPlaces.Home.Name, DemoPlaces.Home.Lat, DemoPlaces.Home.Lon, DemoPlaces.Home.RadiusM, Occupied: true), home);
    }

    // R2-01. 01 section 5.3, "What counts as here (R2-009)": "A place is occupied when at least one person is inside it (PlaceVm.MemberIdsInside, which includes stale members,
    // 02 §4.5); a vehicle never makes a place occupied. The row's "n here", the mini avatars, the occupied-first sort, the zone's occupied fill (4.6) and the "{m} occupied"
    // summary (8.2) all count people only". The zone's `occupied` flag is that fill, so only a person sets it.
    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(0, 1, false)]     // only the wagon: not occupied
    [InlineData(0, 2, false)]     // the wagon and the chariot: still not occupied
    [InlineData(1, 0, true)]
    [InlineData(1, 1, true)]      // a member and the wagon: occupied
    [InlineData(2, 2, true)]
    public void AZoneIsOccupiedWhenAPersonIsInside_WhateverVehiclesAreToo(int people, int vehicles, bool occupied)
    {
        var place = Demo.Places.Single(candidate => candidate.Id == DemoPlaces.Work.Id) with
        {
            MemberIdsInside = [.. DemoCast.AllMembers.Take(people).Select(member => member.Id)],
            VehicleIdsInside = [.. DemoCast.AllVehicles.Take(vehicles).Select(vehicle => vehicle.Id)],
        };

        var zone = Assert.Single(MapPayloadFactory.Zones([place], show: true, Options, 1).Zones);

        Assert.Equal(occupied, zone.Occupied);
    }

    [Fact]
    public void Demo_TheWagonAloneAtHearthHaven_LeavesItsZoneEmpty_AndOnlyTheJestersHallOccupied()
    {
        // The Demo's Hearth Haven holds Alden and the pickup; take Alden away and only the pickup is left inside.
        var places = Demo.Places.Select(place => place.Id == DemoPlaces.Home.Id ? place with { MemberIdsInside = [] } : place).ToList();

        var payload = MapPayloadFactory.Zones(places, show: true, Options, 1);

        Assert.Equal(DemoCast.Wagon.Id, Assert.Single(places.Single(place => place.Id == DemoPlaces.Home.Id).VehicleIdsInside));
        Assert.False(payload.Zones.Single(zone => zone.Id == DemoPlaces.Home.Id).Occupied);
        Assert.Equal([DemoPlaces.JesterHall.Id], payload.Zones.Where(zone => zone.Occupied).Select(zone => zone.Id));
    }

    // 01 section 5.3 names no exception for a static member: "at least one person ... (PlaceVm.MemberIdsInside ...)", and 02 §4.5 lists under a zone every member whose PlaceId is that
    // zone, the static prince included; only the Drivers summary leaves him out (8.2). A zone that lists the prince is occupied, as its list row ("1 here") says.
    [Fact]
    public void AZoneThatListsAStaticMemberIsOccupied()
    {
        var place = Demo.Places.Single(candidate => candidate.Id == DemoPlaces.Work.Id) with { MemberIdsInside = [DemoCast.Prince.Id], VehicleIdsInside = [] };

        var zone = Assert.Single(MapPayloadFactory.Zones([place], show: true, Options, 1).Zones);

        Assert.Contains(Demo.Members, member => member.Id == DemoCast.Prince.Id && member.Kind == MemberKind.Static);
        Assert.True(zone.Occupied);
    }

    [Fact]
    public void Zones_CarryTheThreeAppearancesOf01Section4_6_AndTheSwitch()
    {
        var payload = MapPayloadFactory.Zones([], show: false, Options, 1);

        Assert.False(payload.Show);
        Assert.Equal(new ZoneAppearance(MapPalette.ZoneDark.LineColor, 0.10, 0.22, Casing: false), payload.Appearances.Dark);
        Assert.Equal(new ZoneAppearance("#8A5F00", 0.14, 0.22, Casing: false), payload.Appearances.Light);
        Assert.Equal(new ZoneAppearance(MapPalette.ZoneDark.LineColor, 0.10, 0.22, Casing: true), payload.Appearances.Imagery);
    }

    // ---- the default view -------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Demo_DefaultView_FramesMeTheJesterTheQueenAndTheWagon_NotTheTwoFarMembers()
    {
        var targets = Targets(Demo.Members, Demo.Vehicles, Demo.Places, DemoCast.King.Id);

        var (west, south) = (targets.Default.Bounds[0][0], targets.Default.Bounds[0][1]);
        var (east, north) = (targets.Default.Bounds[1][0], targets.Default.Bounds[1][1]);
        var king = Demo.Members.Single(member => member.Id == DemoCast.King.Id);
        var queen = Demo.Members.Single(member => member.Id == DemoCast.Queen.Id);
        var jester = Demo.Members.Single(member => member.Id == DemoCast.Jester.Id);

        // The tight box: the queen is the westernmost and the southernmost, the jester the northernmost, the king the easternmost.
        Assert.Equal(queen.Lon, west);
        Assert.Equal(queen.Lat, south);
        Assert.Equal(king.Lon, east);
        Assert.Equal(jester.Lat, north);
        Assert.Equal(16, targets.Default.MaxZoom);
        Assert.Equal(16, targets.Me!.Zoom);
        Assert.Equal([king.Lon!.Value, king.Lat!.Value], targets.Me.Center);
    }

    [Fact]
    public void DefaultView_FallsBackToTheNearestLiveMember_WhenOnlyMeQualifies()
    {
        // R-014: the only member inside the radius is me; the nearest live member, 100 km away, widens the fit (the static one never does).
        var me = Member("a", lat: MeLat, lon: MeLon);
        var nearest = Member("b", lat: MeLat + 0.9, lon: MeLon);
        var farther = Member("c", lat: MeLat + 3, lon: MeLon);
        var prince = Member("p", kind: MemberKind.Static, freshness: Freshness.Static, lat: MeLat + 0.5, lon: MeLon);

        var targets = Targets([me, farther, nearest, prince], [], [], "a");

        Assert.Equal(MeLat, targets.Default.Bounds[0][1]);
        Assert.Equal(MeLat + 0.9, targets.Default.Bounds[1][1]);
    }

    [Fact]
    public void DefaultView_WithNoOtherLiveMember_IsMeAlone_GrownToTheMinimumDiagonal()
    {
        var targets = Targets([Member("a", lat: MeLat, lon: MeLon), Member("p", kind: MemberKind.Static, freshness: Freshness.Static, lat: 38.8, lon: -92.8)], [], [], "a");

        AssertDiagonalAtLeast800(targets);
        AssertCentredOn(targets, MeLat, MeLon);
        Assert.Equal([MeLon, MeLat], targets.Me!.Center);
    }

    [Fact]
    public void DefaultView_GrowsASmallBoxSymmetrically_ToAnEightHundredMetreDiagonal()
    {
        // Two members 111 m apart: a diagonal of 111 m before growing.
        var targets = Targets([Member("a", lat: MeLat, lon: MeLon), Member("b", lat: MeLat + Step, lon: MeLon)], [], [], "a");

        AssertDiagonalAtLeast800(targets);
        AssertCentredOn(targets, MeLat + (Step / 2), MeLon);
    }

    [Fact]
    public void DefaultView_LeavesABigBoxAlone()
    {
        var targets = Targets([Member("a", lat: MeLat, lon: MeLon), Member("b", lat: MeLat + 0.1, lon: MeLon + 0.1)], [], [], "a");

        Assert.Equal(MeLat, targets.Default.Bounds[0][1]);
        Assert.Equal(MeLon + 0.1, targets.Default.Bounds[1][0]);
    }

    [Fact]
    public void DefaultView_CountsAStaticMemberOnlyInsideTheRadius_AndIgnoresOfflineAndNoFixMembers()
    {
        var me = Member("a", lat: MeLat, lon: MeLon);
        var nearStatic = Member("p", kind: MemberKind.Static, freshness: Freshness.Static, lat: MeLat + 0.1, lon: MeLon);
        var offline = Member("o", freshness: Freshness.Offline, lat: MeLat - 0.1, lon: MeLon);
        var noFix = Member("n", freshness: Freshness.NoFix, lat: null, lon: null);

        var targets = Targets([me, nearStatic, offline, noFix], [], [], "a");

        // The static pin 11 km away counts; the offline one 11 km south does not.
        Assert.Equal(MeLat, targets.Default.Bounds[0][1]);
        Assert.Equal(MeLat + 0.1, targets.Default.Bounds[1][1]);
    }

    [Fact]
    public void DefaultView_CountsVehiclesWithAPosition_NotOneWithoutAFix()
    {
        var me = Member("a", lat: MeLat, lon: MeLon);
        var stale = Vehicle("v", freshness: Freshness.Stale, lat: MeLat + 0.05, lon: MeLon);
        var noFix = Vehicle("x", freshness: Freshness.NoFix, lat: MeLat - 0.06, lon: MeLon);

        var targets = Targets([me], [stale, noFix], [], "a");

        Assert.Equal(MeLat, targets.Default.Bounds[0][1]);
        Assert.Equal(MeLat + 0.05, targets.Default.Bounds[1][1]);
    }

    [Fact]
    public void DefaultView_UsesTheConfiguredRadius_AndAllIncludesEveryone()
    {
        var me = Member("a", lat: MeLat, lon: MeLon);
        var inside = Member("b", lat: MeLat + 0.1, lon: MeLon);
        var outside = Member("c", lat: MeLat + 0.4, lon: MeLon);

        var narrow = Targets([me, inside, outside], [], [], "a", Options with { DefaultViewRadiusKm = 20 });
        var all = Targets([me, inside, outside], [], [], "a", Options with { DefaultViewRadiusKm = double.PositiveInfinity });

        Assert.Equal(MeLat + 0.1, narrow.Default.Bounds[1][1]);
        Assert.Equal(MeLat + 0.4, all.Default.Bounds[1][1]);
    }

    [Fact]
    public void DefaultView_WhenIHaveNoFix_UsesTheHomeZoneAsMe()
    {
        var me = Member("a", freshness: Freshness.NoFix, lat: null, lon: null);
        var home = new PlaceVm("home", "Home", string.Empty, PlaceKind.Home, 31.2, -85.2, 100, [], []);

        var targets = Targets([me], [], [home], "a");

        Assert.Equal([-85.2, 31.2], targets.Me!.Center);
        AssertCentredOn(targets, 31.2, -85.2);
    }

    [Fact]
    public void DefaultView_WithNoPositionForMeAndNoHome_IsNull()
    {
        var me = Member("a", freshness: Freshness.NoFix, lat: null, lon: null);

        Assert.Null(MapPayloadFactory.Targets([me], [], [Place("park", 100)], "a", Options, 1));
        Assert.Null(MapPayloadFactory.Targets([], [], [], null, Options, 1));
    }

    // ---- layout, versions, determinism ---------------------------------------------------------------------------------------------------------

    [Fact]
    public void Layout_Compact_HasNoPanelAndTheStackFollowsTheSheet()
    {
        var safe = new Padding(10, 0, 20, 0);

        var layout = MapPayloadFactory.Layout(MapLayout.Compact with { StackVisible = false, Safe = safe });

        Assert.Equal(new LayoutPayload(MapLayoutMode.Compact, 16, 0, PanelHidden: false, StackVisible: false, safe, 64), layout);
    }

    [Fact]
    public void Layout_Expanded_HasA400PixelPanel_ZeroWhenHidden()
    {
        var shown = MapPayloadFactory.Layout(MapLayout.Expanded);
        var hidden = MapPayloadFactory.Layout(MapLayout.Expanded with { PanelHidden = true });

        Assert.Equal(new LayoutPayload(MapLayoutMode.Expanded, 16, 400, PanelHidden: false, StackVisible: true, new Padding(0, 0, 0, 0), 64), shown);
        Assert.Equal(0, hidden.PanelWidthPx);
        Assert.True(hidden.PanelHidden);
    }

    [Fact]
    public void EveryPayload_CarriesTheVersionItWasGiven()
    {
        Assert.Equal(3, MapPayloadFactory.Members(Demo.Members, Demo.Places, null, Now, Options, 3).Version);
        Assert.Equal(4, MapPayloadFactory.Vehicles(Demo.Vehicles, Demo.Places, 4).Version);
        Assert.Equal(5, MapPayloadFactory.Zones(Demo.Places, true, Options, 5).Version);
        Assert.Equal(6, MapPayloadFactory.Targets(Demo.Members, Demo.Vehicles, Demo.Places, null, Options, 6)!.Version);
    }

    [Fact]
    public void Payloads_AreDeterministic()
    {
        var first = System.Text.Json.JsonSerializer.Serialize(MapPayloadFactory.Members(Demo.Members, Demo.Places, null, Now, Options, 1), MapJson.Options);
        var second = System.Text.Json.JsonSerializer.Serialize(MapPayloadFactory.Members(Demo.Members, Demo.Places, null, Now, Options, 1), MapJson.Options);

        Assert.Equal(first, second);
    }

    // ---- helpers ---------------------------------------------------------------------------------------------------------------------------------

    private static RealmSnapshot LoadDemo() => FullCast.Session(null).Current;

    private static MembersPayload Members(IReadOnlyList<MemberVm> members, IReadOnlyList<PlaceVm> places, string? meId) =>
        MapPayloadFactory.Members(members, places, meId, Now, Options, 1);

    private static Dictionary<string, MemberPayloadItem> DemoMembers() =>
        Members(Demo.Members, Demo.Places, meId: null).Members.ToDictionary(member => member.Id);

    private static Dictionary<string, MemberPayloadItem> DemoMembersSelecting(EntityRef selection) =>
        MapPayloadFactory.Members(Demo.Members, Demo.Places, null, Now, Options, 1, selection).Members.ToDictionary(member => member.Id);

    private static DefaultTargets Targets(IReadOnlyList<MemberVm> members, IReadOnlyList<VehicleVm> vehicles, IReadOnlyList<PlaceVm> places, string? meId, MapPayloadOptions? options = null) =>
        MapPayloadFactory.Targets(members, vehicles, places, meId, options ?? Options, 1) ?? throw new InvalidOperationException("The targets were null.");

    private static void AssertPin(MemberPayloadItem item, MemberStatus status, string color, bool dashed, PinBadge? badge, int zClass)
    {
        Assert.Equal(status, item.Status);
        Assert.Equal(color, item.Ring.Color);
        Assert.Equal(dashed, item.Ring.Dashed);
        Assert.Equal(badge, item.Badge);
        Assert.Equal(zClass, item.ZClass);
    }

    private static void AssertDiagonalAtLeast800(DefaultTargets targets)
    {
        var bounds = targets.Default.Bounds;
        Assert.True(Geo.DistanceM(bounds[0][1], bounds[0][0], bounds[1][1], bounds[1][0]) >= 800);
    }

    // The box is grown by the same distance on every side, so its middle stays where the tight box's middle was.
    private static void AssertCentredOn(DefaultTargets targets, double lat, double lon)
    {
        var bounds = targets.Default.Bounds;
        Assert.Equal(lon, (bounds[0][0] + bounds[1][0]) / 2, 9);
        Assert.Equal(lat, (bounds[0][1] + bounds[1][1]) / 2, 9);
    }

    private static PlaceVm Place(string id, double radiusM, PlaceKind kind = PlaceKind.Other) =>
        new(id, "Place " + id, string.Empty, kind, MeLat + 0.5, MeLon, radiusM, [], []);

    private static MemberVm Member(
        string id,
        Freshness freshness = Freshness.Fresh,
        MemberKind kind = MemberKind.Live,
        double? lat = MeLat,
        double? lon = MeLon,
        double? accuracyM = 10,
        int? battery = 50,
        bool isDriving = false,
        string? placeId = null,
        DateTimeOffset? since = null,
        string? avatarUrl = null,
        int sortOrder = 0,
        double? speedMps = null,
        DateTimeOffset? lastUpdate = null) =>
        new(
            Id: id,
            DisplayName: "Pat",
            LoreTitle: null,
            AvatarUrl: avatarUrl,
            Color: "#445566",
            Kind: kind,
            Lat: lat,
            Lon: lon,
            AccuracyM: accuracyM,
            BatteryPct: battery,
            Charging: null,
            BatteryAsOfUtc: null,
            IsDriving: isDriving,
            SpeedMps: speedMps,
            Street: null,
            City: null,
            Region: null,
            FullAddress: null,
            PlaceId: placeId,
            SinceUtc: since,
            LastUpdateUtc: lastUpdate,
            SortOrder: sortOrder,
            Freshness: freshness,
            StaticLabel: null);

    private static VehicleVm Vehicle(
        string id,
        Freshness freshness = Freshness.Fresh,
        bool isMoving = false,
        double? lat = MeLat,
        double? lon = MeLon,
        double? speedMps = null,
        DateTimeOffset? lastUpdate = null) =>
        new(
            Id: id,
            Name: "Cart",
            LoreTitle: null,
            Glyph: VehicleGlyph.Car,
            Lat: lat,
            Lon: lon,
            Street: null,
            PlaceId: null,
            LastUpdateUtc: lastUpdate,
            SpeedMps: speedMps,
            IsMoving: isMoving,
            Freshness: freshness);
}
