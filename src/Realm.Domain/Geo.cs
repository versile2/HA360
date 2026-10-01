namespace Realm.Domain;

/// <summary>Great-circle distance on a sphere, the one distance function of the data layer.</summary>
public static class Geo
{
    /// <summary>Mean earth radius in metres.</summary>
    public const double EarthRadiusM = 6_371_008.8;

    /// <summary>Haversine distance in metres between two points given in degrees.</summary>
    public static double DistanceM(double lat1, double lon1, double lat2, double lon2)
    {
        var phi1 = ToRadians(lat1);
        var phi2 = ToRadians(lat2);
        var halfDeltaPhi = (phi2 - phi1) / 2;
        var halfDeltaLambda = ToRadians(lon2 - lon1) / 2;
        var a = (Math.Sin(halfDeltaPhi) * Math.Sin(halfDeltaPhi))
            + (Math.Cos(phi1) * Math.Cos(phi2) * Math.Sin(halfDeltaLambda) * Math.Sin(halfDeltaLambda));
        return 2 * EarthRadiusM * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180;
}
