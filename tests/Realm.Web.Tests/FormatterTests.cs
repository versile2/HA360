using System.Globalization;
using Realm.Demo;
using Realm.Domain;
using Realm.Web.Formatting;
using Realm.Web.Map;
using Realm.Web.Sheet;
using Xunit;

namespace Realm.Web.Tests;

/// <summary>
/// The handle-summary line of 01 section 8.2 (<see cref="HandleSummaryFormatter"/>): the rows of the spec built from the Demo cast, then the rules one at a time on
/// small hand-built view models (the 24 hour window, "driving" needing a fresh fix, the singular forms). S7b adds the row formatters: <see cref="TimeFormatter"/>,
/// <see cref="UnitFormatter"/>, <see cref="MemberTextFormatter"/> and the vehicle and place strings, with the rows of 01 section 8 built from <see cref="DemoCast"/> and
/// <see cref="DemoPlaces"/> through <see cref="VmFactory"/> (the names, lore titles and streets are read from the cast, never retyped; only the clock
/// times and distances, which the spec states and the cast does not carry as text, are written out).
/// </summary>
public sealed class FormatterTests
{
    private static readonly DateTimeOffset Now = DemoDataSource.Anchor;

    // ---- the rows of 01 section 8.2, from the Demo cast (DemoCast: Alden, Briar, Cass, Dara, Elio; the wagon and the chariot; 14 places) -------------

    [Fact(DisplayName = "[AC-05] the handle summary of the Demo cast reads \"4 in the Realm · 1 driving\"")]
    public void Drivers_FromTheDemoCast_ReadsFourInTheRealmOneDriving()
    {
        Assert.Equal("4 in the Realm · 1 driving", HandleSummaryFormatter.Drivers(Demo.Members, DemoNow));
    }

    [Fact]
    public void Vehicles_FromTheDemoCast_ReadsTwoVehiclesAllParked()
    {
        Assert.Equal("2 vehicles · all parked", HandleSummaryFormatter.Vehicles(Demo.Vehicles));
    }

    [Fact]
    public void Places_FromTheDemoCast_ReadsFourteenPlacesTwoOccupied()
    {
        Assert.Equal("14 places · 2 occupied", HandleSummaryFormatter.Places(Demo.Places));
    }

    [Theory]
    [InlineData(Section.Drivers, "4 in the Realm · 1 driving")]
    [InlineData(Section.Vehicles, "2 vehicles · all parked")]
    [InlineData(Section.Places, "14 places · 2 occupied")]
    public void Format_PicksTheSummaryOfTheSectionThatShows(Section section, string expected)
    {
        Assert.Equal(expected, HandleSummaryFormatter.Format(section, Demo.Members, Demo.Vehicles, Demo.Places, DemoNow));
    }

    [Fact]
    public void Drivers_TheDemoCastIsTheOneThatTheSpecDescribes()
    {
        var members = Demo.Members;

        // Briar drives; Elio is the static prince and is never "in the Realm"; Dara's fix is stale but still inside the 24 hours.
        Assert.Contains(members, member => member.Id == DemoCast.Queen.Id && member.IsDriving && member.Freshness == Freshness.Fresh);
        Assert.Contains(members, member => member.Id == DemoCast.Prince.Id && member.Kind == MemberKind.Static);
        Assert.Contains(members, member => member.Id == DemoCast.Cryptid.Id && member.Freshness == Freshness.Stale);
    }

    // ---- before the first data -------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(Section.Drivers)]
    [InlineData(Section.Vehicles)]
    [InlineData(Section.Places)]
    public void Format_NoPeopleNoVehiclesNoPlaces_IsTheLoadingLine_InEverySection(Section section) =>
        Assert.Equal(HandleSummaryFormatter.Loading, HandleSummaryFormatter.Format(section, [], [], [], Now));

    [Fact]
    public void TheFixedLines_AreThoseOfTheCopyTable()
    {
        Assert.Equal("Summoning the court…", HandleSummaryFormatter.Loading);
        Assert.Equal("The Realm is empty", HandleSummaryFormatter.Empty);
        Assert.Equal(TimeSpan.FromHours(24), HandleSummaryFormatter.LiveWindow);
    }

    // ---- drivers ---------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Drivers_NobodyHasALiveFix_IsEmpty_OnceTheListsAreNotEmpty()
    {
        var members = new[] { Member("a", freshness: Freshness.NoFix, updated: null), Member("b", kind: MemberKind.Static, freshness: Freshness.Static) };

        Assert.Equal(HandleSummaryFormatter.Empty, HandleSummaryFormatter.Drivers(members, Now));
        Assert.Equal(HandleSummaryFormatter.Empty, HandleSummaryFormatter.Format(Section.Drivers, members, [], [], Now));
    }

    [Fact]
    public void Drivers_NoPeopleButAPlace_IsEmpty_NotLoading()
    {
        var places = new[] { Place("home", memberIds: [], vehicleIds: []) };

        Assert.Equal(HandleSummaryFormatter.Empty, HandleSummaryFormatter.Format(Section.Drivers, [], [], places, Now));
    }

    [Fact]
    public void Drivers_CountsLiveFixesOnly_AndDrivingNeedsAFreshFix()
    {
        var members = new[]
        {
            Member("fresh-driving", isDriving: true),
            Member("fresh-parked"),
            Member("stale-driving", freshness: Freshness.Stale, isDriving: true),   // a stale "driving" is not shown as driving
            Member("offline", freshness: Freshness.Offline),
            Member("static", kind: MemberKind.Static, freshness: Freshness.Static),
            Member("no-fix", freshness: Freshness.NoFix, updated: null),
        };

        Assert.Equal("4 in the Realm · 1 driving", HandleSummaryFormatter.Drivers(members, Now));
    }

    [Fact]
    public void Drivers_NobodyDriving_HasNoDrivingSuffix()
    {
        var members = new[] { Member("a"), Member("b"), Member("c") };

        Assert.Equal("3 in the Realm", HandleSummaryFormatter.Drivers(members, Now));
    }

    [Fact]
    public void Drivers_SeveralDriving_CountsThem()
    {
        var members = new[] { Member("a", isDriving: true), Member("b", isDriving: true), Member("c") };

        Assert.Equal("3 in the Realm · 2 driving", HandleSummaryFormatter.Drivers(members, Now));
    }

    [Fact]
    public void Drivers_AloneInTheRealm_StillReadsInTheRealm() =>
        Assert.Equal("1 in the Realm", HandleSummaryFormatter.Drivers([Member("a")], Now));

    [Fact]
    public void Drivers_TheLiveWindowIsTwentyFourHours_Inclusive()
    {
        var atTheEdge = Member("edge", updated: Now - TimeSpan.FromHours(24));
        var justOutside = Member("outside", updated: Now - TimeSpan.FromHours(24) - TimeSpan.FromSeconds(1));
        var recent = Member("recent", updated: Now - TimeSpan.FromMinutes(5));

        Assert.Equal("2 in the Realm", HandleSummaryFormatter.Drivers([atTheEdge, recent, justOutside], Now));
        Assert.Equal(HandleSummaryFormatter.Empty, HandleSummaryFormatter.Drivers([justOutside], Now));
    }

