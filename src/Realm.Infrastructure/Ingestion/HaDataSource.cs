using Realm.Domain;
using Realm.Infrastructure.Ha;
using Realm.Infrastructure.Roster;
using Realm.Infrastructure.Stats;

namespace Realm.Infrastructure.Ingestion;

/// <summary>
/// The singleton behind every circuit's <see cref="LiveRealmSession"/> (02 section 1.10, 03 section 2.2): a facade over <see cref="RealmState"/> (the
/// current snapshot), <see cref="ChangeNotifier"/> (its change events), <see cref="DiscoveryState"/> (who "me" is), <see cref="RosterService"/> (who is on the map)
/// and <see cref="StatsService"/> (the zone and the weekly report built from the stored trips). It holds no state of its own, so it is cheap to share and never blocks.
/// </summary>
public sealed class HaDataSource
{
    private readonly RealmState _state;
    private readonly DiscoveryState _discovery;
    private readonly ChangeNotifier _notifier;
    private readonly StatsService _stats;
    private readonly RosterService _roster;
    private readonly TimeProvider _time;

    public HaDataSource(RealmState state, DiscoveryState discovery, ChangeNotifier notifier, StatsService stats, RosterService roster, TimeProvider time)
    {
        _state = state;
        _discovery = discovery;
        _notifier = notifier;
        _stats = stats;
        _roster = roster;
        _time = time;
    }

    /// <summary>The add-on's stored roster: who is on the map (shared by every circuit).</summary>
    public IRosterEditor Roster => _roster;

    /// <summary>The current immutable snapshot.</summary>
    public RealmSnapshot Current => _state.Current;

    /// <summary>The snapshot changed (at most once a second, at once for a connection change). Raised on the thread pool.</summary>
    public event Action? Changed
    {
        add => _notifier.Changed += value;
        remove => _notifier.Changed -= value;
    }

    /// <summary>The real clock of the add-on: the clock the pipeline measures freshness with.</summary>
    public TimeProvider Time => _time;

    /// <summary>HA's time zone (<c>config.time_zone</c>); UTC until it is known or if this machine does not know the zone.</summary>
    public TimeZoneInfo Zone => _stats.Zone;

    /// <summary>The report of week <paramref name="weekOffset"/> (0 to 3) from the stored trips.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The week offset is not 0 to 3.</exception>
    public ValueTask<WeekReportVm> GetWeekReportAsync(int weekOffset, DayOfWeek weekStart, CancellationToken cancellationToken) =>
        _stats.GetWeekReportAsync(weekOffset, weekStart, cancellationToken);

    /// <summary>One driver's week; null for an id that is not a driver of the report.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The week offset is not 0 to 3.</exception>
    public ValueTask<DriverWeek?> GetDriverWeekAsync(string memberId, int weekOffset, DayOfWeek weekStart, CancellationToken cancellationToken) =>
        _stats.GetDriverWeekAsync(memberId, weekOffset, weekStart, cancellationToken);

    /// <summary>The member whose person is the HA user <paramref name="haUserId"/>; null when there is none. A lookup over the members in memory.</summary>
    public string? ResolveMe(string? haUserId)
    {
        if (string.IsNullOrEmpty(haUserId))
        {
            return null;
        }

        return _discovery.Current.Members.FirstOrDefault(m => string.Equals(m.UserId, haUserId, StringComparison.Ordinal))?.Id;
    }
}
