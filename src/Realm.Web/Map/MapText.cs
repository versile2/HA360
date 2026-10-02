using System.Globalization;
using System.Text;
using Realm.Domain;

namespace Realm.Web.Map;

/// <summary>
/// The display strings that travel in the map payloads: pin names, tooltips, bubble labels (01 sections 4.10, 8.3 and 10.3). These are
/// the parts of the 01 strings that need only the view models; the "since" and "updated" times and the distance of a pin name belong to
/// S7's <c>MemberTextFormatter</c> and <c>UnitFormatter</c>, which replace these builders, and the edge-bubble strings are
/// <see cref="Formatting.BubbleTextFormatter"/>'s. Imperial only (D35).
/// </summary>
internal static class MapText
{
    private const string SomewhereInTheRealm = "Somewhere in the Realm";

    /// <summary>The first letter of the display name, shown on the member colour when there is no photo.</summary>
    public static string Initial(string displayName)
    {
        var trimmed = displayName.Trim();
        return trimmed.Length == 0 ? "?" : StringInfo.GetNextTextElement(trimmed).ToUpperInvariant();
    }

    /// <summary>"{Name} · {Lore}", or the name alone.</summary>
    public static string Title(string name, string? lore) => string.IsNullOrWhiteSpace(lore) ? name : name + " · " + lore;

    /// <summary>The pin's accessible name: "Alden, The King. At Hearth Haven. Battery 19 percent, charging." (01 section 10.3 without the time and distance parts).</summary>
    public static string MemberPinName(MemberVm member, MemberStatus status, PlaceVm? place, bool poorAccuracy, bool lowBattery)
    {
        var name = new StringBuilder(string.IsNullOrWhiteSpace(member.LoreTitle) ? member.DisplayName : member.DisplayName + ", " + member.LoreTitle);
        name.Append(". ").Append(StatusSentence(member, status, place, poorAccuracy)).Append('.');
        if (member.BatteryPct is { } battery && status != MemberStatus.Static)
        {
            name.Append(" Battery ").Append(battery.ToString(CultureInfo.InvariantCulture)).Append(" percent");
            if (member.Charging == true)
            {
                name.Append(", charging");
            }

            if (lowBattery)
            {
                name.Append(", low");
            }

            name.Append('.');
        }

        return name.ToString();
    }

    /// <summary>The accessible name of a vehicle pin: "Ford Pickup, The King's Wagon. At Hearth Haven. Engine off. Fuel 71 percent." (01 section 10.3 without the update time).</summary>
    public static string VehiclePinName(VehicleVm vehicle, PlaceVm? place)
    {
        var name = new StringBuilder(string.IsNullOrWhiteSpace(vehicle.LoreTitle) ? vehicle.Name : vehicle.Name + ", " + vehicle.LoreTitle);
        if (vehicle.IsPlaceholder)
        {
            return name.Append(". ").Append(vehicle.PlaceholderNote ?? "Location unavailable").Append('.').ToString();
        }

        var location = vehicle.Lat is null || vehicle.Lon is null
            ? "Location unavailable"
            : place is not null ? "At " + place.DisplayName
            : !string.IsNullOrWhiteSpace(vehicle.Street) ? vehicle.Street
            : SomewhereInTheRealm;
        name.Append(". ").Append(location).Append('.');
        if (EngineText(vehicle) is { } engine)
        {
            name.Append(' ').Append(engine).Append('.');
        }

        if (vehicle.FuelPct is { } fuel)
        {
            name.Append(" Fuel ").Append(fuel.ToString(CultureInfo.InvariantCulture)).Append(" percent.");
        }

        return name.ToString();
    }

    /// <summary>Distance and initial bearing (degrees clockwise from north) from the first point to the second.</summary>
    public static (double Meters, double BearingDeg) Leg(double fromLat, double fromLon, double toLat, double toLon)
    {
        var phi1 = fromLat * Math.PI / 180;
        var phi2 = toLat * Math.PI / 180;
        var deltaLambda = (toLon - fromLon) * Math.PI / 180;
        var y = Math.Sin(deltaLambda) * Math.Cos(phi2);
        var x = (Math.Cos(phi1) * Math.Sin(phi2)) - (Math.Sin(phi1) * Math.Cos(phi2) * Math.Cos(deltaLambda));
        var bearing = (Math.Atan2(y, x) * 180 / Math.PI) + 360;
        return (Geo.DistanceM(fromLat, fromLon, toLat, toLon), bearing % 360);
    }

    private static string StatusSentence(MemberVm member, MemberStatus status, PlaceVm? place, bool poorAccuracy) => status switch
    {
        MemberStatus.NoFix => "Location unavailable",
        MemberStatus.Static => string.IsNullOrWhiteSpace(member.StaticLabel) ? "Fixed position" : member.StaticLabel,
        MemberStatus.Offline => "Offline",
        MemberStatus.Stale => "Position is stale",
        MemberStatus.Driving => string.IsNullOrWhiteSpace(member.Street) ? "Driving" : "Driving on " + member.Street,
        MemberStatus.AtPlace when place is not null => "At " + place.DisplayName,
        _ => string.IsNullOrWhiteSpace(member.Street)
            ? SomewhereInTheRealm
            : poorAccuracy ? "Near " + member.Street : member.Street,
    };

    private static string? EngineText(VehicleVm vehicle) => vehicle.Ignition switch
    {
        IgnitionState.Off => "Engine off",
        IgnitionState.Accessory => "Accessory on",
        IgnitionState.On => "Engine on",
        IgnitionState.RemoteStart when vehicle.RemoteStartSecondsLeft is { } seconds =>
            "Remote start · " + ((seconds + 59) / 60).ToString(CultureInfo.InvariantCulture) + " min left",
        IgnitionState.RemoteStart => "Remote start",
        _ => null,
    };
}
