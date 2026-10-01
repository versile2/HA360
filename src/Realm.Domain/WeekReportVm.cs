namespace Realm.Domain;

/// <summary>
/// The Driving report of one week. It carries no trips: per-driver trips come only from
/// <see cref="IRealmSession.GetDriverWeekAsync"/> as <see cref="DriverWeek.Trips"/>.
/// </summary>
/// <param name="Start">Local start of the week (carries HA's UTC offset).</param>
/// <param name="End">Local display end of the week, one second before the next week starts.</param>
/// <param name="Events">Keyed speeding, phone, accel, braking.</param>
/// <param name="TopSpeed">Null when no driver has a speed record that week.</param>
/// <param name="Drivers">In card order: Drives descending, Meters descending, name.</param>
public record WeekReportVm(
    DateTimeOffset Start,
    DateTimeOffset End,
    bool IsCurrent,
    WeekCoverage Coverage,
    DistanceBasis DistanceBasis,
    IReadOnlyDictionary<string, EventStat> Events,
    TopSpeedStat? TopSpeed,
    WeekTotals Totals,
    IReadOnlyList<DriverSummary> Drivers);
