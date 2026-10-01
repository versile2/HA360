namespace Realm.Domain;

/// <summary>
/// The only data surface the UI sees, one per circuit. The v1 surface is exactly these seven members.
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
    /// One driver's week. Null for the static member, an unknown id or any member that is not in the report.
    /// </summary>
    ValueTask<DriverWeek?> GetDriverWeekAsync(string memberId, int weekOffset, DayOfWeek weekStart, CancellationToken ct);

    /// <summary>The member whose person link matches the HA user id; null if none. Synchronous: a lookup over in-memory members.</summary>
    string? ResolveMe(string? haUserId);
}
