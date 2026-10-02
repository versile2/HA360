using Realm.Demo;
using Realm.Domain;
using Realm.Web.Formatting;
using Xunit;

namespace Realm.Web.Tests;

/// <summary>
/// The handle-summary line of 01 section 8.2 (<see cref="HandleSummaryFormatter"/>): the rows of the spec built from the Demo cast, then the rules one at a time on
/// small hand-built view models (the 24 hour window, "driving" needing a fresh fix, the singular forms). S7b extends this file with the row formatters.
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
    public void Vehicles_APlaceholderWithNoData_IsParked()
    {
        var vehicles = new[] { Vehicle("wagon"), Vehicle("chariot", isPlaceholder: true) };

        Assert.Equal("2 vehicles · all parked", HandleSummaryFormatter.Vehicles(vehicles));
    }

    // ---- places ----------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Places_OccupiedMeansAPersonOrAVehicleInside()
    {
        var places = new[]
        {
            Place("a", memberIds: ["king"], vehicleIds: []),
            Place("b", memberIds: [], vehicleIds: ["wagon"]),
            Place("c", memberIds: ["queen", "jester"], vehicleIds: ["chariot"]),
            Place("d", memberIds: [], vehicleIds: []),
        };

        Assert.Equal("4 places · 3 occupied", HandleSummaryFormatter.Places(places));
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

    // ---- helpers ---------------------------------------------------------------------------------------------------------------------------

    // The Demo session owns a clock and the line is computed at its instant (the frozen Demo anchor), never at the wall clock; the session is never started,
    // so there is nothing to dispose (the same snapshot is read by MapPayloadFactoryTests).
    private static readonly IRealmSession Session = new DemoRealmSessionFactory().Create(null);

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
            Lon: -97.3410,
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

    private static VehicleVm Vehicle(string id, bool isMoving = false, bool isPlaceholder = false) =>
        new(
            Id: id,
            Name: "Cart " + id,
            LoreTitle: null,
            Glyph: VehicleGlyph.Car,
            Lat: isPlaceholder ? null : 31.0990,
            Lon: isPlaceholder ? null : -97.3410,
            Street: null,
            PlaceId: null,
            Ignition: null,
            RemoteStartSecondsLeft: null,
            FuelPct: null,
            OdometerM: null,
            LastUpdateUtc: null,
            SpeedMps: null,
            IsMoving: isMoving,
            Freshness: isPlaceholder ? Freshness.NoFix : Freshness.Fresh,
            IsPlaceholder: isPlaceholder,
            PlaceholderNote: null);

    private static PlaceVm Place(string id, IReadOnlyList<string> memberIds, IReadOnlyList<string> vehicleIds) =>
        new(id, "Place " + id, string.Empty, PlaceKind.Other, 31.0990, -97.3410, 100, memberIds, vehicleIds);
}
