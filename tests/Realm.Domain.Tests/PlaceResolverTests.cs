using Xunit;

namespace Realm.Domain.Tests;

// Expected values are the rules of 02 section 4.5: enter inside the radius, stay until radius + 30 m, a fix
// worse than 200 m cannot change membership, the smallest zone wins, and trip endpoints snap within radius + 75 m.
// Distances are built with the exact inverse of the haversine for due north (metres / (R x pi / 180) degrees).
public class PlaceResolverTests
{
    private const double HomeLat = 31.0990;
    private const double HomeLon = -97.3410;
    private const double MetresPerDegreeOfLatitude = 6_371_008.8 * Math.PI / 180;

    private static readonly IReadOnlyList<RawPlace> HomeOnly = [new("home", "Hearth Haven", HomeLat, HomeLon, 100, false)];

    private static readonly IReadOnlyList<string> NoPreviousZones = [];

    // A latitude the given number of metres due north of the home zone's centre.
    private static double North(double metres) => HomeLat + (metres / MetresPerDegreeOfLatitude);

    private static PlaceMembership ResolveNorth(double metres, double? accuracyM, IReadOnlyList<RawPlace> zones, IReadOnlyCollection<string> previous) =>
        PlaceResolver.Resolve(North(metres), HomeLon, accuracyM, zones, previous);

    // ---- entering: inside the radius ------------------------------------------------------------------------

    [Theory]
    [InlineData(0.0, true)]
    [InlineData(99.9, true)]
    [InlineData(100.1, false)]
    [InlineData(120.0, false)]
    public void A_zone_is_entered_only_inside_its_radius(double metresFromCentre, bool inside)
    {
        var membership = ResolveNorth(metresFromCentre, 18, HomeOnly, NoPreviousZones);

        Assert.Equal(inside ? "home" : null, membership.PlaceId);
        Assert.Equal(inside, membership.ZoneIds.Count > 0);
    }

    // ---- leaving: the 30 m exit margin ----------------------------------------------------------------------

    // A zone the entity is already in is kept until radius + 30 m = 130 m, so GPS jitter at the edge does not flap.
    [Theory]
    [InlineData(100.1, true)]
    [InlineData(120.0, true)]
    [InlineData(129.9, true)]
    [InlineData(130.1, false)]
    [InlineData(250.0, false)]
    public void A_zone_is_kept_until_30_metres_outside_its_radius(double metresFromCentre, bool stays)
    {
        var membership = ResolveNorth(metresFromCentre, 18, HomeOnly, ["home"]);

        Assert.Equal(stays ? "home" : null, membership.PlaceId);
    }

    // The margin only applies to a zone already in the previous membership; another zone is still entered at its radius.
    [Fact]
    public void The_exit_margin_does_not_apply_to_a_zone_the_entity_was_not_in()
    {
        var withoutHistory = ResolveNorth(120, 18, HomeOnly, NoPreviousZones);
        var withOtherHistory = ResolveNorth(120, 18, HomeOnly, ["jester_hall"]);

        Assert.Null(withoutHistory.PlaceId);
        Assert.Null(withOtherHistory.PlaceId);
    }

    // A previous zone that no longer exists (deleted or hidden) is ignored.
    [Fact]
    public void A_previous_zone_that_is_gone_is_ignored()
    {
        var membership = ResolveNorth(0, 18, HomeOnly, ["deleted_zone"]);

        Assert.Equal(["home"], membership.ZoneIds);
    }

    // ---- poor accuracy hold ---------------------------------------------------------------------------------

