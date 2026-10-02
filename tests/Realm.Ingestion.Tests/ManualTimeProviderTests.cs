using Realm.TestKit;
using Xunit;

namespace Realm.Ingestion.Tests;

// The manual clock of Realm.TestKit moves only when told to, fires timers in due order and tells a test when code under test has gone to sleep.
public class ManualTimeProviderTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Advance_runs_the_timers_that_fall_due_in_due_order_with_the_clock_at_each_due_time()
    {
        var clock = new ManualTimeProvider(Start);
        var seen = new List<(string Name, TimeSpan At)>();
        using var late = clock.CreateTimer(_ => seen.Add(("late", clock.GetUtcNow() - Start)), null, TimeSpan.FromSeconds(30), Timeout.InfiniteTimeSpan);
        using var early = clock.CreateTimer(_ => seen.Add(("early", clock.GetUtcNow() - Start)), null, TimeSpan.FromSeconds(10), Timeout.InfiniteTimeSpan);
        using var beyond = clock.CreateTimer(_ => seen.Add(("beyond", clock.GetUtcNow() - Start)), null, TimeSpan.FromSeconds(60), Timeout.InfiniteTimeSpan);

        clock.Advance(TimeSpan.FromSeconds(45));

        Assert.Equal([("early", TimeSpan.FromSeconds(10)), ("late", TimeSpan.FromSeconds(30))], seen);
        Assert.Equal(Start.AddSeconds(45), clock.GetUtcNow());
        Assert.Equal(1, clock.ActiveTimerCount);
    }

    [Fact]
    public void A_periodic_timer_fires_as_often_as_its_period_fits()
    {
        var clock = new ManualTimeProvider(Start);
        var count = 0;
        using var timer = clock.CreateTimer(_ => count++, null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));

        clock.Advance(TimeSpan.FromSeconds(7));

        Assert.Equal(3, count);
    }

    [Fact]
    public async Task A_delay_on_the_clock_completes_only_when_time_reaches_it()
    {
        var clock = new ManualTimeProvider(Start);
        var delay = Task.Delay(TimeSpan.FromSeconds(5), clock, CancellationToken.None);

        clock.Advance(TimeSpan.FromSeconds(4));
        Assert.False(delay.IsCompleted);

        clock.Advance(TimeSpan.FromSeconds(1));
        await delay.WaitAsync(TimeSpan.FromSeconds(30)); // a hang guard
    }

    [Fact]
    public async Task WaitForTimerAsync_sees_a_timer_set_before_and_after_the_call_and_ignores_fired_and_disposed_ones()
    {
        var clock = new ManualTimeProvider(Start);
        using var before = clock.CreateTimer(_ => { }, null, TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);
        await clock.WaitForTimerAsync(TimeSpan.FromSeconds(1)).WaitAsync(TimeSpan.FromSeconds(30));

        clock.Advance(TimeSpan.FromSeconds(1)); // the timer fired: it no longer counts
        var next = clock.WaitForTimerAsync(TimeSpan.FromSeconds(1));
        Assert.False(next.IsCompleted);

        var disposed = clock.CreateTimer(_ => { }, null, TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan);
        await next.WaitAsync(TimeSpan.FromSeconds(30)); // a timer of that length was set after the call
        disposed.Dispose();
        var afterDispose = clock.WaitForTimerAsync(TimeSpan.FromSeconds(1));
        Assert.False(afterDispose.IsCompleted);
        Assert.Equal(0, clock.ActiveTimerCount);
    }
}
