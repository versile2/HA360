using Realm.Domain;

namespace Realm.Demo;

/// <summary>
/// The Demo implementation of <see cref="IRealmSession"/>, one per circuit (03 section 2.2): a thin adapter over a
/// <see cref="DemoDataSource"/>. The snapshot is built on first use, so the prerender pass that creates and discards a
/// session pays nothing (03 section 3.1). Under the ha-down variant the snapshot is re-evaluated every 30 seconds so
/// <see cref="Freshness"/> follows the running clock (02 section 9.0).
/// </summary>
public sealed class DemoRealmSession : IRealmSession
{
    /// <summary>The ha-down re-evaluation interval (02 section 9.0).</summary>
    public static readonly TimeSpan DefaultTickInterval = TimeSpan.FromSeconds(30);

    private readonly DemoDataSource _source;
    private readonly TimeSpan _tick;
    private readonly object _gate = new();
    private volatile Lazy<RealmSnapshot> _current;
    private Action? _changed;
    private ITimer? _timer;
    private readonly IPlaceEditor _places;
    private bool _restored;
    private bool _disposed;

    /// <param name="source">The fixture this session reads.</param>
    /// <param name="tickInterval">How often the ha-down variant re-evaluates the snapshot; null for 30 seconds.</param>
    public DemoRealmSession(DemoDataSource source, TimeSpan? tickInterval = null)
    {
        _source = source;
        _tick = tickInterval ?? DefaultTickInterval;
        _current = NewSnapshot();
        source.Roster.Changed += OnRosterChanged;
        source.PlacesChanged += OnRosterChanged;
        _places = new DemoPlaceEditor(source);
    }

    /// <inheritdoc />
    public IRosterEditor Roster => _source.Roster;

    /// <inheritdoc />
    /// <remarks>Simulated (D119): the place is added in memory to this session only; nothing is written anywhere.</remarks>
    public IPlaceEditor PlaceEditor => _places;

    /// <inheritdoc />
    public RealmSnapshot Current => _current.Value;

    /// <inheritdoc />
    /// <remarks>
    /// Raised on a roster change, and under the ha-down variant: on every tick, once the first subscriber arrives (a session nobody listens to
    /// starts no timer), and by <see cref="RestoreHomeAssistant"/>. The demo's statistics never change at run time.
    /// </remarks>
    public event Action? Changed
    {
        add
        {
            lock (_gate)
            {
                _changed += value;
                if (_source.Variants.HaDown && _timer is null && !_disposed)
                {
                    _timer = _source.Time.CreateTimer(_ => Tick(), null, _tick, _tick);
                }
            }
        }

        remove
        {
            lock (_gate)
            {
                _changed -= value;
            }
        }
    }

    /// <inheritdoc />
    public TimeProvider Time => _source.Time;

    /// <inheritdoc />
    public TimeZoneInfo Zone => _source.Zone;

    /// <inheritdoc />
    /// <exception cref="ArgumentOutOfRangeException">The week offset is not 0 to 3.</exception>
    public ValueTask<WeekReportVm> GetWeekReportAsync(int weekOffset, DayOfWeek weekStart, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return ValueTask.FromResult(_source.WeekReport(weekOffset, weekStart));
    }

    /// <inheritdoc />
    /// <remarks>The week start does not change the demo's drive lists: they are generated once, for the Monday week (02 section 9.0).</remarks>
    /// <exception cref="ArgumentOutOfRangeException">The week offset is not 0 to 3.</exception>
    public ValueTask<DriverWeek?> GetDriverWeekAsync(string memberId, int weekOffset, DayOfWeek weekStart, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return ValueTask.FromResult(_source.GetDriverWeek(memberId, weekOffset));
    }

    /// <inheritdoc />
    /// <remarks>A week chip is the frozen week of the fixture; any other period is computed over the fixture's drives.</remarks>
    public ValueTask<WeekReportVm> GetPeriodReportAsync(ReportWindow window, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return ValueTask.FromResult(window.Period.Kind == PeriodKind.Week
            ? _source.WeekReport(window.Period.WeekOffset, window.Start.DayOfWeek) with { Period = window.Period }
            : _source.PeriodReport(window));
    }

