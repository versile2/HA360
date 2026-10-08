using System.Text.RegularExpressions;

namespace Realm.Domain;

/// <summary>A zone as the place labels need it: id, name and centre.</summary>
public sealed record LabelZone(string Id, string Name, double Lat, double Lon);

/// <summary>
/// Names the ends of a trip for the drive list (01 section 6.6, D113). A place is never "unknown": the name of the zone the end lies in, else the city of the address stored near it
/// ("Pinebrook"; "near Pinebrook" for a road without a house number; "I-65 near Pinebrook" for a highway), else the stored street (with "near {zone}" for a highway), and as the last resort the
/// zone nearest to the point ("near Hearth Haven"). Only when nothing at all is known does it say <see cref="Fallback"/>.
/// </summary>
public sealed partial class PlaceLabeler
{
    /// <summary>The label of an end about which nothing is known (no zone, no address, no coordinates, no zones).</summary>
    public const string Fallback = "Somewhere in the Realm";

    private static readonly Regex HighwayCode = HighwayCodeRegex();
    private static readonly Regex HighwayWords = HighwayWordsRegex();
    private static readonly Regex HouseNumber = HouseNumberRegex();

    private readonly IReadOnlyDictionary<string, LabelZone> _zones;
    private readonly IReadOnlyList<LabelZone> _list;
    private readonly Func<StatsTrip, bool, string?>? _addressOf;

    /// <param name="zones">The zones that exist now (a deleted zone is not here, so its id falls through to the address).</param>
    /// <param name="addressOf">The full stored address near an end of a trip (<c>true</c>: the end, <c>false</c>: the start), or null; resolved by the caller from the stored fixes.</param>
    public PlaceLabeler(IReadOnlyList<LabelZone> zones, Func<StatsTrip, bool, string?>? addressOf = null)
    {
        ArgumentNullException.ThrowIfNull(zones);
        _list = zones;
        _zones = zones.GroupBy(zone => zone.Id, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        _addressOf = addressOf;
    }

    /// <summary>A labeler over names only (no coordinates, so no "nearest zone"): the zone name or the stored street.</summary>
    public static PlaceLabeler FromNames(IReadOnlyDictionary<string, string> names) =>
        new([.. names.Select(pair => new LabelZone(pair.Key, pair.Value, double.NaN, double.NaN))]);

    /// <summary>The label of the start (<paramref name="end"/> false) or the end of a trip.</summary>
    public string Label(StatsTrip trip, bool end)
    {
        ArgumentNullException.ThrowIfNull(trip);
        var placeId = end ? trip.EndPlaceId : trip.StartPlaceId;
        var zoneName = placeId is not null && _zones.TryGetValue(placeId, out var zone) ? zone.Name : null;
        var lat = end ? trip.EndLat : trip.StartLat;
        var lon = end ? trip.EndLon : trip.StartLon;
        var address = zoneName is null ? _addressOf?.Invoke(trip, end) : null;
        return Name(zoneName, end ? trip.EndStreet : trip.StartStreet, address, lat, lon, _list);
    }

    /// <summary>The naming rules for one point (see the class remarks).</summary>
    public static string Name(string? zoneName, string? street, string? address, double? lat, double? lon, IReadOnlyList<LabelZone> zones)
    {
        if (!string.IsNullOrWhiteSpace(zoneName))
        {
            return zoneName;
        }

        var parsed = AddressParser.Parse(address);
        var road = (parsed?.Street ?? street)?.Trim();
        var nearest = Nearest(lat, lon, zones);
        var city = parsed?.City;
        if (!string.IsNullOrWhiteSpace(city))
        {
            return IsHighway(road) ? road + " near " + city
                : HasHouseNumber(road) ? city
                : "near " + city;
        }

        if (!string.IsNullOrWhiteSpace(road))
        {
            return IsHighway(road) && nearest is not null ? road + " near " + nearest.Name : road;
        }

        return nearest is not null ? "near " + nearest.Name : Fallback;
    }

    /// <summary>True for a highway designator ("I-65", "US 31", "AL-69", "State Route 3", "Highway 31").</summary>
    public static bool IsHighway(string? street) =>
        !string.IsNullOrWhiteSpace(street) && (HighwayCode.IsMatch(street.Trim()) || HighwayWords.IsMatch(street.Trim()));

    /// <summary>True when a street starts with a house number ("120 Eastgate Avenue").</summary>
    public static bool HasHouseNumber(string? street) => !string.IsNullOrWhiteSpace(street) && HouseNumber.IsMatch(street.Trim());

    private static LabelZone? Nearest(double? lat, double? lon, IReadOnlyList<LabelZone> zones)
    {
        if (lat is not { } latitude || lon is not { } longitude || double.IsNaN(latitude) || double.IsNaN(longitude))
        {
            return null;
        }

        LabelZone? best = null;
        var bestDistance = double.MaxValue;
        foreach (var zone in zones)
        {
            if (double.IsNaN(zone.Lat) || double.IsNaN(zone.Lon))
            {
                continue;
            }

            var distance = Geo.DistanceM(latitude, longitude, zone.Lat, zone.Lon);
            if (distance < bestDistance)
            {
                best = zone;
                bestDistance = distance;
            }
        }

        return best;
    }

    [GeneratedRegex(@"^(?:I|US|SR|CR|FM|RM|[A-Z]{2})[- ]?\d{1,4}[A-Z]?(?:\s+(?:[NSEW]|North|South|East|West)(?:bound)?)?$")]
    private static partial Regex HighwayCodeRegex();

    [GeneratedRegex(@"^(?:Interstate|Highway|Hwy|State Route|State Highway|County Road|Route|Rte|US Highway|U\.S\. Highway)\b", RegexOptions.IgnoreCase)]
    private static partial Regex HighwayWordsRegex();

    [GeneratedRegex(@"^\d+[A-Za-z]?\s")]
    private static partial Regex HouseNumberRegex();
}
