using System.Globalization;

namespace Realm.Web.Formatting;

/// <summary>The radius of a new place as the placement panel writes it (D120): metres, with the imperial value beside it, since the app shows feet and miles (D35).</summary>
public static class PlacementFormatter
{
    private const double FeetPerMeter = 3.280839895;
    private const double MetersPerMile = 1609.344;

    /// <summary>"100 m · 328 ft" under a kilometre, "1.5 km · 0.9 mi" from a kilometre.</summary>
    public static string Radius(double meters)
    {
        var culture = CultureInfo.InvariantCulture;
        if (meters < 1000)
        {
            return Math.Round(meters).ToString("0", culture) + " m · " + Math.Round(meters * FeetPerMeter).ToString("0", culture) + " ft";
        }

        return (meters / 1000).ToString("0.0#", culture) + " km · " + (meters / MetersPerMile).ToString("0.0", culture) + " mi";
    }
}
