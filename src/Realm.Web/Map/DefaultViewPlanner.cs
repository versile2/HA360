using Realm.Domain;

namespace Realm.Web.Map;

/// <summary>
/// The default camera of 01 section 4.9: which members and vehicles the opening view frames, and the bounds around them. Pure; the
/// candidates are decided here and JavaScript only performs the fit (03 section 4.5 <c>DefaultTargets</c>).
/// </summary>
internal static class DefaultViewPlanner
{
    private const double MetersPerDegree = Geo.EarthRadiusM * Math.PI / 180;

    /// <summary>"At least 800 m" (01 section 4.9 step 4), aimed a hair above so the planar growth cannot land under it.</summary>
    private const double MinDiagonalM = 800;
    private const double DiagonalTarget = MinDiagonalM * 1.001;
    private const int FitMaxZoom = 16;
    private const int MeZoom = 16;

    /// <summary>
    /// My position (step 1): the viewer's own fix, or the home zone when I have none. Null when neither exists.
    /// </summary>
    public static (double Lat, double Lon)? Origin(IReadOnlyList<MemberVm> members, IReadOnlyList<PlaceVm> places, string? meId)
    {
        var me = meId is null ? null : members.FirstOrDefault(member => member.Id == meId);
        if (me is { Lat: { } lat, Lon: { } lon } && me.Freshness != Freshness.NoFix)
        {
            return (lat, lon);
        }

        var home = places.FirstOrDefault(place => place.Kind == PlaceKind.Home);
        return home is null ? null : (home.Lat, home.Lon);
    }

    /// <summary>The targets, or null when there is no position that stands for me.</summary>
    public static DefaultTargets? Plan(
        IReadOnlyList<MemberVm> members,
        IReadOnlyList<VehicleVm> vehicles,
        IReadOnlyList<PlaceVm> places,
        string? meId,
        MapPayloadOptions options,
        int version)
    {
        if (Origin(members, places, meId) is not { } origin)
        {
            return null;
        }

        var radiusM = options.DefaultViewRadiusKm * 1000;
        var points = new List<(double Lat, double Lon)> { origin };

        // Step 2: every other member with a live fix (the static member too), and every vehicle with a position, inside the radius.
        foreach (var member in members)
        {
            if (member.Id != meId && Position(member) is { } at && Geo.DistanceM(origin.Lat, origin.Lon, at.Lat, at.Lon) <= radiusM)
            {
                points.Add(at);
            }
        }

        foreach (var vehicle in vehicles)
        {
            if (Position(vehicle) is { } at && Geo.DistanceM(origin.Lat, origin.Lon, at.Lat, at.Lon) <= radiusM)
            {
                points.Add(at);
            }
        }

        // Step 3 (R-014): only me qualified, so the single nearest live member joins whatever the radius.
        if (points.Count < 2 && NearestLiveMember(members, meId, origin) is { } nearest)
        {
            points.Add(nearest);
        }

        var (south, west, north, east) = Bounds(points);
        var (s, w, n, e) = EnsureDiagonal(south, west, north, east);
        return new DefaultTargets(
            version,
            new DefaultView([[w, s], [e, n]], FitMaxZoom),
            new MeTarget([origin.Lon, origin.Lat], MeZoom));
    }

    // A member counts when someone can see the pin and the position is current: a live member that is fresh or stale, or the static member.
    private static (double Lat, double Lon)? Position(MemberVm member) =>
        member.Freshness is (Freshness.Fresh or Freshness.Stale or Freshness.Static) && member.Lat is { } lat && member.Lon is { } lon
            ? (lat, lon)
            : null;

    private static (double Lat, double Lon)? Position(VehicleVm vehicle) =>
        vehicle.Freshness != Freshness.NoFix && vehicle.Lat is { } lat && vehicle.Lon is { } lon
            ? (lat, lon)
            : null;

    private static (double Lat, double Lon)? NearestLiveMember(IReadOnlyList<MemberVm> members, string? meId, (double Lat, double Lon) origin)
    {
        (double Lat, double Lon)? best = null;
        var bestDistance = double.PositiveInfinity;
        foreach (var member in members)
        {
            if (member.Id == meId || member.Kind != MemberKind.Live || member.Freshness is not (Freshness.Fresh or Freshness.Stale))
            {
                continue;
            }

            if (Position(member) is { } at)
            {
                var distance = Geo.DistanceM(origin.Lat, origin.Lon, at.Lat, at.Lon);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = at;
                }
            }
        }

        return best;
    }

    private static (double South, double West, double North, double East) Bounds(List<(double Lat, double Lon)> points) =>
        (points.Min(p => p.Lat), points.Min(p => p.Lon), points.Max(p => p.Lat), points.Max(p => p.Lon));

    // Step 4: grown by the same distance on every side until the diagonal is at least MinDiagonalM, solving
    // (w + 2d)^2 + (h + 2d)^2 = target^2 for d on the local metre grid.
    private static (double South, double West, double North, double East) EnsureDiagonal(double south, double west, double north, double east)
    {
        if (Geo.DistanceM(south, west, north, east) >= MinDiagonalM)
        {
            return (south, west, north, east);
        }

        var metersPerLonDegree = MetersPerDegree * Math.Cos((south + north) / 2 * Math.PI / 180);
        var heightM = (north - south) * MetersPerDegree;
        var widthM = (east - west) * metersPerLonDegree;
        var sum = widthM + heightM;
        var rest = (widthM * widthM) + (heightM * heightM) - (DiagonalTarget * DiagonalTarget);
        var growM = (-4 * sum + Math.Sqrt((16 * sum * sum) - (32 * rest))) / 16;
        var dLat = growM / MetersPerDegree;
        var dLon = growM / metersPerLonDegree;
        return (south - dLat, west - dLon, north + dLat, east + dLon);
    }
}
