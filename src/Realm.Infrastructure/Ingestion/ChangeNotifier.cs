using Microsoft.Extensions.Logging;

namespace Realm.Infrastructure.Ingestion;

/// <summary>
/// Tells the circuits that the snapshot changed (03 section 2.9). <see cref="Changed"/> is raised at most once per <see cref="MinInterval"/> (1 000 ms): a
/// change that arrives inside the interval is folded into one event at its end (the trailing edge), and one that arrives after a quiet second goes out at
/// once. <see cref="NotifyNow"/> (a connection-state change) raises it immediately, so a banner is never held back. Every subscriber is called on the thread
/// pool inside try/catch, so one broken circuit cannot stall the rest or the pipeline. A 30 s timer raises <see cref="Tick"/>, the data tick that
/// re-evaluates freshness without any event (R-041).
/// </summary>
public sealed class ChangeNotifier : IDisposable
{
    /// <summary><c>Snapshot.MinIntervalMs</c> (02 section 1.10).</summary>
    public static readonly TimeSpan MinInterval = TimeSpan.FromMilliseconds(1000);

    /// <summary>The period of the data tick (03 section 2.9).</summary>
    public static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(30);

    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly object _gate = new();
    private DateTimeOffset _lastRaised = DateTimeOffset.MinValue;
    private ITimer? _trailing;
    private ITimer? _tick;
    private bool _pending;
    private bool _disposed;

    public ChangeNotifier(TimeProvider time, ILogger<ChangeNotifier> logger)
    {
        _time = time;
        _logger = logger;
    }

    /// <summary>The snapshot changed. Subscribers run on the thread pool; they must marshal to their own context.</summary>
    public event Action? Changed;

    /// <summary>The 30 s data tick, with the clock's instant. Raised on the timer's thread; the handler is called inside try/catch.</summary>
    public event Action<DateTimeOffset>? Tick;

    /// <summary>Starts the data tick (once). Nothing else needs it: change events work without it.</summary>
    public void Start()
    {
        lock (_gate)
        {
            if (_tick is null && !_disposed)
            {
                _tick = _time.CreateTimer(_ => RaiseTick(), null, TickInterval, TickInterval);
            }
        }
    }

    /// <summary>The snapshot changed: raises <see cref="Changed"/> now if the last event is at least a second old, otherwise at the end of that second.</summary>
    public void NotifyChanged()
    {
        bool raise;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            var now = _time.GetUtcNow();
            var due = _lastRaised + MinInterval;
            if (now >= due)
            {
                raise = true;
                _lastRaised = now;
                _pending = false;
            }
            else
            {
                raise = false;
                if (!_pending)
                {
                    _pending = true;
                    _trailing?.Dispose();
                    _trailing = _time.CreateTimer(_ => RaiseTrailing(), null, due - now, Timeout.InfiniteTimeSpan);
                }
            }
        }

        if (raise)
        {
            Dispatch();
        }
    }

    /// <summary>A connection state changed: raises <see cref="Changed"/> at once, whatever the interval, and folds a pending trailing event into it.</summary>
    public void NotifyNow()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _lastRaised = _time.GetUtcNow();
            _pending = false;
            _trailing?.Dispose();
            _trailing = null;
        }

        Dispatch();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _trailing?.Dispose();
            _tick?.Dispose();
            _trailing = null;
            _tick = null;
        }
    }

    private void RaiseTrailing()
    {
        lock (_gate)
        {
            if (_disposed || !_pending)
            {
                return;
            }

            _pending = false;
            _lastRaised = _time.GetUtcNow();
        }

        Dispatch();
    }

    private void RaiseTick()
    {
        var handler = Tick;
        if (handler is null)
        {
            return;
        }

        var now = _time.GetUtcNow();
        foreach (var subscriber in handler.GetInvocationList().Cast<Action<DateTimeOffset>>())
        {
            try
            {
                subscriber(now);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "A data tick subscriber failed");
            }
        }
    }

    // Each subscriber on the pool, so a slow or failing one neither delays the others nor the caller.
    private void Dispatch()
    {
        var handler = Changed;
        if (handler is null)
        {
            return;
        }

        foreach (var subscriber in handler.GetInvocationList().Cast<Action>())
        {
            ThreadPool.QueueUserWorkItem(
                static state =>
                {
                    try
                    {
                        state.Subscriber();
                    }
                    catch (Exception ex)
                    {
                        state.Logger.LogWarning(ex, "A snapshot subscriber failed");
                    }
                },
                (Subscriber: subscriber, Logger: _logger),
                preferLocal: false);
        }
    }
}
