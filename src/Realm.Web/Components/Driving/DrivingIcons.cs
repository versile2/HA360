using MudBlazor;
using Realm.Domain;
using Realm.Web.Formatting;

namespace Realm.Web.Components.Driving;

/// <summary>
/// The Material icon of each of the four event types and of the two other headline items (01 sections 6.3, 6.6 and 6.7): the popup, the event row and the chips of a
/// drive share it, so a type has one glyph on the whole Driving screen.
/// </summary>
internal static class DrivingIcons
{
    /// <summary><c>Speed</c> for speeding and Top Speed, <c>PhoneAndroid</c>, <c>Bolt</c>, <c>PanTool</c>, and <c>DirectionsCar</c> for the Drives card.</summary>
    public static string For(string key) =>
        key switch
        {
            EventKeys.Phone => Icons.Material.Filled.PhoneAndroid,
            EventKeys.Accel => Icons.Material.Filled.Bolt,
            EventKeys.Braking => Icons.Material.Filled.PanTool,
            StatNameFormatter.DrivesKey => Icons.Material.Filled.DirectionsCar,
            _ => Icons.Material.Filled.Speed,
        };
}
