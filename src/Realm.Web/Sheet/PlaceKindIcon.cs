using MudBlazor;
using Realm.Domain;

namespace Realm.Web.Sheet;

/// <summary>The MudBlazor icon of a place kind (01 section 5.3), shared by the Places rows and the place picker so a place keeps its look.</summary>
public static class PlaceKindIcon
{
    /// <summary>Home, work, family, fun, park, vet and cemetery have their own icon; anything else is a pin.</summary>
    public static string Material(PlaceKind kind) =>
        kind switch
        {
            PlaceKind.Home => Icons.Material.Filled.Home,
            PlaceKind.Work => Icons.Material.Filled.Work,
            PlaceKind.Family => Icons.Material.Filled.Cottage,
            PlaceKind.Fun => Icons.Material.Filled.RollerSkating,
            PlaceKind.Park => Icons.Material.Filled.Park,
            PlaceKind.Vet => Icons.Material.Filled.Pets,
            PlaceKind.Cemetery => Icons.Material.Filled.Church,
            _ => Icons.Material.Filled.Place,
        };
}
