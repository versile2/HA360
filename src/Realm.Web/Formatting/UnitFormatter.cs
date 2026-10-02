using System.Globalization;
using Realm.Domain;

namespace Realm.Web.Formatting;

/// <summary>
/// Speeds, distances, radii and percentages (01 section 8.3). v1 is imperial only (D35): every function takes the <see cref="UnitSystem"/> so metric is
/// one more branch in v1.1, and asks for anything else throws <see cref="NotSupportedException"/> rather than printing the wrong unit. Pure; the
/// numbers are rounded away from zero, as the map's strings are, and always printed with the invariant culture.
/// </summary>
public static class UnitFormatter
{
    private const double MetersPerMile = 1609.344;
    private const double FeetPerMeter = 3.280839895;
    private const double MilesPerHourPerMetersPerSecond = 2.2369362920544;

    /// <summary>The nearest 10 ft, for a distance under 0.1 mi (a place to the north, not a tape measure).</summary>
    private const double DistanceFeetStep = 10;

    /// <summary>"54 mph": a whole number.</summary>
    public static string Speed(double metersPerSecond, UnitSystem units = UnitSystem.Imperial)
    {
        RequireImperial(units);
        return Whole(metersPerSecond * MilesPerHourPerMetersPerSecond) + " mph";
    }

    /// <summary>Under 0.1 mi in feet rounded to 10, from 0.1 to 9.9 mi with one decimal, 10 mi and more whole: "320 ft", "1.0 mi", "155 mi".</summary>
    public static string Distance(double meters, UnitSystem units = UnitSystem.Imperial)
    {
        RequireImperial(units);
        var (value, feet, _) = Parts(meters, DistanceFeetStep);
        return feet ? value + " ft" : value + " mi";
    }

    /// <summary>The spoken form of <see cref="Distance"/>: "320 feet", "1.0 mile", "155 miles" (01 section 10.3).</summary>
    public static string DistanceWords(double meters, UnitSystem units = UnitSystem.Imperial)
    {
        RequireImperial(units);
        var (value, feet, singular) = Parts(meters, DistanceFeetStep);
        return feet ? value + " feet" : value + (singular ? " mile" : " miles");
    }

    /// <summary>"Radius 328 ft": feet to the foot under 0.1 mi, then miles as for <see cref="Distance"/> ("Radius 0.5 mi").</summary>
    public static string Radius(double meters, UnitSystem units = UnitSystem.Imperial)
    {
        RequireImperial(units);
        var (value, feet, _) = Parts(meters, 1);
        return feet ? "Radius " + value + " ft" : "Radius " + value + " mi";
    }

    /// <summary>"19%": the battery and the fuel level.</summary>
    public static string Percent(int value) => value.ToString(CultureInfo.InvariantCulture) + "%";

    private static void RequireImperial(UnitSystem units)
    {
        if (units != UnitSystem.Imperial)
        {
            throw new NotSupportedException("Metric units are deferred to v1.1 (D35); v1 formats imperial only.");
        }
    }

    private static string Whole(double value) => Math.Round(value, 0, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture);

    private static (string Value, bool Feet, bool Singular) Parts(double meters, double feetStep)
    {
        var distance = Math.Max(0, meters);
        var miles = distance / MetersPerMile;
        if (miles < 0.1)
        {
            var feet = Math.Round(distance * FeetPerMeter / feetStep, MidpointRounding.AwayFromZero) * feetStep;
            return (feet.ToString("0", CultureInfo.InvariantCulture), true, false);
        }

        var oneDecimal = Math.Round(miles, 1, MidpointRounding.AwayFromZero);
        return oneDecimal < 10
            ? (oneDecimal.ToString("0.0", CultureInfo.InvariantCulture), false, oneDecimal == 1)
            : (Whole(miles), false, false);
    }
}
