namespace Realm.Domain;

/// <summary>One sampled speeding episode (02 section 5.8): the trip_events row of kind speeding.</summary>
/// <param name="StartUtc">The first fix of the episode.</param>
/// <param name="EndUtc">The last fix still within the hysteresis band.</param>
/// <param name="PeakMps">The highest corroborated speed of the episode.</param>
/// <param name="Lat">The position of the first fix.</param>
public record SpeedingEpisode(
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    double PeakMps,
    double Lat,
    double Lon);
