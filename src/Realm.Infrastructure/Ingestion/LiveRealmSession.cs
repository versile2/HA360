using Realm.Domain;

namespace Realm.Infrastructure.Ingestion;

/// <summary>
/// The Live <see cref="IRealmSession"/> of one circuit (03 section 2.2): a thin adapter over the singleton <see cref="HaDataSource"/>. It subscribes to the
/// data source only while someone is subscribed to it, so the session that prerendering creates and throws away holds nothing, and it lets go of the data
/// source when the last subscriber leaves or the circuit ends: a circuit that is gone must not stay referenced by the singleton.
/// </summary>
public sealed class LiveRealmSession : IRealmSession
{
    private readonly HaDataSource _source;
    private readonly object _gate = new();
    private Action? _changed;
    private bool _subscribed;
    private bool _disposed;

    public LiveRealmSession(HaDataSource source)
    {
        _source = source;
    }

    /// <inheritdoc />
    public RealmSnapshot Current => _source.Current;

    /// <inheritdoc />
    public event Action? Changed
    {
        add
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                _changed += value;
                if (!_subscribed)
                {
                    _subscribed = true;
                    _source.Changed += OnChanged;
                }
            }
        }

        remove
        {
            lock (_gate)
            {
                _changed -= value;
                if (_changed is null && _subscribed)
                {
                    _subscribed = false;
                    _source.Changed -= OnChanged;
                }
            }
        }
    }

    /// <inheritdoc />
    public IRosterEditor Roster => _source.Roster;

    /// <inheritdoc />
    public TimeProvider Time => _source.Time;

    /// <inheritdoc />
    public TimeZoneInfo Zone => _source.Zone;

    /// <inheritdoc />
    public ValueTask<WeekReportVm> GetWeekReportAsync(int weekOffset, DayOfWeek weekStart, CancellationToken ct) =>
        _source.GetWeekReportAsync(weekOffset, weekStart, ct);

    /// <inheritdoc />
    public ValueTask<DriverWeek?> GetDriverWeekAsync(string memberId, int weekOffset, DayOfWeek weekStart, CancellationToken ct) =>
        _source.GetDriverWeekAsync(memberId, weekOffset, weekStart, ct);

    /// <inheritdoc />
    public ValueTask<WeekReportVm> GetPeriodReportAsync(ReportWindow window, CancellationToken ct) =>
        _source.GetPeriodReportAsync(window, ct);

    /// <inheritdoc />
    public ValueTask<DriverWeek?> GetDriverPeriodAsync(string memberId, ReportWindow window, CancellationToken ct) =>
        _source.GetDriverPeriodAsync(memberId, window, ct);

    /// <inheritdoc />
    public string? ResolveMe(string? haUserId) => _source.ResolveMe(haUserId);

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            _disposed = true;
            _changed = null;
            if (_subscribed)
            {
                _subscribed = false;
                _source.Changed -= OnChanged;
            }
        }

        return ValueTask.CompletedTask;
    }

    private void OnChanged()
    {
        Action? handler;
        lock (_gate)
        {
            handler = _changed;
        }

        handler?.Invoke();
    }
}
