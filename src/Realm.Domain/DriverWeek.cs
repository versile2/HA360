namespace Realm.Domain;

/// <summary>A driver's week: the summary tiles and the trips, newest first.</summary>
public record DriverWeek(
    DriverSummary Summary,
    IReadOnlyList<DriveVm> Trips);
