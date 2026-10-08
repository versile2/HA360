namespace Realm.Domain;

/// <summary>Where a roster entry lives (Settings, "Who's on the map"): on the map as a person, on the map as a vehicle, or nowhere.</summary>
public enum RosterGroup
{
    /// <summary>On the map, in the People list, the Driving report and the stored history.</summary>
    People,

    /// <summary>On the map and in the Vehicles list from the tracker's position; no history is stored.</summary>
    Vehicles,

    /// <summary>Not on the map, in a list or in a report, and no new history is stored.</summary>
    NotTracked,
}
