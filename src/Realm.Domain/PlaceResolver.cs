namespace Realm.Domain;

/// <summary>
/// Place membership by our own geometry (02 section 4.5), because Home Assistant's own zone state is
/// unreliable when zones overlap. Pass only the drawn zones: the caller has already dropped hidden zones and
/// those above the maximum radius.
/// </summary>
public static class PlaceResolver
{
    /// <summary>A zone an entity is already in is kept until it is this far outside its edge.</summary>
    public const double ExitMarginM = 30;

    /// <summary>A fix with a worse accuracy than this cannot add or remove a zone.</summary>
    public const double DecisionMaxAccuracyM = 200;

    /// <summary>How far outside a zone's edge a trip endpoint still counts as being at it.</summary>
    public const double EndpointSnapM = 75;

    /// <summary>
    /// The zones an entity at this position is in, given the zones it was in before. A zone is entered inside
    /// its radius and kept until 30 m outside it; a fix with an accuracy above 200 m changes nothing. The
    /// smallest zone is the place, then the nearest centre, then the lowest id.
    /// </summary>
    /// <param name="accuracyM">The fix accuracy; null (unknown) counts as 100 m, and a vehicle has none.</param>
    /// <param name="previousZoneIds">The zone ids from the previous call for this entity; empty at first.</param>
    public static PlaceMembership Resolve(
        double lat,
        double lon,
        double? accuracyM,
        IReadOnlyList<RawPlace> zones,
        IReadOnlyCollection<string> previousZoneIds)
    {
        var previous = new HashSet<string>(previousZoneIds, StringComparer.Ordinal);
        var measured = zones.Select(z => (Zone: z, Distance: Geo.DistanceM(lat, lon, z.Lat, z.Lon)));

        var inside = Fuse.EffectiveAccuracyM(accuracyM) > DecisionMaxAccuracyM
            ? measured.Where(x => previous.Contains(x.Zone.Id))
            : measured.Where(x => x.Distance <= x.Zone.RadiusM
                || (previous.Contains(x.Zone.Id) && x.Distance <= x.Zone.RadiusM + ExitMarginM));

        var ordered = inside
            .OrderBy(x => x.Zone.RadiusM)
            .ThenBy(x => x.Distance)
            .ThenBy(x => x.Zone.Id, StringComparer.Ordinal)
            .Select(x => x.Zone.Id)
            .ToList();

        return new PlaceMembership(ordered, ordered.Count > 0 ? ordered[0] : null);
    }

    /// <summary>
    /// The place a trip starts or ends at: a zone qualifies within 75 m of its edge, and the nearest edge wins
    /// (distance to the centre minus the radius), then the smaller radius. Null when no zone qualifies.
    /// </summary>
    public static string? SnapEndpoint(double lat, double lon, IReadOnlyList<RawPlace> zones)
    {
        return zones
            .Select(z => (Zone: z, Distance: Geo.DistanceM(lat, lon, z.Lat, z.Lon)))
            .Where(x => x.Distance <= x.Zone.RadiusM + EndpointSnapM)
            .OrderBy(x => x.Distance - x.Zone.RadiusM)
            .ThenBy(x => x.Zone.RadiusM)
            .ThenBy(x => x.Zone.Id, StringComparer.Ordinal)
            .Select(x => x.Zone.Id)
            .FirstOrDefault();
    }
}