    [Fact]
    public void Drivers_TheClockMovesTheWindow_NotTheWallClock()
    {
        var member = Member("a", updated: Now - TimeSpan.FromHours(23));

        Assert.Equal("1 in the Realm", HandleSummaryFormatter.Drivers([member], Now));
        Assert.Equal("1 in the Realm", HandleSummaryFormatter.Drivers([member], Now + TimeSpan.FromHours(1)));
        Assert.Equal(HandleSummaryFormatter.Empty, HandleSummaryFormatter.Drivers([member], Now + TimeSpan.FromHours(1) + TimeSpan.FromMinutes(1)));
    }

    // ---- vehicles --------------------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(0, 2, "2 vehicles · all parked")]
    [InlineData(1, 2, "2 vehicles · 1 on the road")]
    [InlineData(2, 2, "2 vehicles · 2 on the road")]
    [InlineData(0, 1, "1 vehicle · all parked")]
    [InlineData(1, 1, "1 vehicle · 1 on the road")]
    [InlineData(0, 3, "3 vehicles · all parked")]
    public void Vehicles_CountsTheMovingOnes(int moving, int total, string expected)
    {
        var vehicles = Enumerable.Range(0, total).Select(index => Vehicle($"v{index}", isMoving: index < moving)).ToList();

        Assert.Equal(expected, HandleSummaryFormatter.Vehicles(vehicles));
    }

    [Fact]
    public void Vehicles_AVehicleWithNoData_IsParked()
    {
        var vehicles = new[] { Vehicle("wagon"), Vehicle("chariot", noFix: true) };

        Assert.Equal("2 vehicles · all parked", HandleSummaryFormatter.Vehicles(vehicles));
    }

    // ---- places ----------------------------------------------------------------------------------------------------------------------------

    // R2-01. 01 section 5.3, "What counts as here (R2-009)": "A place is occupied when at least one person is inside it (PlaceVm.MemberIdsInside, which includes stale members,
    // 02 §4.5); a vehicle never makes a place occupied. The row's "n here", the mini avatars, the occupied-first sort, the zone's occupied fill (4.6) and the "{m} occupied"
    // summary (8.2) all count people only". The rows below pin that rule, not the implementation.
    [Fact]
    public void Places_OccupiedMeansAPersonInside_AVehicleNeverMakesAPlaceOccupied()
    {
        var places = new[]
        {
            Place("a", memberIds: [DemoCast.King.Id], vehicleIds: []),
            Place("b", memberIds: [], vehicleIds: [DemoCast.Wagon.Id]),
            Place("c", memberIds: [DemoCast.Queen.Id, DemoCast.Jester.Id], vehicleIds: [DemoCast.Chariot.Id]),
            Place("d", memberIds: [], vehicleIds: []),
        };

        Assert.Equal("4 places · 2 occupied", HandleSummaryFormatter.Places(places));
    }

    [Theory]
    [InlineData(0, 0, "1 place · all quiet")]
    [InlineData(0, 1, "1 place · all quiet")]     // only the wagon: not occupied
    [InlineData(0, 2, "1 place · all quiet")]     // the wagon and the chariot: still not occupied
    [InlineData(1, 0, "1 place · 1 occupied")]
    [InlineData(1, 1, "1 place · 1 occupied")]    // a member and the wagon: occupied
    [InlineData(2, 2, "1 place · 1 occupied")]    // two people and two vehicles: still one occupied place
    public void Places_OnePlace_OccupiedWhenAPersonIsInside_WhateverVehiclesAreToo(int people, int vehicles, string expected)
    {
        var place = Place(
            "p",
            memberIds: [.. DemoCast.Members.Take(people).Select(member => member.Id)],
            vehicleIds: [.. DemoCast.AllVehicles.Take(vehicles).Select(vehicle => vehicle.Id)]);

        Assert.Equal(expected, HandleSummaryFormatter.Places([place]));
    }

    [Fact]
    public void Places_TheWagonAloneAtHearthHaven_DoesNotOccupyIt_AndItsRowSaysEmpty()
    {
        // The Demo's Hearth Haven holds Alden and the pickup; take Alden away and only the pickup is left inside. The summary, the Format entry and the row must agree.
        var places = Demo.Places.Select(place => place.Id == DemoPlaces.Home.Id ? place with { MemberIdsInside = [] } : place).ToList();
        var home = places.Single(place => place.Id == DemoPlaces.Home.Id);

        Assert.Equal(DemoCast.Wagon.Id, Assert.Single(home.VehicleIdsInside));
        Assert.Equal("14 places · 1 occupied", HandleSummaryFormatter.Places(places));
        Assert.Equal("14 places · 1 occupied", HandleSummaryFormatter.Format(Section.Places, Demo.Members, Demo.Vehicles, places, DemoNow));
        Assert.Equal(PlaceTextFormatter.Empty, VmFactory.Place(home, Facts()).CountText);
    }

    // 01 section 5.3 names no exception for a static member: the rule is "at least one person ... (PlaceVm.MemberIdsInside ...)", and 02 §4.5 lists under a zone every member whose
    // PlaceId is that zone, the static prince included (his PlaceId is the own-geometry result like anyone's, 02 section 1.5). Only the Drivers line leaves him out (8.2, "the static
    // prince is not counted"). So a place that lists the prince is occupied, exactly as its row reads "1 here": the summary follows MemberIdsInside, the data layer's call.
    [Fact]
    public void Places_AStaticMemberListedInsideAPlace_IsAPerson_SoThePlaceIsOccupiedAsItsRowSays()
    {
        var work = Demo.Places.Single(place => place.Id == DemoPlaces.Work.Id) with { MemberIdsInside = [DemoCast.Prince.Id], VehicleIdsInside = [] };
        var places = new[] { work };

        Assert.Contains(Demo.Members, member => member.Id == DemoCast.Prince.Id && member.Kind == MemberKind.Static);
        Assert.Equal("1 place · 1 occupied", HandleSummaryFormatter.Places(places));
        Assert.Equal("1 here", VmFactory.Place(work, RowFacts.Create(Demo.Members, places, null, DemoNow, Session.Zone)).CountText);
    }

    [Theory]
    [InlineData(1, "1 place · all quiet")]
    [InlineData(3, "3 places · all quiet")]
    public void Places_NobodyInside_IsAllQuiet(int count, string expected)
    {
        var places = Enumerable.Range(0, count).Select(index => Place($"p{index}", memberIds: [], vehicleIds: [])).ToList();

        Assert.Equal(expected, HandleSummaryFormatter.Places(places));
    }

    [Fact]
    public void Places_OneOccupiedPlace_ReadsSingular() =>
        Assert.Equal("1 place · 1 occupied", HandleSummaryFormatter.Places([Place("a", memberIds: ["king"], vehicleIds: [])]));

    // ---- the section is the only input that changes the line -------------------------------------------------------------------------------

