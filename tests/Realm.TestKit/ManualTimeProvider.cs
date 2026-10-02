namespace Realm.TestKit;

/// <summary>
/// A clock that moves only when a test calls <see cref="Advance"/>; there is no wall clock in it. Its timers (and so every
/// <see cref="Task.Delay(TimeSpan, TimeProvider, CancellationToken)"/> and <c>new CancellationTokenSource(timeout, provider)</c>) fire on the thread that
/// advances, in the order of their due times, with the clock set to each due time. Code under test that waits on real I/O and on this clock uses
/// <see cref="WaitForTimerAsync"/> to know that it has gone to sleep before the test moves time.
/// </summary>
public sealed class ManualTimeProvider : TimeProvider
{
    private readonly object _gate = new();
    private readonly List<ManualTimer> _timers = [];
    private readonly List<(TimeSpan DueTime, TaskCompletionSource Completion)> _waiters = [];
    private DateTimeOffset _now;

    public ManualTimeProvider(DateTimeOffset start)
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
        var timer = new ManualTimer(this, callback, state);
        timer.Change(dueTime, period);
        return timer;
    }

    /// <summary>The number of timers that are waiting to fire.</summary>
    public int ActiveTimerCount
    {
        get
        {
            lock (_gate)
            {
                return _timers.Count(timer => timer.Active);
            }
        }
    }

    /// <summary>
    /// Completes when a timer that was set to fire <paramref name="dueTime"/> after it was started is waiting (right now or later). A timer that has fired or
    /// been disposed does not count, so waiting for the same duration again waits for the next timer of that length.
    /// </summary>
    public Task WaitForTimerAsync(TimeSpan dueTime)
    {
        lock (_gate)
        {
            if (_timers.Any(timer => timer.Active && timer.DueTime == dueTime))
            {
                return Task.CompletedTask;
            }

            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _waiters.Add((dueTime, completion));
            return completion.Task;
        }
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
            ManualTimer? next = null;
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

    // Called by a timer that has just been (re)started.
    private void Started(ManualTimer timer)
    {
        List<TaskCompletionSource> ready = [];
        lock (_gate)
        {
            if (!_timers.Contains(timer))
            {
                _timers.Add(timer);
            }

            for (var i = _waiters.Count - 1; i >= 0; i--)
            {
                if (_waiters[i].DueTime == timer.DueTime)
                {
                    ready.Add(_waiters[i].Completion);
                    _waiters.RemoveAt(i);
                }
            }
        }

        foreach (var completion in ready)
        {
            completion.TrySetResult();
        }
    }

    private sealed class ManualTimer : ITimer
    {
        private readonly ManualTimeProvider _owner;
        private readonly TimerCallback _callback;
        private readonly object? _state;
        private TimeSpan _period;

        public ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state)
        {
            _owner = owner;
            _callback = callback;
            _state = state;
        }

        public bool Active { get; private set; }

        public DateTimeOffset Due { get; private set; }

        /// <summary>The wait the timer was last started with, counted from the moment it was started.</summary>
        public TimeSpan DueTime { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (_owner._gate)
            {
                if (dueTime == Timeout.InfiniteTimeSpan)
                {
                    Active = false;
                    return true;
                }

                DueTime = dueTime;
                Due = _owner._now + dueTime;
                _period = period == Timeout.InfiniteTimeSpan ? TimeSpan.Zero : period;
                Active = true;
            }

            _owner.Started(this);
            return true;
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
