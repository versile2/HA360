namespace Realm.Domain;

/// <summary>The trip detection parameters of 02 section 5.3, with the defaults of its table. Seconds, metres and metres per second.</summary>
/// <param name="StartSpeedMps">Option trips_start_speed_mph. Also the least top speed of a valid trip.</param>
/// <param name="StartMaxGapS">The consecutive fast fixes must be this close in time.</param>
/// <param name="StartMinDisplacementM">The distance from the anchor that the last fast fix must reach.</param>
/// <param name="FlagAssistDisplacementM">A Life360 driving flag starts a trip from this far from the anchor.</param>
/// <param name="DepartureLookbackS">How far the start is back-dated.</param>
/// <param name="DepartureSnapM">The departure fix must be within this of the anchor.</param>
/// <param name="StopMergeS">Option trips_stop_merge_seconds: a stop shorter than this is part of the trip.</param>
/// <param name="NoFixEndS">No fix at all for this long while driving closes the trip at its last fix.</param>
/// <param name="GapMergeS">A no-fix trip merges with a trip that starts within this.</param>
/// <param name="MinDistanceM">Option trips_min_distance_miles (0.3 mi).</param>
/// <param name="MinDurationS">Option trips_min_duration_seconds.</param>
/// <param name="ArrivalAnnounceS">The UI-only shortcut: below the stop speed this long inside a zone means no longer driving.</param>
/// <param name="TrackFailoverS">A source feeds the track only when no better source had a fix this recently.</param>
public record TripOptions(
    double StartSpeedMps = 6.7,
    int StartConsecutiveFixes = 2,
    double StartMaxGapS = 120,
    double StartMinDisplacementM = 150,
    double FlagAssistDisplacementM = 400,
    double DepartureLookbackS = 240,
    double DepartureSnapM = 60,
    double StopSpeedMps = 1.5,
    double ResumeSpeedMps = 1.5,
    double ResumeDisplacementM = 60,
    double StopMergeS = 180,
    double NoFixEndS = 600,
    double GapMergeS = 900,
    double GapMergeMaxSpeedMps = 45,
    double MinDistanceM = 483,
    double MinDurationS = 120,
    double ArrivalAnnounceS = 45,
    double SparseMinDistanceM = 1000,
    double SparseMinSpeedMps = 5.0,
    double SparseMaxGapS = 900,
    double TrackFailoverS = 120,
    double MaxPlausibleSpeedMps = 54);