    [Fact]
    public void Format_DoesNotDependOnSelectionOrSize_OnlyOnTheSectionAndTheData()
    {
        var members = new[] { Member("a", isDriving: true), Member("b") };
        var vehicles = new[] { Vehicle("v", isMoving: true) };
        var places = new[] { Place("p", memberIds: ["a"], vehicleIds: []) };

        Assert.Equal("2 in the Realm · 1 driving", HandleSummaryFormatter.Format(Section.Drivers, members, vehicles, places, Now));
        Assert.Equal("1 vehicle · 1 on the road", HandleSummaryFormatter.Format(Section.Vehicles, members, vehicles, places, Now));
        Assert.Equal("1 place · 1 occupied", HandleSummaryFormatter.Format(Section.Places, members, vehicles, places, Now));
    }

    // ---- time: the 12-hour clock, "Since" and relative strings, in the session's zone ----------------------------------------------------------

    [Theory]
    [InlineData("2026-10-01T02:24:00Z", "9:24 pm")]
    [InlineData("2026-10-01T05:05:00Z", "12:05 am")]
    [InlineData("2026-09-30T17:00:00Z", "12:00 pm")]
    [InlineData("2026-09-30T16:59:00Z", "11:59 am")]
    [InlineData("2026-09-30T14:05:00Z", "9:05 am")]
    public void Clock_IsTwelveHourLowercaseWithNoLeadingZero(string utc, string expected) =>
        Assert.Equal(expected, TimeFormatter.Clock(Instant(utc), Chicago));

    [Theory]
    [InlineData("2026-09-30T21:24:00-05:00", "9:24 pm")]                    // today
    [InlineData("2026-09-29T16:10:00-05:00", "yesterday 4:10 pm")]
    [InlineData("2026-09-28T16:10:00-05:00", "Mon 4:10 pm")]                // within six days
    [InlineData("2026-09-24T16:10:00-05:00", "Thu 4:10 pm")]                // the sixth day back
    [InlineData("2026-09-23T16:10:00-05:00", "Sep 23")]                     // the seventh: a date
    [InlineData("2026-09-12T08:00:00-05:00", "Sep 12")]
    public void When_IsTodayYesterdayAWeekdayOrADate(string instant, string expected) =>
        Assert.Equal(expected, TimeFormatter.When(Instant(instant), Now, Chicago));

    [Fact]
    public void Since_PrefixesTheTimeString()
    {
        Assert.Equal("Since 9:24 pm", TimeFormatter.Since(Instant("2026-09-30T21:24:00-05:00"), Now, Chicago));
        Assert.Equal("Since yesterday 4:10 pm", TimeFormatter.Since(Instant("2026-09-29T16:10:00-05:00"), Now, Chicago));
        Assert.Equal("Since Mon 4:10 pm", TimeFormatter.Since(Instant("2026-09-28T16:10:00-05:00"), Now, Chicago));
        Assert.Equal("Since Sep 12", TimeFormatter.Since(Instant("2026-09-12T08:00:00-05:00"), Now, Chicago));
    }

    [Fact]
    public void Since_YesterdayIsTheCalendarDayInTheZone_NotTwentyFourHours()
    {
        // 40 minutes before 12:30 am on Oct 1 in Chicago is still the evening of Sep 30: "yesterday".
        var now = Instant("2026-10-01T00:30:00-05:00");

        Assert.Equal("Since yesterday 11:50 pm", TimeFormatter.Since(Instant("2026-09-30T23:50:00-05:00"), now, Chicago));
        Assert.Equal("Since 12:10 am", TimeFormatter.Since(Instant("2026-10-01T00:10:00-05:00"), now, Chicago));
    }

    [Theory]
    [InlineData(0, "just now")]
    [InlineData(44, "just now")]
    [InlineData(45, "1 min ago")]
    [InlineData(60, "1 min ago")]
    [InlineData(42 * 60, "42 min ago")]
    [InlineData((59 * 60) + 59, "59 min ago")]
    [InlineData(60 * 60, "1 hr ago")]
    [InlineData(3 * 3600, "3 hr ago")]
    [InlineData((23 * 3600) + (59 * 60), "23 hr ago")]
    [InlineData(-30, "just now")]                                           // an instant from the future (clock skew)
    public void Relative_FollowsTheElapsedTime(int secondsAgo, string expected) =>
        Assert.Equal(expected, TimeFormatter.Relative(Now - TimeSpan.FromSeconds(secondsAgo), Now, Chicago));

    [Fact]
    public void Relative_AfterADay_IsAWeekdayTimeOrADate()
    {
        Assert.Equal("yesterday 9:00 pm", TimeFormatter.Relative(Now - TimeSpan.FromHours(24) - TimeSpan.FromMinutes(25), Now, Chicago));
        Assert.Equal("Sun 9:25 pm", TimeFormatter.Relative(Now - TimeSpan.FromDays(3), Now, Chicago));
        Assert.Equal("Sep 23", TimeFormatter.Relative(Now - TimeSpan.FromDays(7), Now, Chicago));
    }

    [Fact]
    public void LastSeen_IsTheTailOfTheStaleAndOfflineLines()
    {
        Assert.Equal("last seen 42 min ago", TimeFormatter.LastSeen(Now - TimeSpan.FromMinutes(42), Now, Chicago));
        Assert.Equal("last seen 3 hr ago", TimeFormatter.LastSeen(Now - TimeSpan.FromHours(3), Now, Chicago));
        Assert.Equal("last seen Sun 9:25 pm", TimeFormatter.LastSeen(Now - TimeSpan.FromDays(3), Now, Chicago));
    }

    [Theory]
    [InlineData("2026-09-30T17:31:00-05:00", "2026-09-30T17:48:00-05:00", "5:31 – 5:48 pm")]
    [InlineData("2026-09-30T11:50:00-05:00", "2026-09-30T12:10:00-05:00", "11:50 am – 12:10 pm")]
    [InlineData("2026-09-30T09:05:00-05:00", "2026-09-30T09:50:00-05:00", "9:05 – 9:50 am")]
    [InlineData("2026-09-30T23:30:00-05:00", "2026-10-01T00:15:00-05:00", "11:30 pm – 12:15 am")]
    public void Range_ShowsAmPmOnceWhenBothEndsAreInTheSameHalfOfTheDay(string start, string end, string expected) =>
        Assert.Equal(expected, TimeFormatter.Range(Instant(start), Instant(end), Chicago));

    // ---- units: imperial only --------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(0, "0 ft")]
    [InlineData(97.5, "320 ft")]                                            // under 0.1 mi: feet, to the nearest 10
    [InlineData(161, "0.1 mi")]
    [InlineData(1609.344, "1.0 mi")]                                        // 0.1 to 9.9 mi: one decimal
    [InlineData(15000, "9.3 mi")]
    [InlineData(16093, "10 mi")]                                            // 10 mi and more: whole
    [InlineData(249000, "155 mi")]
    public void Distance_FollowsTheThreeBands(double meters, string expected) =>
        Assert.Equal(expected, UnitFormatter.Distance(meters));