    // A fix with an accuracy above 200 m cannot add or remove a zone: the membership stays as it was. Unknown
    // (null) counts as 100 m, so a vehicle (no accuracy) is never held.
    [Theory]
    [InlineData(250.0, true)]
    [InlineData(200.1, true)]
    [InlineData(200.0, false)]
    [InlineData(35.0, false)]
    [InlineData(null, false)]
    public void A_fix_worse_than_200_metres_holds_the_previous_membership(double? accuracyM, bool holds)
    {
        var enteringInside = ResolveNorth(0, accuracyM, HomeOnly, NoPreviousZones);
        var leavingFarAway = ResolveNorth(500, accuracyM, HomeOnly, ["home"]);

        Assert.Equal(holds ? null : "home", enteringInside.PlaceId);
        Assert.Equal(holds ? "home" : null, leavingFarAway.PlaceId);
    }

    // ---- smallest zone wins, nested zones -------------------------------------------------------------------

    private static readonly IReadOnlyList<RawPlace> NestedZones =
    [
        new("outer", "Campus", HomeLat, HomeLon, 200, false),
        new("inner", "Hall", HomeLat, HomeLon, 50, false),
    ];

    // Nested zones resolve to the inner one; between the inner edge and the outer edge only the outer zone holds.
    [Theory]
    [InlineData(10.0, "inner")]
    [InlineData(49.9, "inner")]
    [InlineData(50.1, "outer")]
    [InlineData(199.9, "outer")]
    public void Nested_zones_resolve_to_the_smallest_one_the_entity_is_in(double metresFromCentre, string expected)
    {
        var membership = ResolveNorth(metresFromCentre, 18, NestedZones, NoPreviousZones);

        Assert.Equal(expected, membership.PlaceId);
    }

    [Fact]
    public void An_entity_in_nested_zones_is_in_all_of_them_smallest_first()
    {
        var membership = ResolveNorth(10, 18, NestedZones, NoPreviousZones);

        Assert.Equal(["inner", "outer"], membership.ZoneIds);
    }

    // The never-drawn arrival zone (radius 32 187 m, centred on home) would contain home, but home is smaller.
    [Fact]
    public void The_arrival_zone_never_beats_the_zone_inside_it()
    {
        IReadOnlyList<RawPlace> zones = [new("approach", "Arrival", HomeLat, HomeLon, 32_187, false), .. HomeOnly];

        var atHome = ResolveNorth(0, 18, zones, NoPreviousZones);
        var fiveKilometresAway = ResolveNorth(5000, 18, zones, NoPreviousZones);

        Assert.Equal("home", atHome.PlaceId);
        Assert.Equal(["home", "approach"], atHome.ZoneIds);
        Assert.Equal("approach", fiveKilometresAway.PlaceId);
    }

    // The exit margin can keep a larger zone and a smaller one at once; the smallest still wins.
    [Fact]
    public void A_kept_small_zone_still_beats_a_larger_zone_that_contains_the_point()
    {
        var membership = ResolveNorth(60, 18, NestedZones, ["inner"]);

        Assert.Equal("inner", membership.PlaceId);
        Assert.Equal(["inner", "outer"], membership.ZoneIds);
    }

    // The two Work zones of the fixture (radius 150 each, 60 m apart): the equal-radius tie goes to the nearer centre.
    [Fact]
    public void Equal_zones_resolve_to_the_nearer_centre()
    {
        var work = DemoZoneTable.Drawn.Single(z => z.Id == "work");
        var work2 = DemoZoneTable.Drawn.Single(z => z.Id == "work_2");

        var atWork = PlaceResolver.Resolve(work.Lat, work.Lon, 18, DemoZoneTable.Drawn, NoPreviousZones);
        var atWork2 = PlaceResolver.Resolve(work2.Lat, work2.Lon, 18, DemoZoneTable.Drawn, NoPreviousZones);

        Assert.Equal("work", atWork.PlaceId);
        Assert.Equal(["work", "work_2"], atWork.ZoneIds);
        Assert.Equal("work_2", atWork2.PlaceId);
        Assert.Equal(["work_2", "work"], atWork2.ZoneIds);
    }

