namespace Realm.Domain;

/// <summary>
/// A closed trip with everything 02 section 5.5 computes at close. Phone use needs the phone signals, so a
/// detector output has PhoneCount null and no phone events until <see cref="PhoneUseDetector"/> is applied.
/// </summary>
/// <param name="DurationS">Whole seconds between the UTC instants, so a DST change inside the trip does not matter.</param>
/// <param name="StartPlaceId">The zone within 75 m of its edge at the start; null when none.</param>
/// <param name="StartStreet">The street of the Life360 address nearest in time within 250 m and 5 minutes; null when none.</param>
/// <param name="DistanceGpsM">The sum of the chords between consecutive track fixes, gaps included.</param>
/// <param name="TopSpeedMps">The corroborated top speed (a floor on the true one); null for a coarse trip or when no speed is usable.</param>
/// <param name="SpeedingCount">The sampled speeding episodes; null for a coarse trip.</param>
/// <param name="PhoneCount">Null until phone use has been applied, and for every trip it cannot be measured for.</param>
/// <param name="HasGap">A segment with more than 180 s between fixes, or a re-anchor jump.</param>
/// <param name="SourceMask">The sources that fed the track, sorted and comma-separated, for example "companion,life360".</param>
/// <param name="Track">The track fixes from StartUtc to EndUtc.</param>
public record DetectedTrip(
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    int DurationS,
    double StartLat,
    double StartLon,
    double EndLat,
    double EndLon,
    string? StartPlaceId,
    string? EndPlaceId,
    string? StartStreet,
    string? EndStreet,
    double DistanceGpsM,
    double? TopSpeedMps,
    DateTimeOffset? TopSpeedAtUtc,
    string? TopSpeedStreet,
    int? SpeedingCount,
    int? PhoneCount,
    TripQuality Quality,
    bool HasGap,
    TripEndedBy EndedBy,
    string SourceMask,
    IReadOnlyList<TrackPoint> Track,
    IReadOnlyList<SpeedingEpisode> SpeedingEpisodes,
    IReadOnlyList<PhoneUseEvent> PhoneEvents);
