using Realm.Domain;
using Realm.Infrastructure.Ha;
using Realm.Infrastructure.Options;

namespace Realm.Infrastructure.Ingestion;

/// <summary>
/// The singleton behind every circuit's <see cref="LiveRealmSession"/> (02 section 1.10, 03 section 2.2): a facade over <see cref="RealmState"/> (the
/// current snapshot), <see cref="ChangeNotifier"/> (its change events), <see cref="DiscoveryState"/> (the zone and who "me" is) and
/// <see cref="IRealmQueries"/> (the stored trips the weekly report is built from). It holds no state of its own, so it is cheap to share and never blocks.
/// </summary>
/// <remarks>
/// The week reports are the Domain's <see cref="StatsRules"/> over the stored trips and recording starts. Persisting the trips that feed them is the
/// stats service's job (S14b): until it does, a week has no record and reads <see cref="WeekCoverage.NoRecord"/>, which is the truth.
/// </remarks>
public sealed class HaDataSource
{
    private const int WeeksKept = 4;

    private readonly RealmState _state;
    private readonly DiscoveryState _discovery;
    private readonly ChangeNotifier _notifier;
    private readonly IRealmQueries _queries;
    private readonly RealmOptions _options;
    private readonly TimeProvider _time;
    private volatile ZoneEntry? _zone;

    public HaDataSource(RealmState state, DiscoveryState discovery, ChangeNotifier notifier, IRealmQueries queries, RealmOptions options, TimeProvider time)
    {
        _state = state;
        _discovery = discovery;
        _notifier = notifier;
        _queries = queries;
        _options = options;
        _time = time;
    }

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
    public TimeZoneInfo Zone
    {
        get
        {
            var id = _state.Current.Zone;
            if (_zone is { } known && known.Id == id)
            {
                return known.Zone;
            }

            var zone = TimeZoneInfo.Utc;
            try
            {
                zone = TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                // An unknown zone reads as UTC rather than failing every page.
            }

            _zone = new ZoneEntry(id, zone);
            return zone;
        }
    }

    /// <summary>The report of week <paramref name="weekOffset"/> (0 to 3) from the stored trips.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The week offset is not 0 to 3.</exception>
    public async ValueTask<WeekReportVm> GetWeekReportAsync(int weekOffset, DayOfWeek weekStart, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(weekOffset);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(weekOffset, WeeksKept);
        var (now, zone, members, trips) = await ReadAsync(weekOffset, weekStart, cancellationToken);
        return StatsRules.WeekReport(now, weekStart, zone, weekOffset, members, trips);
    }

    /// <summary>One driver's week; null for an id that is not a driver of the report.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The week offset is not 0 to 3.</exception>
    public async ValueTask<DriverWeek?> GetDriverWeekAsync(string memberId, int weekOffset, DayOfWeek weekStart, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(weekOffset);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(weekOffset, WeeksKept);
        var (now, zone, members, trips) = await ReadAsync(weekOffset, weekStart, cancellationToken);
        var placeNames = _state.Current.Places.ToDictionary(p => p.Id, p => p.DisplayName, StringComparer.Ordinal);
        return StatsRules.DriverWeekOf(now, weekStart, zone, weekOffset, memberId, members, trips, placeNames);
    }

    /// <summary>The member whose person is the HA user <paramref name="haUserId"/>; null when there is none. A lookup over the members in memory.</summary>
    public string? ResolveMe(string? haUserId)
    {
        if (string.IsNullOrEmpty(haUserId))
        {
            return null;
        }

        return _discovery.Current.Members.FirstOrDefault(m => string.Equals(m.UserId, haUserId, StringComparison.Ordinal))?.Id;
    }

    // The report drivers (live members that count in the Driving report) with their recording starts, and the trips of the week and of its comparator.
    private async Task<(DateTimeOffset Now, TimeZoneInfo Zone, IReadOnlyList<StatsMember> Members, IReadOnlyList<StatsTrip> Trips)> ReadAsync(
        int weekOffset,
        DayOfWeek weekStart,
        CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow();
        var zone = Zone;
        var current = WeekMath.StartUtc(now, weekStart, zone, weekOffset);
        var comparator = StatsRules.ComparatorWindow(now, weekStart, zone, weekOffset);
        var end = WeekMath.EndUtc(now, weekStart, zone, weekOffset);

        var starts = await _queries.GetRecordingStartsAsync(cancellationToken);
        var members = _discovery.Current.Members
            .Where(m => m.Kind == MemberKind.Live && m.InDrivingReport)
            .Select(m => new StatsMember(m.Id, m.DisplayName, m.PhoneCapable, starts.TryGetValue(m.Id, out var start) ? start : null))
            .ToList();
        var trips = await _queries.GetTripsAsync(current < comparator.StartUtc ? current : comparator.StartUtc, end, null, cancellationToken);
        return (now, zone, members, trips);
    }

    private sealed record ZoneEntry(string Id, TimeZoneInfo Zone);
}