    /// <inheritdoc />
    public ValueTask<DriverWeek?> GetDriverPeriodAsync(string memberId, ReportWindow window, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return ValueTask.FromResult(window.Period.Kind == PeriodKind.Week
            ? _source.GetDriverWeek(memberId, window.Period.WeekOffset)
            : _source.GetDriverPeriod(memberId, window));
    }

    /// <inheritdoc />
    /// <remarks>Only the live people and the trackers that keep their history are on the map with one: someone moved to Not tracked has none (their history is not shown), and a day outside the 400 retained days is none.</remarks>
    public ValueTask<HistoryDayVm?> GetHistoryDayAsync(string memberId, DateOnly day, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var days = HistoryDays(memberId, day, day, includeTrail: true);
        return ValueTask.FromResult(days.Count == 0 ? null : days[0]);
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<HistoryDayVm>> GetHistoryRangeAsync(string memberId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return ValueTask.FromResult(HistoryDays(memberId, from, to, includeTrail: false));
    }

    private IReadOnlyList<HistoryDayVm> HistoryDays(string memberId, DateOnly from, DateOnly to, bool includeTrail)
    {
        var member = Current.Members.FirstOrDefault(m => string.Equals(m.Id, memberId, StringComparison.Ordinal));
        var tracker = member is null ? Current.Vehicles.FirstOrDefault(v => string.Equals(v.Id, memberId, StringComparison.Ordinal)) : null;
        if (tracker is null && (member is null || member.Kind != MemberKind.Live))
        {
            return [];
        }

        // A tracker (0.3.1, D125) has history when its owner keeps it on, or when positions were stored before it was switched off (the wagon's).
        if (tracker is not null && !tracker.KeepHistory && !_source.HasTrackerHistory(memberId))
        {
            return [];
        }

        var (oldest, newest) = HistoryDayMath.Range(Time.GetUtcNow(), Zone, DemoDataSource.DemoRetentionFixDays);
        return _source.HistoryDays(memberId, HistoryDayMath.Clamp(from, oldest, newest), HistoryDayMath.Clamp(to, oldest, newest), includeTrail, tracker is not null);
    }

    /// <inheritdoc />
    public string? ResolveMe(string? haUserId) => _source.ResolveMe(haUserId);

    /// <summary>
    /// The test hook of the ha-down variant (02 section 9.5): Home Assistant is connected again, so the banner clears.
    /// The clock keeps running. Does nothing without ha-down or after the first call.
    /// </summary>
    public void RestoreHomeAssistant()
    {
        Action? handler;
        lock (_gate)
        {
            if (!_source.Variants.HaDown || _restored || _disposed)
            {
                return;
            }

            _restored = true;
            _current = NewSnapshot();
            handler = _changed;
        }

        handler?.Invoke();
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        ITimer? timer;
        lock (_gate)
        {
            _disposed = true;
            _source.Roster.Changed -= OnRosterChanged;
            _source.PlacesChanged -= OnRosterChanged;
            _changed = null;
            timer = _timer;
            _timer = null;
        }

        timer?.Dispose();
        return ValueTask.CompletedTask;
    }

    // A snapshot that is built, at the clock's instant then, when it is first read.
    private Lazy<RealmSnapshot> NewSnapshot()
    {
        var restored = _restored;
        return new Lazy<RealmSnapshot>(() => _source.Snapshot(restored));
    }

    // A move or a rename in Settings: the snapshot is built again and listeners hear of it.
    private void OnRosterChanged()
    {
        Action? handler;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _current = NewSnapshot();
            handler = _changed;
        }

        handler?.Invoke();
    }

    private void Tick()
    {
        Action? handler;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _current = NewSnapshot();
            handler = _changed;
        }

        handler?.Invoke();
    }
}
