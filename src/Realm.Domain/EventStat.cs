namespace Realm.Domain;

/// <summary>
/// One event type (speeding, phone, accel or braking) across drivers. Null means not recorded or unknown; 0 is a real zero.
/// </summary>
/// <param name="Total">Sum of the non-null driver counts; null if none.</param>
/// <param name="ComparatorTotal">Like-for-like total of the comparison window over the same drivers; null if unavailable.</param>
/// <param name="TrendDelta">Change over the drivers that have a value in both periods; null if there are none.</param>
/// <param name="Partial">Some report drivers have no count for a type that is All or Some available (the chip asterisk).</param>
/// <param name="Note">Qualifier shown in the chip tooltip, for example for sampled speeding.</param>
public record EventStat(
    int? Total,
    int? ComparatorTotal,
    int? TrendDelta,
    StatSource Source,
    EventAvailability Availability,
    bool Partial,
    string? Note,
    IReadOnlyList<EventDriverCount> Drivers);
