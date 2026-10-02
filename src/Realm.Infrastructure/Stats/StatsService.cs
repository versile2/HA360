using Realm.Domain;
using Realm.Infrastructure.Ha;
using Realm.Infrastructure.Ingestion;
using Realm.Infrastructure.Options;

namespace Realm.Infrastructure.Stats;

/// <summary>
/// The driving statistics (03 section 2.10): the weekly report and the driver's week by the pure rules of 02 section 6 (<see cref="StatsRules"/>) over the
/// stored trips, and the other half of the trip life cycle: a closed trip gets its phone-use count from the stored signals (D59, D36), is written, and
/// then <see cref="RealmSnapshot.StatsVersion"/> is counted up so the Driving page refetches. The trips read for a week are memoized per
/// (week, week start, zone, <see cref="RealmSnapshot.StatsVersion"/>), so a version bump is what invalidates them; the members, their recording starts and
/// the rules are applied afresh on every call, because they are cheap and change without a trip closing.
/// </summary>
public sealed class StatsService
{
    private const int WeeksKept = 4;
    private const int MaxCachedWeeks = 16;
    private static readonly TimeSpan PhoneSignalLookback = TimeSpan.FromDays(7);

    private readonly RealmState _state;
    private readonly DiscoveryState _discovery;
    private readonly ChangeNotifier _notifier;
    private readonly IRealmQueries _queries;
    private readonly IRealmWriter _writer;
    private readonly TimeProvider _time;
    private readonly DetectionSettings _detection;
    private readonly object _cacheGate = new();
    private readonly Dictionary<TripsKey, IReadOnlyList<StatsTrip>> _cache = [];
    private int _cacheVersion = -1;
    private volatile ZoneEntry? _zone;
    private int _reads;

    public StatsService(
        RealmState state,
        DiscoveryState discovery,
        ChangeNotifier notifier,
        IRealmQueries queries,
        IRealmWriter writer,
        RealmOptions options,
        TimeProvider time)
    {
        _state = state;
        _discovery = discovery;
        _notifier = notifier;
        _queries = queries;
        _writer = writer;
        _time = time;
        _detection = new DetectionSettings(options);
    }

    /// <summary>How many times the stored trips were read, that is, the weeks that were not served from the memo (diagnostics and tests).</summary>
    public int TripReads => Volatile.Read(ref _reads);

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
        var (now, zone, members, trips) = await ReadAsync(weekOffset, weekStart, cancellationToken);
        return StatsRules.WeekReport(now, weekStart, zone, weekOffset, members, trips);
    }

    /// <summary>One driver's week; null for an id that is not a driver of the report.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The week offset is not 0 to 3.</exception>
    public async ValueTask<DriverWeek?> GetDriverWeekAsync(string memberId, int weekOffset, DayOfWeek weekStart, CancellationToken cancellationToken)
    {
        var (now, zone, members, trips) = await ReadAsync(weekOffset, weekStart, cancellationToken);
        var placeNames = _state.Current.Places.ToDictionary(p => p.Id, p => p.DisplayName, StringComparer.Ordinal);
        return StatsRules.DriverWeekOf(now, weekStart, zone, weekOffset, memberId, members, trips, placeNames);
    }

    /// <summary>
    /// Stores a trip the detector closed: attaches the phone-use count (<see cref="PhoneUseDetector"/> over the stored signals, with Android Auto time left
    /// out), writes it after everything queued before it has been committed, and counts <see cref="RealmSnapshot.StatsVersion"/> up when a new row was
    /// written. A trip that is already stored (a replay finds the same trip again) changes nothing and returns false.
    /// </summary>
    public async Task<bool> RecordTripAsync(string memberId, DetectedTrip trip, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(trip);
        var phoneCapable = _discovery.Current.Members.FirstOrDefault(m => string.Equals(m.Id, memberId, StringComparison.Ordinal))?.PhoneCapable == true;

        // The signals of the trip were queued before it closed; committing them first means the read below sees them.
        await _writer.FlushAsync(cancellationToken);
        IReadOnlyList<PhoneSignal> signals = phoneCapable && trip.Quality == TripQuality.Dense
            ? await _queries.GetPhoneSignalsAsync(memberId, trip.StartUtc - PhoneSignalLookback, trip.EndUtc.AddSeconds(1), cancellationToken)
            : [];
        var phone = PhoneUseDetector.Detect(trip, signals, phoneCapable, _detection.Driving);
        var counted = trip with { PhoneCount = phone.PhoneCount, PhoneEvents = phone.Events };

        var written = await _writer.WriteTripAsync(memberId, counted, DetectionSettings.AlgoVersion, _detection.DeriveHash, cancellationToken);
        if (written)
        {
            _state.BumpStatsVersion();
            _notifier.NotifyChanged();
        }

        return written;
    }

    // The report drivers (live members that count in the Driving report) with their recording starts, and the trips of the week and of its comparator.
    private async Task<(DateTimeOffset Now, TimeZoneInfo Zone, IReadOnlyList<StatsMember> Members, IReadOnlyList<StatsTrip> Trips)> ReadAsync(
        int weekOffset,
        DayOfWeek weekStart,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(weekOffset);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(weekOffset, WeeksKept);
        var version = _state.Current.StatsVersion;
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
        var trips = await TripsAsync(new TripsKey(weekOffset, weekStart, zone.Id, current < comparator.StartUtc ? current : comparator.StartUtc, end), version, cancellationToken);
        return (now, zone, members, trips);
    }

    private async Task<IReadOnlyList<StatsTrip>> TripsAsync(TripsKey key, int version, CancellationToken cancellationToken)
    {
        lock (_cacheGate)
        {
            if (_cacheVersion != version)
            {
                _cache.Clear();
                _cacheVersion = version;
            }

            if (_cache.TryGetValue(key, out var hit))
            {
                return hit;
            }
        }

        Interlocked.Increment(ref _reads);
        var trips = await _queries.GetTripsAsync(key.From, key.To, null, cancellationToken);
        lock (_cacheGate)
        {
            if (_cacheVersion == version)
            {
                if (_cache.Count >= MaxCachedWeeks)
                {
                    _cache.Clear();
                }

                _cache[key] = trips;
            }
        }

        return trips;
    }

    private sealed record TripsKey(int WeekOffset, DayOfWeek WeekStart, string ZoneId, DateTimeOffset From, DateTimeOffset To);

    private sealed record ZoneEntry(string Id, TimeZoneInfo Zone);
}
