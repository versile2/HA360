namespace Realm.Domain;

/// <summary>
/// The only data surface the UI sees, one per circuit. The surface is these ten members (0.2.0 added the two period reads): the snapshot, its change event, the clock, the zone, the two
/// weekly Driving reports, the two period reads, "me", and the roster that Settings edits.
/// </summary>
public interface IRealmSession : IAsyncDisposable
{
    /// <summary>The current immutable snapshot.</summary>
    RealmSnapshot Current { get; }

    /// <summary>
    /// Raised when the snapshot changes: at most once per second, immediately on a connection-state change.
    /// Raised off the UI thread, so subscribers must marshal with InvokeAsync.
    /// </summary>
    event Action? Changed;

    /// <summary>The session's clock; algorithms never read the wall clock.</summary>
    TimeProvider Time { get; }

    /// <summary>HA's configured zone, never the browser's.</summary>
    TimeZoneInfo Zone { get; }

    /// <summary>The Driving report for week <paramref name="weekOffset"/> (0 to 3).</summary>
    ValueTask<WeekReportVm> GetWeekReportAsync(int weekOffset, DayOfWeek weekStart, CancellationToken ct);

    /// <summary>
    /// The Driving report of any period (a week chip, a calendar month, a rolling window or a custom range) as resolved by <see cref="PeriodMath.Resolve"/>
    /// against <see cref="Time"/> and <see cref="Zone"/>.
    /// </summary>
    ValueTask<WeekReportVm> GetPeriodReportAsync(ReportWindow window, CancellationToken ct);

    /// <summary>One driver's period: the summary and every drive that started in the window, newest first. Null like <see cref="GetDriverWeekAsync"/>.</summary>
    ValueTask<DriverWeek?> GetDriverPeriodAsync(string memberId, ReportWindow window, CancellationToken ct);

    /// <summary>
    /// One driver's week. Null for the static member, an unknown id or any member that is not in the report.
    /// </summary>
    ValueTask<DriverWeek?> GetDriverWeekAsync(string memberId, int weekOffset, DayOfWeek weekStart, CancellationToken ct);

    /// <summary>
    /// Who is on the map: every roster entry in its group (People, Vehicles, Not tracked), with the two edits Settings offers. A Live session shares the add-on's
    /// stored roster; a Demo session has a roster of its own, in memory, that dies with the circuit.
    /// </summary>
    IRosterEditor Roster { get; }

    /// <summary>The member whose person link matches the HA user id; null if none. Synchronous: a lookup over in-memory members.</summary>
    string? ResolveMe(string? haUserId);
}
