namespace Realm.Domain;

/// <summary>
/// One driver's week: the data of a driver card and of the summary tiles of the driver week page.
/// A driver who is not covered for the week (Covered false) has null Drives, Meters and counts.
/// </summary>
/// <param name="Events">Keyed speeding, phone, accel, braking; each count is nullable.</param>
/// <param name="CoarseTrips">Trips from sparse sources, counted in Drives and Meters but skipped in speeding and top speed.</param>
/// <param name="PhoneCapable">Tells the UI whether a null phone count means structurally absent or not recorded this week.</param>
/// <param name="EventsTotal">Sum of the non-null counts of event types that have a source; null if no type has a count.</param>
/// <param name="EventsPartial">True exactly when CoarseTrips is above zero.</param>
public record DriverSummary(
    string MemberId,
    int? Drives,
    double? Meters,
    DistanceBasis DistanceBasis,
    int CoarseTrips,
    bool PhoneCapable,
    IReadOnlyDictionary<string, int?> Events,
    int? EventsTotal,
    bool EventsPartial,
    bool Covered,
    DateTimeOffset? CoverageStartUtc);
