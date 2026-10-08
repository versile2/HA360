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
    private const int MaxCachedWeeks = 24;
    private const int MaxAddressLookups = 3000;
    private const int MaxMemoAddresses = 20_000;
    private const double AddressMaxDistanceM = 1000;
    private static readonly TimeSpan AddressMaxAge = TimeSpan.FromMinutes(30);
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
    private readonly System.Collections.Concurrent.ConcurrentDictionary<(string MemberId, long AtMs), string?> _addressMemo = new();
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
    public ValueTask<WeekReportVm> GetWeekReportAsync(int weekOffset, DayOfWeek weekStart, CancellationToken cancellationToken)
    {
        CheckWeek(weekOffset);
        return GetPeriodReportAsync(PeriodMath.Resolve(ReportPeriod.OfWeek(weekOffset), _time.GetUtcNow(), weekStart, Zone), cancellationToken);
    }

    /// <summary>One driver's week; null for an id that is not a driver of the report.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The week offset is not 0 to 3.</exception>
    public ValueTask<DriverWeek?> GetDriverWeekAsync(string memberId, int weekOffset, DayOfWeek weekStart, CancellationToken cancellationToken)
    {
        CheckWeek(weekOffset);
        return GetDriverPeriodAsync(memberId, PeriodMath.Resolve(ReportPeriod.OfWeek(weekOffset), _time.GetUtcNow(), weekStart, Zone), cancellationToken);
    }

    /// <summary>The report of any period (a week, a calendar month, a rolling window or a custom range) from the stored trips.</summary>
    public async ValueTask<WeekReportVm> GetPeriodReportAsync(ReportWindow window, CancellationToken cancellationToken)
    {
        var (members, trips) = await ReadAsync(window, cancellationToken);
        return StatsRules.PeriodReport(window, members, trips);
    }

    /// <summary>
    /// One driver's period; null for an id that is not a driver of the report. The ends of every drive are named (<see cref="PlaceLabeler"/>): the zone, else the city of the
    /// address stored near the end, else the nearest zone.
    /// </summary>
    public async ValueTask<DriverWeek?> GetDriverPeriodAsync(string memberId, ReportWindow window, CancellationToken cancellationToken)
    {
        var (members, trips) = await ReadAsync(window, cancellationToken);
        var zones = _state.Current.Places.Select(p => new LabelZone(p.Id, p.DisplayName, p.Lat, p.Lon)).ToList();
        var addresses = await AddressesAsync(memberId, window, trips, zones, cancellationToken);
        var labels = new PlaceLabeler(zones, (trip, end) => addresses.TryGetValue((trip.StartUtc, end), out var address) ? address : null);
        return StatsRules.DriverPeriodOf(window, memberId, members, trips, labels);
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

    private static void CheckWeek(int weekOffset)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(weekOffset);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(weekOffset, WeeksKept);
    }

    // The report drivers (live members that count in the Driving report) with their recording starts, and the trips of the window and of its comparator.
    private async Task<(IReadOnlyList<StatsMember> Members, IReadOnlyList<StatsTrip> Trips)> ReadAsync(ReportWindow window, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(window);
        var version = _state.Current.StatsVersion;
        var from = window.StartUtc < window.Comparator.StartUtc ? window.StartUtc : window.Comparator.StartUtc;

        var starts = await _queries.GetRecordingStartsAsync(cancellationToken);
        var members = _discovery.Current.Members
            .Where(m => m.Kind == MemberKind.Live && m.InDrivingReport)
            .Select(m => new StatsMember(m.Id, m.DisplayName, m.PhoneCapable, starts.TryGetValue(m.Id, out var start) ? start : null))
            .ToList();
        var trips = await TripsAsync(new TripsKey(Zone.Id, from, window.EndUtc), version, cancellationToken);
        return (members, trips);
    }

    // The stored address near the start and the end of each of the member's drives that lies in no zone that still exists: the latest address fix at or before the point,
    // at most 30 minutes old and 1 km away. Memoized by (member, instant); at most MaxAddressLookups reads per call, the rest are named by the nearest zone.
    private async Task<Dictionary<(DateTimeOffset Start, bool End), string?>> AddressesAsync(
        string memberId,
        ReportWindow window,
        IReadOnlyList<StatsTrip> trips,
        IReadOnlyList<LabelZone> zones,
        CancellationToken cancellationToken)
    {
        var known = zones.Select(z => z.Id).ToHashSet(StringComparer.Ordinal);
        var result = new Dictionary<(DateTimeOffset Start, bool End), string?>();
        var lookups = 0;
        foreach (var trip in trips)
        {
            if (!string.Equals(trip.MemberId, memberId, StringComparison.Ordinal) || trip.StartUtc < window.StartUtc || trip.StartUtc >= window.EndUtc)
            {
                continue;
            }

            foreach (var end in new[] { false, true })
            {
                var placeId = end ? trip.EndPlaceId : trip.StartPlaceId;
                if (placeId is not null && known.Contains(placeId))
                {
                    continue;
                }

                if (lookups >= MaxAddressLookups)
                {
                    return result;
                }

                lookups++;
                var at = end ? trip.EndUtc : trip.StartUtc;
                result[(trip.StartUtc, end)] = await AddressNearAsync(memberId, at, end ? trip.EndLat : trip.StartLat, end ? trip.EndLon : trip.StartLon, cancellationToken);
            }
        }

        return result;
    }

    private async Task<string?> AddressNearAsync(string memberId, DateTimeOffset at, double? lat, double? lon, CancellationToken cancellationToken)
    {
        var key = (memberId, at.ToUnixTimeMilliseconds());
        if (_addressMemo.TryGetValue(key, out var hit))
        {
            return hit;
        }

        var fix = await _queries.GetLatestAddressFixAsync(memberId, at, cancellationToken);
        var near = fix is { Address: not null }
            && at - fix.Ts <= AddressMaxAge
            && (lat is null || lon is null || Geo.DistanceM(fix.Lat, fix.Lon, lat.Value, lon.Value) <= AddressMaxDistanceM);
        var address = near ? fix!.Address : null;
        if (_addressMemo.Count >= MaxMemoAddresses)
        {
            _addressMemo.Clear();
        }

        _addressMemo[key] = address;
        return address;
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

    private sealed record TripsKey(string ZoneId, DateTimeOffset From, DateTimeOffset To);

    private sealed record ZoneEntry(string Id, TimeZoneInfo Zone);
}