    [Theory]
    [InlineData(97.5, "320 feet")]
    [InlineData(1609.344, "1.0 mile")]
    [InlineData(1700, "1.1 miles")]
    [InlineData(249000, "155 miles")]
    public void DistanceWords_IsTheSpokenForm_SingularAtOne(double meters, string expected) =>
        Assert.Equal(expected, UnitFormatter.DistanceWords(meters));

    [Theory]
    [InlineData(100, "Radius 328 ft")]
    [InlineData(804.672, "Radius 0.5 mi")]
    public void Radius_IsFeetUnderATenthOfAMile(double meters, string expected) =>
        Assert.Equal(expected, UnitFormatter.Radius(meters));

    [Theory]
    [InlineData(24.1, "54 mph")]
    [InlineData(0, "0 mph")]
    [InlineData(4.47, "10 mph")]
    public void Speed_IsAWholeNumberOfMilesPerHour(double metersPerSecond, string expected) =>
        Assert.Equal(expected, UnitFormatter.Speed(metersPerSecond));

    [Fact]
    public void Percent_IsAnIntegerWithAPercentSign() => Assert.Equal("19%", UnitFormatter.Percent(19));

    [Fact]
    public void Metric_IsDeferred_NotQuietlyPrintedInMiles()
    {
        Assert.Throws<NotSupportedException>(() => UnitFormatter.Distance(100, UnitSystem.Metric));
        Assert.Throws<NotSupportedException>(() => UnitFormatter.DistanceWords(100, UnitSystem.Metric));
        Assert.Throws<NotSupportedException>(() => UnitFormatter.Radius(100, UnitSystem.Metric));
        Assert.Throws<NotSupportedException>(() => UnitFormatter.Speed(10, UnitSystem.Metric));
    }

    // ---- the rows of 01 section 5.1 and 8.4, from the Demo cast ----------------------------------------------------------------------------

    [Fact(DisplayName = "[AC-25] Drivers lists Alden, Briar, Cass, Dara, Elio; Alden's row reads the spec, with a charging battery and no distance")]
    public void DriversRows_AreInOrder_AndAldensRowIsTheSpecs()
    {
        var rows = DemoRows();

        Assert.Equal(DemoCast.AllMembers.Select(member => member.Id), rows.Select(row => row.Id));
        var alden = rows[0];
        Assert.Equal(DemoCast.King.Name, alden.Name);
        Assert.Equal(DemoCast.King.Lore, alden.Lore);
        Assert.Equal("At " + DemoPlaces.Home.Name, alden.StatusLine);
        Assert.Equal("Since 5:52 pm", alden.DetailLine);
        Assert.DoesNotContain("away", alden.DetailLine, StringComparison.Ordinal);
        Assert.Equal("19%", alden.Battery?.Text);
        Assert.True(alden.Battery?.Charging);
        Assert.False(alden.Battery?.Low);
        Assert.Equal("row-member-king", alden.TestId);
        Assert.Equal(MemberStatus.AtPlace, alden.Status);
    }

    [Fact(DisplayName = "[AC-26] Briar's row reads Driving · 54 mph on I-65 with Since 9:12 pm; Cass's reads her hall with 1.0 mi away and a low battery")]
    public void DriversRows_BriarDrives_AndCassIsLow()
    {
        var rows = DemoRows();

        var briar = rows[1];
        Assert.Equal($"Driving · 54 mph on {DemoCast.Queen.Address}", briar.StatusLine);
        Assert.Equal("Since 9:12 pm", briar.DetailLine);                    // a driving row has no distance (AC-26)
        Assert.Equal("62%", briar.Battery?.Text);
        Assert.Equal(MemberStatus.Driving, briar.Status);

        var cass = rows[2];
        Assert.Equal("At " + DemoPlaces.JesterHall.Name, cass.StatusLine);
        Assert.Equal("Since 9:06 pm · 1.0 mi away", cass.DetailLine);
        Assert.Equal("12%", cass.Battery?.Text);
        Assert.True(cass.Battery?.Low);
        Assert.Contains("low", cass.Battery?.AccessibleName, StringComparison.Ordinal);
        Assert.Contains("low", cass.AccessibleName, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "[AC-27] Dara's row is the stale, far-away one (warning line, 10% low); Elio's is the static prince with no battery")]
    public void DriversRows_DaraIsStale_AndElioIsStatic()
    {
        var rows = DemoRows();

        var dara = rows[3];
        // The cast stores the address as "Eastgate Avenue, Pinebrook, AL"; the far-away line reads "{street} · {City}, {ST}".
        var address = DemoCast.Cryptid.Address!.Split(", ");
        Assert.Equal($"{address[0]} · {address[1]}, {address[2]}", dara.StatusLine);
        Assert.Equal("The raven's late — last seen 42 min ago", dara.DetailLine);
        Assert.Equal(LineTone.Warning, dara.DetailTone);
        Assert.Equal(MemberStatus.Stale, dara.Status);
        Assert.Equal("10%", dara.Battery?.Text);
        Assert.True(dara.Battery?.Low);

        var elio = rows[4];
        Assert.Equal(DemoCast.Prince.StaticLabel, elio.StatusLine);
        Assert.Equal("Location isn't shared", elio.DetailLine);
        Assert.Null(elio.Battery);
        Assert.Equal(DemoCast.Prince.Lore, elio.Lore);
        Assert.Equal(MemberStatus.Static, elio.Status);
    }

    [Fact(DisplayName = "[AC-46] Cass's accessible name is the spec's pin name, to the word")]
    public void DriversRows_CassAccessibleName_IsThePinNameOfTheSpec()
    {
        var cass = DemoRows()[2];

        Assert.Equal(
            $"{DemoCast.Jester.Name}, {DemoCast.Jester.Lore}. At {DemoPlaces.JesterHall.Name} since 9:06 pm. Battery 12 percent, low. 1.0 mile away.",
            cass.AccessibleName);
    }

    [Fact]
    public void DriversRows_TheOtherAccessibleNames_ReadAsSentences()
    {
        var rows = DemoRows();

        Assert.Equal($"{DemoCast.King.Name}, {DemoCast.King.Lore}. At {DemoPlaces.Home.Name} since 5:52 pm. Battery 19 percent, charging.", rows[0].AccessibleName);
        Assert.Equal($"{DemoCast.Queen.Name}, {DemoCast.Queen.Lore}. Driving, 54 mph on {DemoCast.Queen.Address} since 9:12 pm. Battery 62 percent.", rows[1].AccessibleName);

        // A reading older than 15 minutes adds its age to the name, never to the visible pill (R-113).
        Assert.Equal("Battery 10 percent, low (battery as of 42 min ago)", rows[3].Battery?.AccessibleName);
        Assert.Equal("10%", rows[3].Battery?.Text);
        Assert.Equal($"{DemoCast.Prince.Name}, {DemoCast.Prince.Lore}. Home, Highmeadow. Location isn't shared.", rows[4].AccessibleName);
    }

