namespace Realm.Data.Tests;

/// <summary>
/// A clock that moves only when a test calls <see cref="Advance"/>. Its timers (the writer's 2 s flush timer, the wait of <see cref="Task.Delay(TimeSpan, TimeProvider, CancellationToken)"/>)
/// fire on the thread that advances, in the order of their due times, with the clock set to each due time. There is no wall clock in it.
/// </summary>
internal sealed class FakeClock : TimeProvider
{
    private readonly object _gate = new();
    private readonly List<FakeTimer> _timers = [];
    private DateTimeOffset _now;

    public FakeClock(DateTimeOffset start)
    {
        _now = start;
    }

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
        {
            return _now;
        }
    }

    public override long GetTimestamp()
    {
        lock (_gate)
        {
            return _now.UtcTicks;
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new FakeTimer(this, callback, state);
        timer.Change(dueTime, period);
        lock (_gate)
        {
            _timers.Add(timer);
        }

        return timer;
    }

    /// <summary>Moves the clock forward and runs every timer that falls due on the way, a periodic one as often as its period fits.</summary>
    public void Advance(TimeSpan delta)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(delta, TimeSpan.Zero);
        DateTimeOffset target;
        lock (_gate)
        {
            target = _now + delta;
        }

        while (true)
        {
            FakeTimer? next = null;
            lock (_gate)
            {
                foreach (var timer in _timers)
                {
                    if (timer.Active && timer.Due <= target && (next is null || timer.Due < next.Due))
                    {
                        next = timer;
                    }
                }

                if (next is null)
                {
                    _now = target;
                    return;
                }

                if (next.Due > _now)
                {
                    _now = next.Due;
                }

                next.Rearm();
            }

            next.Fire();
        }
    }

    private sealed class FakeTimer : ITimer
    {
        private readonly FakeClock _owner;
        private readonly TimerCallback _callback;
        private readonly object? _state;
        private TimeSpan _period;

        public FakeTimer(FakeClock owner, TimerCallback callback, object? state)
        {
            _owner = owner;
            _callback = callback;
            _state = state;
        }

        public bool Active { get; private set; }

        public DateTimeOffset Due { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (_owner._gate)
            {
                if (dueTime == Timeout.InfiniteTimeSpan)
                {
                    Active = false;
                    return true;
                }

                Due = _owner._now + dueTime;
                _period = period == Timeout.InfiniteTimeSpan ? TimeSpan.Zero : period;
                Active = true;
                return true;
            }
        }

        // Called with the clock's lock held, just before the callback runs: a periodic timer is due again one period later, a one-shot timer is done.
        public void Rearm()
        {
            if (_period > TimeSpan.Zero)
            {
                Due += _period;
            }
            else
            {
                Active = false;
            }
        }

        public void Fire()
        {
            _callback(_state);
        }

        public void Dispose()
        {
            lock (_owner._gate)
            {
                Active = false;
                _owner._timers.Remove(this);
            }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
