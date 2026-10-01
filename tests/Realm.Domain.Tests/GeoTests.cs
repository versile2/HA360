using Xunit;

namespace Realm.Domain.Tests;

// Expected distances are 02 section 9.3 (haversine from the king's point, R = 6 371 008.8 m), which Python
// reproduces: queen 12 713.9 m, jester 1 532.6 m, cryptid 249 788.9 m, prince 1 096 544.9 m, wagon 7.78 m.
public class GeoTests
{
    private const double KingLat = 31.0990;
    private const double KingLon = -97.3410;

    [Theory]
    [InlineData(31.0560, -97.4647, 12.71)]
    [InlineData(31.1040, -97.3560, 1.53)]
    [InlineData(31.3382, -94.7291, 249.79)]
    [InlineData(38.8339, -104.8214, 1096.54)]
    public void Distances_from_the_king_in_the_fixture(double lat, double lon, double expectedKilometres)
    {
        var metres = Geo.DistanceM(KingLat, KingLon, lat, lon);

        Assert.InRange(metres, (expectedKilometres * 1000) - 5, (expectedKilometres * 1000) + 5);
    }

    // The same distances to the centimetre, computed with Python's math using R = 6 371 008.8 m (02 section 0), so
    // a different earth radius is caught: it would move the 1 097 km leg by about 1.5 m.
    [Theory]
    [InlineData(31.0560, -97.4647, 12713.935)]
    [InlineData(31.1040, -97.3560, 1532.570)]
    [InlineData(31.3382, -94.7291, 249788.924)]
    [InlineData(38.8339, -104.8214, 1096544.938)]
    public void Distances_from_the_king_to_the_centimetre(double lat, double lon, double expectedMetres)
    {
        Assert.InRange(Geo.DistanceM(KingLat, KingLon, lat, lon), expectedMetres - 0.01, expectedMetres + 0.01);
    }

    // The pickup is 7.78 m from the king's point.
    [Fact]
    public void The_pickup_is_7_78_metres_from_the_king()
    {
        var metres = Geo.DistanceM(KingLat, KingLon, 31.09907, -97.34100);

        Assert.InRange(metres, 7.77, 7.79);
    }

    [Fact]
    public void The_distance_to_the_same_point_is_zero_and_is_symmetric()
    {
        Assert.Equal(0.0, Geo.DistanceM(KingLat, KingLon, KingLat, KingLon));
        Assert.Equal(
            Geo.DistanceM(KingLat, KingLon, 31.3382, -94.7291),
            Geo.DistanceM(31.3382, -94.7291, KingLat, KingLon),
            6);
    }
}
