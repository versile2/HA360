using Xunit;

namespace Realm.Domain.Tests;

public class PlaceLabelerTests
{
    private static readonly LabelZone Home = new("home", "Hearth Haven", 31.0990, -85.3410);
    private static readonly LabelZone Work = new("work", "Cobblestone Court", 31.2200, -85.4000);

    private static readonly IReadOnlyList<LabelZone> Zones = [Home, Work];

    private static StatsTrip Trip(string? startPlace = null, string? endPlace = null, string? startStreet = null, string? endStreet = null, double? endLat = null, double? endLon = null) =>
        new("alden", new DateTimeOffset(2026, 9, 29, 14, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 9, 29, 14, 20, 0, TimeSpan.Zero), 1000, TripQuality.Dense, DistanceBasis.Gps, 20, null, null, 0, 0,
            startPlace, endPlace, startStreet, endStreet, 31.0991, -85.3411, endLat, endLon);

    [Fact]
    public void A_zone_that_exists_names_the_end_whatever_else_is_known()
    {
        var labeler = new PlaceLabeler(Zones, (_, _) => "12 Elm Street, Pinebrook, AL");

        Assert.Equal("Hearth Haven", labeler.Label(Trip(startPlace: "home", startStreet: "Elm Street"), end: false));
    }

    [Theory]
    [InlineData("12 Elm Street, Pinebrook, AL", "Pinebrook")]
    [InlineData("Elm Street, Pinebrook, AL, USA", "near Pinebrook")]
    [InlineData("I-65, Pinebrook, AL", "I-65 near Pinebrook")]
    [InlineData("I-65 S, Pinebrook, AL", "I-65 S near Pinebrook")]
    [InlineData("US-31, Pinebrook, AL", "US-31 near Pinebrook")]
    [InlineData("State Route 3, Pinebrook, AL", "State Route 3 near Pinebrook")]
    public void Without_a_zone_the_end_is_named_by_the_city_of_its_address(string address, string expected)
    {
        Assert.Equal(expected, PlaceLabeler.Name(null, null, address, 31.1, -85.3, Zones));
    }

    [Fact]
    public void A_zone_id_that_no_longer_exists_falls_through_to_the_address()
    {
        var labeler = new PlaceLabeler(Zones, (_, end) => end ? "I-65, Pinebrook, AL" : null);

        Assert.Equal("I-65 near Pinebrook", labeler.Label(Trip(endPlace: "gone"), end: true));
    }

    [Fact]
    public void With_only_a_street_the_street_names_it_and_a_highway_adds_the_nearest_zone()
    {
        Assert.Equal("Eastgate Avenue", PlaceLabeler.Name(null, "Eastgate Avenue", null, 31.1, -85.34, Zones));
        Assert.Equal("I-65 near Hearth Haven", PlaceLabeler.Name(null, "I-65", null, 31.1, -85.34, Zones));
        Assert.Equal("I-65", PlaceLabeler.Name(null, "I-65", null, null, null, []));
    }

    [Fact]
    public void With_nothing_but_a_point_the_nearest_zone_is_named()
    {
        Assert.Equal("near Hearth Haven", PlaceLabeler.Name(null, null, null, 31.1, -85.34, Zones));
        Assert.Equal("near Cobblestone Court", PlaceLabeler.Name(null, null, null, 31.21, -85.39, Zones));
    }

    [Fact]
    public void Nothing_known_at_all_is_never_unknown_place()
    {
        Assert.Equal(PlaceLabeler.Fallback, PlaceLabeler.Name(null, null, null, null, null, Zones));
        Assert.Equal(PlaceLabeler.Fallback, PlaceLabeler.Name(null, " ", "", 31.1, -85.3, []));
        Assert.DoesNotContain("Unknown", PlaceLabeler.Fallback, StringComparison.Ordinal);
        Assert.Equal(PlaceLabeler.Fallback, PlaceLabeler.FromNames(new Dictionary<string, string>()).Label(Trip(), end: true));
    }

    [Theory]
    [InlineData("I-65", true)]
    [InlineData("AL-69", true)]
    [InlineData("US 31", true)]
    [InlineData("Interstate 65", true)]
    [InlineData("Highway 31", true)]
    [InlineData("Eastgate Avenue", false)]
    [InlineData("1 Main Street", false)]
    [InlineData("", false)]
    public void Highways_are_told_from_ordinary_streets(string street, bool highway)
    {
        Assert.Equal(highway, PlaceLabeler.IsHighway(street));
    }

    [Fact]
    public void The_end_point_decides_the_nearest_zone_for_the_end_and_the_start_point_for_the_start()
    {
        var labeler = new PlaceLabeler(Zones);
        var trip = Trip(endLat: 31.21, endLon: -85.39);

        Assert.Equal("near Hearth Haven", labeler.Label(trip, end: false));
        Assert.Equal("near Cobblestone Court", labeler.Label(trip, end: true));
    }
}
