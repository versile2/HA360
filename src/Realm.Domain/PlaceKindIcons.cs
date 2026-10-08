namespace Realm.Domain;

/// <summary>
/// The icon of each <see cref="PlaceKind"/> as Home Assistant names it, for the places the owner adds (0.2.2, D119), and the label the picker shows. The same kinds
/// draw the rows of the Places list (01 section 5.3), so a place keeps its look from the picker to the list.
/// </summary>
public static class PlaceKindIcons
{
    /// <summary>The kinds the picker offers, in the order it shows them.</summary>
    public static IReadOnlyList<PlaceKind> Pickable { get; } =
        [PlaceKind.Home, PlaceKind.Work, PlaceKind.Family, PlaceKind.Fun, PlaceKind.Park, PlaceKind.Vet, PlaceKind.Cemetery, PlaceKind.Other];

    /// <summary>The Material Design Icons name Home Assistant stores for the kind (<c>mdi:...</c>).</summary>
    public static string HaIcon(PlaceKind kind) =>
        kind switch
        {
            PlaceKind.Home => "mdi:home",
            PlaceKind.Work => "mdi:briefcase",
            PlaceKind.Family => "mdi:home-heart",
            PlaceKind.Fun => "mdi:roller-skate",
            PlaceKind.Park => "mdi:tree",
            PlaceKind.Vet => "mdi:paw",
            PlaceKind.Cemetery => "mdi:grave-stone",
            _ => "mdi:map-marker",
        };

    /// <summary>The kind a Home Assistant icon stands for; <see cref="PlaceKind.Other"/> for any icon that is not one of the picker's.</summary>
    public static PlaceKind KindOf(string? haIcon)
    {
        foreach (var kind in Pickable)
        {
            if (string.Equals(HaIcon(kind), haIcon, StringComparison.Ordinal))
            {
                return kind;
            }
        }

        return PlaceKind.Other;
    }

    /// <summary>The word the picker shows under the icon.</summary>
    public static string Label(PlaceKind kind) =>
        kind switch
        {
            PlaceKind.Home => "Home",
            PlaceKind.Work => "Work",
            PlaceKind.Family => "Family",
            PlaceKind.Fun => "Fun",
            PlaceKind.Park => "Park",
            PlaceKind.Vet => "Vet",
            PlaceKind.Cemetery => "Cemetery",
            _ => "Other",
        };
}
