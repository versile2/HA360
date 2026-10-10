using Realm.Domain;

namespace Realm.Web.Formatting;

/// <summary>The radius of a new place as the placement panel writes it (0.2.3, D122): in the units of the owner's Home Assistant, one unit only (see <see cref="RadiusUnits"/>).</summary>
public static class PlacementFormatter
{
    /// <summary>"100 m" and "1.5 km" (metric), "328 ft" and "1.23 mi" (imperial).</summary>
    public static string Radius(double meters, LengthUnits units = LengthUnits.Metric) => RadiusUnits.Text(units, meters);
}