    [Fact(DisplayName = "[AC-50] a UTC browser changes nothing: the row's clock times come from the session's zone, which is an input, not the machine's")]
    public void DriversRows_SinceIsInTheSessionZone_NotTheBrowsers()
    {
        var chicago = VmFactory.Member(Demo.Members[0], RowFacts.Create(Demo.Members, Demo.Places, null, DemoNow, Session.Zone));
        var utc = VmFactory.Member(Demo.Members[0], RowFacts.Create(Demo.Members, Demo.Places, null, DemoNow, TimeZoneInfo.Utc));

        Assert.Equal("Since 5:52 pm", chicago.DetailLine);
        Assert.Equal("Since yesterday 10:52 pm", utc.DetailLine);           // the same instant in UTC: 10:52 pm, and already "yesterday" there (it is 2:25 am on Oct 1)
        Assert.Equal("America/Chicago", Session.Zone.Id);
    }

    // ---- status line and L3, one rule at a time ---------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(54, "I-65", "Driving · 54 mph on I-65")]
    [InlineData(54, null, "Driving · 54 mph")]
    [InlineData(null, "I-65", "Driving on I-65")]
    [InlineData(null, null, "Driving")]
    public void Status_Driving_ShowsTheSpeedAndTheStreetOnlyWhenKnown(int? mph, string? street, string expected)
    {
        var member = Member("a", isDriving: true) with { Street = street, SpeedMps = mph is { } speed ? speed / 2.2369362920544 : null };

        Assert.Equal(expected, RowOf(member).StatusLine);
    }

    [Fact]
    public void Status_Out_IsTheStreet_NearItWhenTheAccuracyIsPoor_AndNeverNearFallbackText()
    {
        var out1 = Member("a") with { Lat = 31.1090, Street = "Elm Street", AccuracyM = 20 };
        var poor = out1 with { AccuracyM = 800 };
        var noStreet = poor with { Street = null };

        Assert.Equal("Elm Street", RowOf(out1).StatusLine);
        Assert.Equal("Near Elm Street", RowOf(poor).StatusLine);
        Assert.Equal("Somewhere in the Realm", RowOf(noStreet).StatusLine);     // O-2: never "Near Somewhere in the Realm"
        Assert.Equal("Somewhere in the Realm", RowOf(noStreet with { Street = "   " }).StatusLine);
    }

    [Fact]
    public void Status_FarAway_IsTheStreetWithTheCityAndState_WhenBothAreKnown()
    {
        var far = Member("a") with { Lat = 32.1, Street = "Eastgate Avenue", City = "Pinebrook", Region = "AL" };

        Assert.Equal("Eastgate Avenue · Pinebrook, AL", RowOf(far).StatusLine);
        Assert.Equal("Eastgate Avenue", RowOf(far with { City = null }).StatusLine);
        Assert.Equal("Eastgate Avenue", RowOf(far with { Region = null }).StatusLine);
        Assert.Equal("Somewhere in the Realm", RowOf(far with { Street = null }).StatusLine);
    }

    [Fact]
    public void Status_AtAPlace_NamesTheZone_AndAZoneThatIsNotListedIsOut()
    {
        var inside = Member("a") with { PlaceId = "p1", Street = "Elm Street" };
        var zone = Place("p1", memberIds: ["a"], vehicleIds: []) with { DisplayName = "Hearth Haven" };

        Assert.Equal("At Hearth Haven", RowOf(inside, zone).StatusLine);
        Assert.Equal(MemberStatus.AtPlace, RowOf(inside, zone).Status);
        Assert.Equal("Elm Street", RowOf(inside).StatusLine);               // the arrival zone is never drawn or listed: the member is simply out
        Assert.Equal(MemberStatus.Out, RowOf(inside).Status);
    }

    [Fact]
    public void Status_StaleAndOffline_KeepTheLastKnownLine_AndWarnOnTheDetail()
    {
        var zone = Place("p1", memberIds: ["a"], vehicleIds: []) with { DisplayName = "Hearth Haven" };
        var stale = Member("a", freshness: Freshness.Stale, updated: Now - TimeSpan.FromHours(3)) with { PlaceId = "p1" };
        var offline = Member("a", freshness: Freshness.Offline, updated: Instant("2026-09-29T16:10:00-05:00") - TimeSpan.FromDays(1)) with { PlaceId = "p1" };

        var staleRow = RowOf(stale, zone);
        Assert.Equal("At Hearth Haven", staleRow.StatusLine);
        Assert.Equal("The raven's late — last seen 3 hr ago", staleRow.DetailLine);
        Assert.Equal(LineTone.Warning, staleRow.DetailTone);

        var offlineRow = RowOf(offline, zone);
        Assert.Equal("At Hearth Haven", offlineRow.StatusLine);
        Assert.Equal("Gone dark — last seen Mon 4:10 pm", offlineRow.DetailLine);
        Assert.Equal(LineTone.Stale, offlineRow.DetailTone);
        Assert.Equal(MemberStatus.Offline, offlineRow.Status);
    }

    [Fact]
    public void Status_NoFix_AndStatic_HaveTheirOwnLines()
    {
        var noFix = Member("a", freshness: Freshness.NoFix, updated: null) with { Lat = null, Lon = null };
        var fixedPin = Member("b", kind: MemberKind.Static, freshness: Freshness.Static);

        var noFixRow = RowOf(noFix);
        Assert.Equal("Location unavailable", noFixRow.StatusLine);
        Assert.Equal("The scouts have not reported", noFixRow.DetailLine);
        Assert.Equal(MemberStatus.NoFix, noFixRow.Status);

        Assert.Equal("Fixed position", RowOf(fixedPin).StatusLine);         // a static member without a label
        Assert.Equal("Home · Highmeadow", RowOf(fixedPin with { StaticLabel = "Home · Highmeadow" }).StatusLine);
        Assert.Equal("Location isn't shared", RowOf(fixedPin).DetailLine);
    }

    [Fact]
    public void Detail_IsSince_OrUpdated_WhenThereIsNoArrivalTime_OrEmpty()
    {
        var since = Member("a") with { SinceUtc = Instant("2026-09-30T21:06:00-05:00") };

        Assert.Equal("Since 9:06 pm · 0.7 mi away", RowOf(since with { Lat = 31.1090 }).DetailLine);
        Assert.Equal("Updated 1 min ago · 0.7 mi away", RowOf(since with { Lat = 31.1090, SinceUtc = null }).DetailLine);
        Assert.Equal("0.7 mi away", RowOf(since with { Lat = 31.1090, SinceUtc = null, LastUpdateUtc = null }).DetailLine);
        Assert.Equal(string.Empty, RowOf(since with { SinceUtc = null, LastUpdateUtc = null }, viewerIsSubject: true).DetailLine);
    }

