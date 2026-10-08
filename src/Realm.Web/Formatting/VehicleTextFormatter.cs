using System.Text;
using Realm.Domain;

namespace Realm.Web.Formatting;

/// <summary>
/// The vehicle strings of 01 sections 5.2, 8.4 and 10.3: the location line, the update line and the accessible name. Pure; the
/// caller passes the display name of the zone the vehicle is in (or null) and the session's clock and zone.
/// </summary>
public static class VehicleTextFormatter
{
    /// <summary>A vehicle with no position (<c>Freshness = NoFix</c>, no coordinates): not the O-2 fallback, which needs a position.</summary>
    public const string LocationUnavailable = "Location unavailable";

    /// <summary>A position but no place and no street (O-2).</summary>
    public const string SomewhereInTheRealm = "Somewhere in the Realm";

    /// <summary>
    /// The location line (L2 of a live vehicle): "Location unavailable" without a position, "Driving · 54 mph" while <see cref="VehicleVm.IsMoving"/> (the speed only when
    /// <see cref="VehicleVm.SpeedMps"/> is known), "At Hearth Haven", the street, or "Somewhere in the Realm".
    /// </summary>
    public static string Location(VehicleVm vehicle, string? placeName, UnitSystem units = UnitSystem.Imperial)
    {
        if (vehicle.Freshness == Freshness.NoFix || vehicle.Lat is null || vehicle.Lon is null)
        {
            return LocationUnavailable;
        }

        if (vehicle.IsMoving)
        {
            return vehicle.SpeedMps is { } speed ? "Driving · " + UnitFormatter.Speed(speed, units) : "Driving";
        }

        if (placeName is not null)
        {
            return "At " + placeName;
        }

        return string.IsNullOrWhiteSpace(vehicle.Street) ? SomewhereInTheRealm : vehicle.Street;
    }

    /// <summary>
    /// L4: "Updated 20 min ago", or "Last heard 1 hr ago" when the vehicle is stale (01 section 5.2). Empty when no update time is known.
    /// </summary>
    public static string Updated(VehicleVm vehicle, DateTimeOffset now, TimeZoneInfo zone) =>
        vehicle.LastUpdateUtc is { } at
            ? (vehicle.Freshness == Freshness.Stale ? "Last heard " : "Updated ") + TimeFormatter.Relative(at, now, zone)
            : string.Empty;

    /// <summary>
    /// The accessible name (01 section 10.3): "Ford Pickup, The King's Wagon. At Hearth Haven. Updated 20 min ago."
    /// </summary>
    public static string AccessibleName(VehicleVm vehicle, string location, string updated)
    {
        var name = new StringBuilder(string.IsNullOrWhiteSpace(vehicle.LoreTitle) ? vehicle.Name : vehicle.Name + ", " + vehicle.LoreTitle).Append(". ").Append(location).Append('.');
        if (updated.Length > 0)
        {
            name.Append(' ').Append(updated).Append('.');
        }

        return name.ToString();
    }
}
