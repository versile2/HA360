using System.Globalization;
using System.Text;
using Realm.Domain;

namespace Realm.Web.Formatting;

/// <summary>
/// The vehicle strings of 01 sections 5.2, 8.4 and 10.3: the location line, the engine and fuel parts of L3, the update line and the accessible name. Pure; the
/// caller passes the display name of the zone the vehicle is in (or null) and the session's clock and zone.
/// </summary>
public static class VehicleTextFormatter
{
    /// <summary>A vehicle with no position (<c>Freshness = NoFix</c>, no coordinates): not the O-2 fallback, which needs a position.</summary>
    public const string LocationUnavailable = "Location unavailable";

    /// <summary>A position but no place and no street (O-2).</summary>
    public const string SomewhereInTheRealm = "Somewhere in the Realm";

    /// <summary>The popover of the placeholder vehicle's info button (01 sections 5.2 and 8.4): the maker has no Home Assistant integration yet.</summary>
    public const string PlaceholderExplanation = "This vehicle's maker has no official Home Assistant integration yet. When one exists, the Chariot will appear on the map.";

    /// <summary>A fuel level under this percentage reads "Low fuel 12%" in the error colour.</summary>
    public const int LowFuelPercent = 15;

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

    /// <summary>"Engine off", "Engine on", "Accessory on", "Remote start · 8 min left" (minutes rounded up); null when the ignition is unknown.</summary>
    public static string? Engine(VehicleVm vehicle) =>
        vehicle.Ignition switch
        {
            IgnitionState.Off => "Engine off",
            IgnitionState.Accessory => "Accessory on",
            IgnitionState.On => "Engine on",
            IgnitionState.RemoteStart when vehicle.RemoteStartSecondsLeft is { } seconds =>
                "Remote start · " + ((seconds + 59) / 60).ToString(CultureInfo.InvariantCulture) + " min left",
            IgnitionState.RemoteStart => "Remote start",
            _ => null,
        };

    /// <summary>True when <paramref name="fuelPct"/> is under <see cref="LowFuelPercent"/>.</summary>
    public static bool IsLowFuel(int fuelPct) => fuelPct < LowFuelPercent;

    /// <summary>"Fuel 71%", or "Low fuel 12%" under <see cref="LowFuelPercent"/>.</summary>
    public static string Fuel(int fuelPct) => (IsLowFuel(fuelPct) ? "Low fuel " : "Fuel ") + UnitFormatter.Percent(fuelPct);

    /// <summary>
    /// L4: "Updated 20 min ago", or "Last heard 1 hr ago" when the vehicle is stale (01 section 5.2). Empty when no update time is known.
    /// </summary>
    public static string Updated(VehicleVm vehicle, DateTimeOffset now, TimeZoneInfo zone) =>
        vehicle.LastUpdateUtc is { } at
            ? (vehicle.Freshness == Freshness.Stale ? "Last heard " : "Updated ") + TimeFormatter.Relative(at, now, zone)
            : string.Empty;

    /// <summary>
    /// The accessible name (01 section 10.3): "Ford Pickup, The King's Wagon. At Hearth Haven. Engine off. Fuel 71 percent. Updated 20 min ago." The placeholder reads its note
    /// only.
    /// </summary>
    public static string AccessibleName(VehicleVm vehicle, string location, string? engine, string updated)
    {
        var name = new StringBuilder(string.IsNullOrWhiteSpace(vehicle.LoreTitle) ? vehicle.Name : vehicle.Name + ", " + vehicle.LoreTitle).Append(". ").Append(location).Append('.');
        if (vehicle.IsPlaceholder)
        {
            return name.ToString();
        }

        if (engine is not null)
        {
            name.Append(' ').Append(engine.Replace(" · ", ", ", StringComparison.Ordinal)).Append('.');
        }

        if (vehicle.FuelPct is { } fuel)
        {
            name.Append(" Fuel ").Append(fuel.ToString(CultureInfo.InvariantCulture)).Append(" percent").Append(IsLowFuel(fuel) ? ", low." : ".");
        }

        if (updated.Length > 0)
        {
            name.Append(' ').Append(updated).Append('.');
        }

        return name.ToString();
    }
}