    [Fact]
    public void Detail_TheDistanceIsForAnotherMembersFreshRow_NeverMineDrivingStaleOrOffline()
    {
        var other = Member("a") with { Lat = 31.1090, SinceUtc = Now - TimeSpan.FromHours(1) };

        Assert.Contains("0.7 mi away", RowOf(other).DetailLine, StringComparison.Ordinal);
        Assert.DoesNotContain("away", RowOf(other, viewerIsSubject: true).DetailLine, StringComparison.Ordinal);
        Assert.DoesNotContain("away", RowOf(other with { IsDriving = true }).DetailLine, StringComparison.Ordinal);
        Assert.DoesNotContain("away", RowOf(other with { Freshness = Freshness.Stale }).DetailLine, StringComparison.Ordinal);
        Assert.DoesNotContain("away", RowOf(other with { Freshness = Freshness.Offline }).DetailLine, StringComparison.Ordinal);
    }

    [Fact]
    public void Title_TheLoreIsOmittedWhenThereIsNone()
    {
        var plain = Member("a");

        Assert.Null(RowOf(plain).Lore);
        Assert.Null(RowOf(plain with { LoreTitle = "  " }).Lore);
        Assert.Equal("The Hermit", RowOf(plain with { LoreTitle = "The Hermit" }).Lore);
        Assert.StartsWith("Pat a. ", RowOf(plain).AccessibleName, StringComparison.Ordinal);
        Assert.StartsWith("Pat a, The Hermit. ", RowOf(plain with { LoreTitle = "The Hermit" }).AccessibleName, StringComparison.Ordinal);
    }

    // ---- the battery ------------------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(14, true)]
    [InlineData(15, false)]
    [InlineData(100, false)]
    public void Battery_IsLowUnderFifteenPercent(int percent, bool low) =>
        Assert.Equal(low, RowOf(Member("a") with { BatteryPct = percent }).Battery?.Low);

    [Fact]
    public void Battery_UnknownIsNoPill_AndTheNameOrdersCharging_Low_ThenTheAge()
    {
        Assert.Null(RowOf(Member("a") with { BatteryPct = null, Charging = null, BatteryAsOfUtc = null }).Battery);

        var both = Member("a") with { BatteryPct = 12, Charging = true, BatteryAsOfUtc = Now - TimeSpan.FromMinutes(20) };
        Assert.Equal("Battery 12 percent, charging, low (battery as of 20 min ago)", RowOf(both).Battery?.AccessibleName);

        // Exactly 15 minutes old is still current.
        Assert.Equal("Battery 12 percent, charging, low", RowOf(both with { BatteryAsOfUtc = Now - TimeSpan.FromMinutes(15) }).Battery?.AccessibleName);
    }