    // The last tie-break is the id, so identical zones give the same answer whatever order they are listed in.
    [Fact]
    public void Identical_zones_resolve_to_the_lowest_id()
    {
        IReadOnlyList<RawPlace> zones =
        [
            new("work_2", "Work", HomeLat, HomeLon, 100, false),
            new("work", "Work", HomeLat, HomeLon, 100, false),
        ];

        var membership = ResolveNorth(10, 18, zones, NoPreviousZones);

        Assert.Equal("work", membership.PlaceId);
    }

    // ---- the fixture instant --------------------------------------------------------------------------------

    // 02 section 9.3: the king and the pickup (7.78 m away) are at home, the jester is at the jester's hall, and the
    // queen and the cryptid are in no drawn zone: only the first two zones are occupied.
    [Theory]
    [InlineData(31.0990, -97.3410, "home")]
    [InlineData(31.09907, -97.34100, "home")]
    [InlineData(31.1040, -97.3560, "jester_hall")]
    [InlineData(31.0560, -97.4647, null)]
    [InlineData(31.3382, -94.7291, null)]
    public void Fixture_positions_resolve_to_the_places_of_the_spec(double lat, double lon, string? expectedPlaceId)
    {
        var membership = PlaceResolver.Resolve(lat, lon, 18, DemoZoneTable.Drawn, NoPreviousZones);

        Assert.Equal(expectedPlaceId, membership.PlaceId);
    }

    // ---- trip endpoints: the 75 m snap ----------------------------------------------------------------------

    // A car parked 120 m from a 100 m zone's centre is not inside the zone, but its trip still ends "at" the place.
    [Fact]
    public void A_trip_endpoint_120_metres_from_a_100_metre_zone_snaps_to_it()
    {
        Assert.Null(ResolveNorth(120, 18, HomeOnly, NoPreviousZones).PlaceId);
        Assert.Equal("home", PlaceResolver.SnapEndpoint(North(120), HomeLon, HomeOnly));
    }

    // The snap reaches radius + 75 m = 175 m from the centre.
    [Theory]
    [InlineData(50.0, "home")]
    [InlineData(174.9, "home")]
    [InlineData(175.1, null)]
    [InlineData(400.0, null)]
    public void A_trip_endpoint_snaps_within_75_metres_of_the_zone_edge(double metresFromCentre, string? expectedPlaceId)
    {
        Assert.Equal(expectedPlaceId, PlaceResolver.SnapEndpoint(North(metresFromCentre), HomeLon, HomeOnly));
    }

    [Fact]
    public void A_trip_endpoint_with_no_zones_snaps_to_nothing()
    {
        Assert.Null(PlaceResolver.SnapEndpoint(HomeLat, HomeLon, []));
    }

    // Among the qualifying zones the nearest edge wins (distance to the centre minus the radius), not the nearest
    // centre and not the smallest zone: here the small zone's edge is 10 m away and the large zone's is 10 m inside.
    [Fact]
    public void A_trip_endpoint_snaps_to_the_zone_with_the_nearest_edge()
    {
        IReadOnlyList<RawPlace> zones =
        [
            new("small", "Small", North(60), HomeLon, 50, false),
            new("large", "Large", North(-190), HomeLon, 200, false),
        ];

        // The endpoint is at the home point: 60 m from the small centre (edge +10 m) and 190 m from the large centre (edge -10 m).
        Assert.Equal("large", PlaceResolver.SnapEndpoint(HomeLat, HomeLon, zones));
    }

    // Both zones qualify. The first has the nearer centre (60 m against 130 m) but the farther edge (+40 m against
    // +30 m), so the second wins.
    [Fact]
    public void A_trip_endpoint_prefers_the_nearer_edge_over_the_nearer_centre()
    {
        IReadOnlyList<RawPlace> zones =
        [
            new("near_centre", "Near centre", North(60), HomeLon, 20, false),
            new("near_edge", "Near edge", North(130), HomeLon, 100, false),
        ];

        Assert.Equal("near_edge", PlaceResolver.SnapEndpoint(HomeLat, HomeLon, zones));
    }
}
