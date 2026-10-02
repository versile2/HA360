using Realm.TestKit;
using Realm.Web.Shell;
using Xunit;

namespace Realm.Web.Tests;

/// <summary>
/// <see cref="MinuteTicker"/> (03 section 2.9): one tick on every minute boundary of the session's own clock, computed again for each wait, stopped by the
/// dispose. The clock is a <see cref="ManualTimeProvider"/>, so the test owns time and nothing here waits for a wall-clock minute.
/// </summary>
public sealed class MinuteTickerTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 30, 21, 25, 30, TimeSpan.Zero);

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    [Theory]
    [InlineData(0, 60.0)]        // exactly on a boundary: a full minute, never a zero wait
    [InlineData(1, 59.0)]
    [InlineData(30, 30.0)]
    [InlineData(59, 1.0)]
    public void UntilNextMinute_IsTheTimeToTheNextWholeMinute(int secondsIntoTheMinute, double expectedSeconds)
    {
        var now = new DateTimeOffset(2026, 9, 30, 21, 25, secondsIntoTheMinute, TimeSpan.Zero);

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), MinuteTicker.UntilNextMinute(now));
    }

    [Fact]
    public void UntilNextMinute_KeepsTheFractionOfASecond() =>
        Assert.Equal(TimeSpan.FromMilliseconds(500), MinuteTicker.UntilNextMinute(new DateTimeOffset(2026, 9, 30, 21, 25, 59, 500, TimeSpan.Zero)));

    [Fact]
    public void UntilNextMinute_DoesNotDependOnTheOffset() =>
        Assert.Equal(
            MinuteTicker.UntilNextMinute(new DateTimeOffset(2026, 9, 30, 21, 25, 45, TimeSpan.Zero)),
            MinuteTicker.UntilNextMinute(new DateTimeOffset(2026, 9, 30, 16, 25, 45, TimeSpan.FromHours(-5))));

    [Fact]
    public async Task Start_TicksOnEveryMinuteBoundary_NotBefore()
    {
        var clock = new ManualTimeProvider(Start);
        using var ticker = new MinuteTicker(clock);
        var ticks = new TickCounter(ticker);

        ticker.Start();
        await clock.WaitForTimerAsync(TimeSpan.FromSeconds(30));

        clock.Advance(TimeSpan.FromSeconds(29));
        Assert.Equal(0, ticks.Count);

        clock.Advance(TimeSpan.FromSeconds(1));
        await ticks.WaitForAsync(1);

        // The next wait is computed from the clock, which now stands on the boundary: a whole minute.
        await clock.WaitForTimerAsync(TimeSpan.FromMinutes(1));
        clock.Advance(TimeSpan.FromMinutes(1));
        await ticks.WaitForAsync(2);
    }

    [Fact]
    public async Task Start_CalledTwice_MakesOneTimer()
    {
        var clock = new ManualTimeProvider(Start);
        using var ticker = new MinuteTicker(clock);

        ticker.Start();
        ticker.Start();
        await clock.WaitForTimerAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(1, clock.ActiveTimerCount);
    }

    [Fact]
    public async Task ASubscriberThatThrows_DoesNotEndTheTicks()
    {
        var clock = new ManualTimeProvider(Start);
        using var ticker = new MinuteTicker(clock);
        var calls = 0;
        var second = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ticker.Tick += () =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                throw new InvalidOperationException("a broken subscriber");
            }

            second.TrySetResult();
        };
        ticker.Start();
        await clock.WaitForTimerAsync(TimeSpan.FromSeconds(30));

        clock.Advance(TimeSpan.FromSeconds(30));
        await clock.WaitForTimerAsync(TimeSpan.FromMinutes(1));
        clock.Advance(TimeSpan.FromMinutes(1));

        await second.Task.WaitAsync(Patience);
        Assert.Equal(2, Volatile.Read(ref calls));
    }

    [Fact]
    public async Task Dispose_StopsTheTicks_AndReleasesTheTimer()
    {
        var clock = new ManualTimeProvider(Start);
        var ticker = new MinuteTicker(clock);
        var ticks = new TickCounter(ticker);
        ticker.Start();
        await clock.WaitForTimerAsync(TimeSpan.FromSeconds(30));

        ticker.Dispose();
        ticker.Dispose();   // a second dispose does nothing

        Assert.True(SpinWait.SpinUntil(() => clock.ActiveTimerCount == 0, Patience), "the timer of the wait was not released by Dispose");
        clock.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(0, ticks.Count);
    }

    [Fact]
    public async Task Start_AfterDispose_DoesNothing()
    {
        var clock = new ManualTimeProvider(Start);
        var ticker = new MinuteTicker(clock);
        ticker.Dispose();

        ticker.Start();
        await Task.Yield();

        Assert.Equal(0, clock.ActiveTimerCount);
    }

    [Fact]
    public void ANullClock_IsRefused() =>
        Assert.Throws<ArgumentNullException>(() =>
        {
            _ = new MinuteTicker(null!);
        });

    // Counts the ticks and lets a test wait for the n-th one without sleeping: the tick is raised on whichever thread runs the timer callback.
    private sealed class TickCounter
    {
        private readonly object _gate = new();
        private readonly List<(int Target, TaskCompletionSource Completion)> _waiters = [];
        private int _count;

        public TickCounter(MinuteTicker ticker) => ticker.Tick += OnTick;

        public int Count
        {
            get
            {
                lock (_gate)
                {
                    return _count;
                }
            }
        }

        public Task WaitForAsync(int count)
        {
            lock (_gate)
            {
                if (_count >= count)
                {
                    return Task.CompletedTask;
                }

                var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _waiters.Add((count, completion));
                return completion.Task.WaitAsync(Patience);
            }
        }

        private void OnTick()
        {
            lock (_gate)
            {
                _count++;
                foreach (var waiter in _waiters.Where(waiter => _count >= waiter.Target).ToList())
                {
                    waiter.Completion.TrySetResult();
                    _waiters.Remove(waiter);
                }
            }
        }
    }
}
