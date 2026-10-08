namespace Realm.Domain;

/// <summary>The columns of a stored trip that the weekly statistics rules read (02 section 7.2). Null counts mean unknown, never 0.</summary>
/// <param name="StartUtc">Decides the week: the week of the local date and time of the start.</param>
/// <param name="Meters">trips.distance_m.</param>
/// <param name="TopSpeedMps">Null for a coarse trip.</param>
/// <param name="TopSpeedAtUtc">When the top speed was reached; null when TopSpeedMps is null.</param>
/// <param name="SpeedingCount">Null for a coarse trip.</param>
/// <param name="PhoneCount">Null when the trip predates the phone sensor or phone use could not be measured.</param>
/// <param name="StartLat">The start and end points, to name an end by its nearest zone or address (<see cref="PlaceLabeler"/>); null when unknown.</param>
public record StatsTrip(
    string MemberId,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    double Meters,
    TripQuality Quality,
    DistanceBasis DistanceBasis,
    double? TopSpeedMps,
    DateTimeOffset? TopSpeedAtUtc,
    string? TopSpeedStreet,
    int? SpeedingCount,
    int? PhoneCount,
    string? StartPlaceId,
    string? EndPlaceId,
    string? StartStreet,
    string? EndStreet,
    double? StartLat = null,
    double? StartLon = null,
    double? EndLat = null,
    double? EndLon = null);