    // ---- the member colour goes into a style attribute ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData("#E8BC4E", "#E8BC4E")]
    [InlineData("#abc", "#abc")]
    [InlineData("#11223344", "#11223344")]
    [InlineData("red", null)]
    [InlineData("#12", null)]
    [InlineData("#GGGGGG", null)]
    [InlineData("#112233; background:url(x)", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void SafeColor_PassesOnlyAPlainHexValue(string? color, string? expected) =>
        Assert.Equal(expected, VmFactory.SafeColor(color));

    // ---- vehicles: 01 section 5.2 ------------------------------------------------------------------------------------------------------------

    [Fact(DisplayName = "[AC-28a] the pickup's row reads the spec and the hatchback, last heard 20 days ago, reads in the warning tone")]
    public void VehicleRows_ThePickupAndTheStaleHatchback_AreTheSpecs()
    {
        var rows = VmFactory.VehicleRows(Demo.Vehicles, Facts());

        Assert.Equal(DemoCast.AllVehicles.Select(vehicle => vehicle.Id), rows.Select(row => row.Id));
        var wagon = rows[0];
        Assert.Equal(DemoCast.Wagon.Name, wagon.Name);
        Assert.Equal(DemoCast.Wagon.Lore, wagon.Lore);
        Assert.Equal("At " + DemoPlaces.Home.Name, wagon.LocationLine);
        Assert.Equal("Updated 20 min ago", wagon.Updated);
        Assert.Equal(LineTone.Normal, wagon.UpdatedTone);
        Assert.Equal(VehicleGlyph.Pickup, wagon.Glyph);
        Assert.Equal("row-vehicle-wagon", wagon.TestId);
        Assert.Equal($"{DemoCast.Wagon.Name}, {DemoCast.Wagon.Lore}. At {DemoPlaces.Home.Name}. Updated 20 min ago.", wagon.AccessibleName);

        var chariot = rows[1];
        Assert.StartsWith("Last heard", chariot.Updated, StringComparison.Ordinal);
        Assert.Equal(LineTone.Warning, chariot.UpdatedTone);
        Assert.Equal(VehicleGlyph.Car, chariot.Glyph);
        Assert.StartsWith($"{DemoCast.Chariot.Name}, {DemoCast.Chariot.Lore}.", chariot.AccessibleName, StringComparison.Ordinal);
    }

    [Fact]
    public void Vehicle_Location_FollowsThePrecedenceOfTheSpec()
    {
        var parked = Vehicle("v") with { Street = "Elm Street", PlaceId = "p1" };
        var zone = Place("p1", memberIds: [], vehicleIds: ["v"]) with { DisplayName = "Hearth Haven" };

        Assert.Equal("At Hearth Haven", VehicleRow(parked, zone).LocationLine);
        Assert.Equal("Elm Street", VehicleRow(parked with { PlaceId = null }).LocationLine);
        Assert.Equal("Somewhere in the Realm", VehicleRow(parked with { PlaceId = null, Street = null }).LocationLine);
        Assert.Equal("Location unavailable", VehicleRow(parked with { Lat = null, Lon = null, Freshness = Freshness.NoFix }).LocationLine);
        Assert.Equal("Driving · 54 mph", VehicleRow(parked with { IsMoving = true, SpeedMps = 24.1 }, zone).LocationLine);
        Assert.Equal("Driving", VehicleRow(parked with { IsMoving = true, SpeedMps = null }, zone).LocationLine);
    }

    [Fact]
    public void Vehicle_UnknownUpdate_IsLeftOut()
    {
        var unknown = VehicleRow(Vehicle("v"));

        Assert.Equal(string.Empty, unknown.Updated);
        Assert.DoesNotContain("Updated", unknown.AccessibleName, StringComparison.Ordinal);
    }

    [Fact]
    public void Vehicle_Stale_IsLastHeard_InTheWarningTone()
    {
        var stale = VehicleRow(Vehicle("v") with { Freshness = Freshness.Stale, LastUpdateUtc = Now - TimeSpan.FromHours(1) });
        var fresh = VehicleRow(Vehicle("v") with { LastUpdateUtc = Now - TimeSpan.FromMinutes(20) });

        Assert.Equal("Last heard 1 hr ago", stale.Updated);
        Assert.Equal(LineTone.Warning, stale.UpdatedTone);
        Assert.Equal("Updated 20 min ago", fresh.Updated);
        Assert.Equal(LineTone.Normal, fresh.UpdatedTone);
    }

    [Fact]
    public void Vehicle_WithoutAFix_ReadsLocationUnavailable()
    {
        var row = VehicleRow(Vehicle("v", noFix: true));

        Assert.Equal("Location unavailable", row.LocationLine);
    }

    // ---- places: 01 section 5.3 --------------------------------------------------------------------------------------------------------------

    [Fact(DisplayName = "[AC-29] Places lists 14 rows: Hearth Haven and The Jester's Hall (1 here each, people only), then the empty places A to Z")]
    public void PlaceRows_AreOccupiedFirst_ThenAToZ_AndCountPeopleOnly()
    {
        var rows = VmFactory.PlaceRows(Demo.Places, Facts());

        Assert.Equal(14, rows.Count);
        Assert.Equal([DemoPlaces.Home.Name, DemoPlaces.JesterHall.Name], rows.Take(2).Select(row => row.Name));
        Assert.All(rows.Take(2), row => Assert.Equal("1 here", row.CountText));

        // The parked pickup is inside Hearth Haven and adds nothing (R2-009): one person, one avatar.
        Assert.Contains(DemoCast.Wagon.Id, Demo.Places.Single(place => place.Id == DemoPlaces.Home.Id).VehicleIdsInside);
        Assert.Equal(1, rows[0].People);

        var empties = rows.Skip(2).ToList();
        Assert.All(empties, row => Assert.Equal("Empty", row.CountText));
        Assert.Equal(empties.Select(row => row.Name).OrderBy(name => name, StringComparer.OrdinalIgnoreCase), empties.Select(row => row.Name));
        Assert.Contains(rows, row => row.Name == DemoPlaces.Work2.Name);
        Assert.Contains(rows, row => row.Name == DemoPlaces.SkateTwo.Name);
        Assert.DoesNotContain(rows, row => row.Id == DemoPlaces.Approach.Id);
    }

    [Fact]
    public void PlaceRows_TheSubtitlesAreTheDemoTable()
    {
        var rows = VmFactory.PlaceRows(Demo.Places, Facts());

        foreach (var place in DemoPlaces.Drawn)
        {
            Assert.Equal(place.Subtitle, rows.Single(row => row.Id == place.Id).Subtitle);
            Assert.Equal(place.Kind, rows.Single(row => row.Id == place.Id).Kind);
        }

        Assert.Equal("row-place-home", rows.Single(row => row.Name == DemoPlaces.Home.Name).TestId);
        Assert.Equal($"{DemoPlaces.Home.Name}, {DemoPlaces.Home.Subtitle}. 1 here: {DemoCast.King.Name}.", rows.Single(row => row.Name == DemoPlaces.Home.Name).AccessibleName);
        Assert.Equal($"{DemoPlaces.Vet.Name}, {DemoPlaces.Vet.Subtitle}. Empty.", rows.Single(row => row.Name == DemoPlaces.Vet.Name).AccessibleName);
    }

    [Fact]
    public void PlaceRows_OccupiedPlacesSortByHeadcountThenName_AndAVehicleNeverOccupies()
    {
        var places = new[]
        {
            Place("c", memberIds: ["a"], vehicleIds: []) with { DisplayName = "Zed Hall" },
            Place("b", memberIds: ["a", "b"], vehicleIds: []) with { DisplayName = "Mid Hall" },
            Place("a", memberIds: ["a"], vehicleIds: []) with { DisplayName = "Abe Hall" },
            Place("d", memberIds: [], vehicleIds: ["v1", "v2"]) with { DisplayName = "Aaa Garage" },
            Place("e", memberIds: [], vehicleIds: []) with { DisplayName = "Bbb Field" },
        };
        var rows = VmFactory.PlaceRows(places, RowFacts.Create([], places, null, Now, Chicago));

        Assert.Equal(["Mid Hall", "Abe Hall", "Zed Hall", "Aaa Garage", "Bbb Field"], rows.Select(row => row.Name));
        Assert.Equal(["2 here", "1 here", "1 here", "Empty", "Empty"], rows.Select(row => row.CountText));
    }

    [Fact]
    public void PlaceRows_ShowThreeMiniAvatarsThenThePlusN()
    {
        var people = Enumerable.Range(0, 5).Select(index => Member($"m{index}") with { DisplayName = "Pat " + (char)('A' + index) }).ToList();
        var crowd = Place("crowd", memberIds: [.. people.Select(person => person.Id)], vehicleIds: []);

        var row = VmFactory.Place(crowd, RowFacts.Create(people, [crowd], null, Now, Chicago));

        Assert.Equal(5, row.People);
        Assert.Equal("5 here", row.CountText);
        Assert.Equal(3, row.Avatars.Count);
        Assert.Equal(2, row.More);
        Assert.Equal("P", row.Avatars[0].Initial);
    }

    // ---- edge bubbles: 01 sections 4.10 and 10.3 ------------------------------------------------------------------------------------------

    // The legs are the ones 01 Appendix A.1 states for the two members behind the bubbles: Dara 155 miles east of Alden, Elio 681 miles north-west (metres here).
    private const double CryptidMeters = 249_790;
    private const double PrinceMeters = 1_095_947;

    [Fact]
    public void BubbleLabel_IsTheNameTheDistanceInWordsAndTheCompassWord()
    {
        Assert.Equal(
            $"{DemoCast.Cryptid.Name}, 155 miles east, off screen. Double tap to include on the map.",
            BubbleTextFormatter.Label(DemoCast.Cryptid.Name, (CryptidMeters, 90)));
        Assert.Equal(
            $"{DemoCast.Prince.Name}, 681 miles north-west, off screen. Double tap to include on the map.",
            BubbleTextFormatter.Label(DemoCast.Prince.Name, (PrinceMeters, 315)));
    }

    [Fact]
    public void BubbleTooltip_IsTheNameTheShortDistanceAndTheCompassWord()
    {
        Assert.Equal(
            $"{DemoCast.Cryptid.Name} · 155 mi east · tap to include on the map",
            BubbleTextFormatter.Tooltip(DemoCast.Cryptid.Name, (CryptidMeters, 90)));
        Assert.Equal(
            $"{DemoCast.Prince.Name} · 681 mi north-west · tap to include on the map",
            BubbleTextFormatter.Tooltip(DemoCast.Prince.Name, (PrinceMeters, 315)));
    }

    [Fact]
    public void BubbleText_WithoutAReferencePoint_LeavesTheDistanceOut()
    {
        // The viewer's own bubble, and any bubble when there is no "me" to measure from.
        Assert.Equal($"{DemoCast.King.Name}, off screen. Double tap to include on the map.", BubbleTextFormatter.Label(DemoCast.King.Name, null));
        Assert.Equal($"{DemoCast.King.Name} · tap to include on the map", BubbleTextFormatter.Tooltip(DemoCast.King.Name, null));
    }

    [Theory]
    [InlineData(97.5, "320 feet", "320 ft")]                                // the three bands of UnitFormatter, in words and short
    [InlineData(1609.344, "1.0 mile", "1.0 mi")]                            // singular at exactly one
    [InlineData(1700, "1.1 miles", "1.1 mi")]
    [InlineData(16093, "10 miles", "10 mi")]
    public void BubbleText_UsesTheDistanceBandsOfUnitFormatter(double meters, string words, string brief)
    {
        Assert.Equal($"{DemoCast.Jester.Name}, {words} north, off screen. Double tap to include on the map.", BubbleTextFormatter.Label(DemoCast.Jester.Name, (meters, 0)));
        Assert.Equal($"{DemoCast.Jester.Name} · {brief} north · tap to include on the map", BubbleTextFormatter.Tooltip(DemoCast.Jester.Name, (meters, 0)));
    }

    [Theory]
    [InlineData(0, "north")]
    [InlineData(45, "north-east")]
    [InlineData(90, "east")]
    [InlineData(135, "south-east")]
    [InlineData(180, "south")]
    [InlineData(225, "south-west")]
    [InlineData(270, "west")]
    [InlineData(315, "north-west")]
    [InlineData(22.4, "north")]                                             // the nearest of eight, a half step rounding clockwise
    [InlineData(22.5, "north-east")]
    [InlineData(337.4, "north-west")]
    [InlineData(337.5, "north")]
    [InlineData(359, "north")]
    [InlineData(360, "north")]
    [InlineData(405, "north-east")]                                         // a bearing outside 0 to 360 wraps
    [InlineData(-45, "north-west")]
    public void CompassWord_IsTheNearestOfEightDirections(double bearingDeg, string expected) =>
        Assert.Equal(expected, BubbleTextFormatter.CompassWord(bearingDeg));

    [Fact]
    public void ClusterText_CountsThePeopleAndListsTheNamesInTheOrderOfTheBubble()
    {
        string[] two = [DemoCast.Cryptid.Name, DemoCast.Prince.Name];
        string[] three = [DemoCast.Queen.Name, DemoCast.King.Name, DemoCast.Jester.Name];

        Assert.Equal($"2 people off screen: {two[0]}, {two[1]}. Double tap to include them on the map.", BubbleTextFormatter.ClusterLabel(two));
        Assert.Equal($"3 people off screen: {three[0]}, {three[1]}, {three[2]}. Double tap to include them on the map.", BubbleTextFormatter.ClusterLabel(three));
        Assert.Equal($"{two[0]}, {two[1]} · tap to include them on the map", BubbleTextFormatter.ClusterTooltip(two));
    }

    [Fact]
    public void ClusterText_IsWhatTheTemplatesOfTheMapStringsGiveWhenTheScriptFillsThem()
    {
        // realmMap.js never builds a string: it fills {n} and {names} of MapStrings. The two sources must not drift apart.
        string[] names = [DemoCast.Cryptid.Name, DemoCast.Prince.Name, DemoCast.King.Name];
        var strings = Realm.Web.Components.Map.MapStrings.Default;
        var joined = string.Join(", ", names);

        Assert.Equal(
            BubbleTextFormatter.ClusterLabel(names),
            strings.ClusterName.Replace("{n}", "3", StringComparison.Ordinal).Replace("{names}", joined, StringComparison.Ordinal));
        Assert.Equal(BubbleTextFormatter.ClusterTooltip(names), strings.ClusterTooltip.Replace("{names}", joined, StringComparison.Ordinal));
    }

    // ---- helpers ---------------------------------------------------------------------------------------------------------------------------

    // The Demo session owns a clock and the line is computed at its instant (the frozen Demo anchor), never at the wall clock; the session is never started,
    // so there is nothing to dispose (the same snapshot is read by MapPayloadFactoryTests).
    private static readonly IRealmSession Session = FullCast.Session(null);

    private static RealmSnapshot Demo => Session.Current;

    private static DateTimeOffset DemoNow => Session.Time.GetUtcNow();

    private static MemberVm Member(
        string id,
        Freshness freshness = Freshness.Fresh,
        MemberKind kind = MemberKind.Live,
        bool isDriving = false,
        DateTimeOffset? updated = null) =>
        new(
            Id: id,
            DisplayName: "Pat " + id,
            LoreTitle: null,
            AvatarUrl: null,
            Color: "#445566",
            Kind: kind,
            Lat: 31.0990,
            Lon: -85.3410,
            AccuracyM: 10,
            BatteryPct: 50,
            Charging: null,
            BatteryAsOfUtc: null,
            IsDriving: isDriving,
            SpeedMps: null,
            Street: null,
            City: null,
            Region: null,
            FullAddress: null,
            PlaceId: null,
            SinceUtc: null,
            LastUpdateUtc: updated ?? (freshness == Freshness.NoFix || kind == MemberKind.Static ? null : Now - TimeSpan.FromMinutes(1)),
            SortOrder: 0,
            Freshness: freshness,
            StaticLabel: null);

    private static VehicleVm Vehicle(string id, bool isMoving = false, bool noFix = false) =>
        new(
            Id: id,
            Name: "Cart " + id,
            LoreTitle: null,
            Glyph: VehicleGlyph.Car,
            Lat: noFix ? null : 31.0990,
            Lon: noFix ? null : -85.3410,
            Street: null,
            PlaceId: null,
            LastUpdateUtc: null,
            SpeedMps: null,
            IsMoving: isMoving,
            Freshness: noFix ? Freshness.NoFix : Freshness.Fresh);

    private static PlaceVm Place(string id, IReadOnlyList<string> memberIds, IReadOnlyList<string> vehicleIds) =>
        new(id, "Place " + id, string.Empty, PlaceKind.Other, 31.0990, -85.3410, 100, memberIds, vehicleIds);

    // ---- helpers for the rows --------------------------------------------------------------------------------------------------------------

    private static TimeZoneInfo Chicago => Session.Zone;

    private static DateTimeOffset Instant(string iso) => DateTimeOffset.Parse(iso, CultureInfo.InvariantCulture);

    private static RowFacts Facts() => RowFacts.Create(Demo.Members, Demo.Places, null, DemoNow, Session.Zone);

    private static IReadOnlyList<MemberRowVm> DemoRows() => VmFactory.MemberRows(Demo.Members, Facts());

    // One member's row, as seen by a viewer who stands at the home coordinates (the origin of "away" and of "far"): the viewer is "me" and the subject is somebody else,
    // unless viewerIsSubject, when the subject is the viewer.
    private static MemberRowVm RowOf(MemberVm subject, params PlaceVm[] places) => RowOf(subject, viewerIsSubject: false, places);

    private static MemberRowVm RowOf(MemberVm subject, bool viewerIsSubject, params PlaceVm[] places)
    {
        var me = Member("me");
        var members = viewerIsSubject ? new[] { subject } : [me, subject];
        var facts = RowFacts.Create(members, places, viewerIsSubject ? subject.Id : me.Id, Now, Chicago);
        return VmFactory.Member(subject, facts);
    }

    private static VehicleRowVm VehicleRow(VehicleVm vehicle, params PlaceVm[] places) =>
        VmFactory.Vehicle(vehicle, RowFacts.Create([], places, null, Now, Chicago));
}
