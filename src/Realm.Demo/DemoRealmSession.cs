using Realm.Domain;

namespace Realm.Demo;

/// <summary>
/// The Demo implementation of <see cref="IRealmSession"/>, one per circuit (03 section 2.2): a thin adapter over a
/// <see cref="DemoDataSource"/>. The snapshot is built on first use, so the prerender pass that creates and discards a
/// session pays nothing (03 section 3.1).
/// </summary>
public sealed class DemoRealmSession : IRealmSession
{
    private readonly DemoDataSource _source;
    private readonly Lazy<RealmSnapshot> _current;

    public DemoRealmSession(DemoDataSource source)
    {
        _source = source;
        _current = new Lazy<RealmSnapshot>(source.Snapshot);
    }

    /// <inheritdoc />
    public RealmSnapshot Current => _current.Value;

    /// <inheritdoc />
    /// <remarks>Never raised yet: the demo's statistics and snapshot do not change at run time. The ha-down variant (S4b) will raise it.</remarks>
    public event Action? Changed
    {
        add { }
        remove { }
    }

    /// <inheritdoc />
    public TimeProvider Time => _source.Time;

    /// <inheritdoc />
    public TimeZoneInfo Zone => _source.Zone;

    /// <inheritdoc />
    public ValueTask<WeekReportVm> GetWeekReportAsync(int weekOffset, DayOfWeek weekStart, CancellationToken ct) =>
        throw new NotSupportedException("S4b");

    /// <inheritdoc />
    public ValueTask<DriverWeek?> GetDriverWeekAsync(string memberId, int weekOffset, DayOfWeek weekStart, CancellationToken ct) =>
        throw new NotSupportedException("S4b");

    /// <inheritdoc />
    public string? ResolveMe(string? haUserId) => _source.ResolveMe(haUserId);

    /// <inheritdoc />
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
