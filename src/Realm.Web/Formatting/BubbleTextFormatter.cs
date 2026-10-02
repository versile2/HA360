using System.Globalization;

namespace Realm.Web.Formatting;

/// <summary>
/// The edge-bubble strings of 01 sections 4.10 and 10.3: the accessible name and the tooltip of a single-member bubble, the same two for a cluster, and the compass
/// words. The caller passes the leg from the viewer to the member (distance in metres and the initial bearing in degrees clockwise from north) or null when there is
/// no reference point, so every function here is pure. The distance text is <see cref="UnitFormatter"/>'s (imperial only, D35).
/// </summary>
public static class BubbleTextFormatter
{
    private static readonly string[] Compass = ["north", "north-east", "east", "south-east", "south", "south-west", "west", "north-west"];

    /// <summary>"north", "north-east", ... for a bearing in degrees: the nearest of eight directions, a half step rounding clockwise (01 section 8.3).</summary>
    public static string CompassWord(double bearingDeg)
    {
        var index = (int)Math.Round((((bearingDeg % 360) + 360) % 360) / 45, MidpointRounding.AwayFromZero) % Compass.Length;
        return Compass[index];
    }

    /// <summary>"Dara, 155 miles east, off screen. Double tap to include on the map." Without a reference point the distance is left out.</summary>
    public static string Label(string name, (double Meters, double BearingDeg)? fromMe) =>
        fromMe is { } leg
            ? name + ", " + UnitFormatter.DistanceWords(leg.Meters) + " " + CompassWord(leg.BearingDeg) + ", off screen. Double tap to include on the map."
            : name + ", off screen. Double tap to include on the map.";

    /// <summary>"Dara · 155 mi east · tap to include on the map" (long-press or hover, 01 section 4.10 step 9).</summary>
    public static string Tooltip(string name, (double Meters, double BearingDeg)? fromMe) =>
        fromMe is { } leg
            ? name + " · " + UnitFormatter.Distance(leg.Meters) + " " + CompassWord(leg.BearingDeg) + " · tap to include on the map"
            : name + " · tap to include on the map";

    /// <summary>"2 people off screen: Dara, Elio. Double tap to include them on the map." (01 section 4.10, accessible name of a cluster bubble.)</summary>
    public static string ClusterLabel(IReadOnlyList<string> names) =>
        names.Count.ToString(CultureInfo.InvariantCulture) + " people off screen: " + string.Join(", ", names) + ". Double tap to include them on the map.";

    /// <summary>"Dara, Elio · tap to include them on the map": the tooltip of a cluster bubble, the names in the order of the bubble.</summary>
    public static string ClusterTooltip(IReadOnlyList<string> names) => string.Join(", ", names) + " · tap to include them on the map";
}
