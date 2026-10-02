using Realm.Infrastructure.Hosting;
using Xunit;

namespace Realm.Data.Tests;

// The failure policy of 03 section 2.14: catch everything but cancellation, log once per distinct error per minute, set the health,
// back off from 1 s doubling to 60 s and start the iteration again. The back-off wait is replaced by a recorder that moves the fake clock.
public class ResilientLoopTests
{
    private static readonly TimeSpan[] FullBackOff =
    [
        TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(16),
        TimeSpan.FromSeconds(32), TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60),
    ];

    private sealed class Rig
    {
        public FakeClock Time { get; } = new(TestData.Start);

        public ListLogger<ResilientLoop> Log { get; } = new();

        public ServiceHealth Health { get; } = new("test");

        public List<TimeSpan> Waits { get; } = [];

        public ResilientLoop Loop { get; }

        public Rig()
        {
            Loop = new ResilientLoop("test", Log, Time, Health)
            {
                Delay = (span, _) =>
                {
                    Waits.Add(span);
                    Time.Advance(span);
                    return Task.CompletedTask;
                },
            };
        }
    }

    [Fact]
    public async Task A_body_that_returns_is_finished_and_not_started_again()
    {
        var rig = new Rig();
        var runs = 0;

        await rig.Loop.RunAsync(_ => { runs++; return Task.CompletedTask; }, CancellationToken.None);

        Assert.Equal(1, runs);
        Assert.Empty(rig.Waits);
        Assert.False(rig.Health.IsFaulted);
        Assert.Equal(0, rig.Health.FaultCount);
    }

    [Fact]
    public async Task A_failing_body_is_started_again_after_a_back_off_of_one_second_doubling_to_sixty()
    {
        var rig = new Rig();
        var runs = 0;

        await rig.Loop.RunAsync(
            _ =>
            {
                runs++;
                return runs <= 9 ? throw new InvalidOperationException("boom") : Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.Equal(10, runs);
        Assert.Equal(FullBackOff, rig.Waits.ToArray());
        Assert.Equal(9, rig.Health.FaultCount);
        Assert.False(rig.Health.IsFaulted); // the last iteration was healthy
    }

    [Fact]
    public async Task The_health_is_faulted_with_the_exception_type_while_the_loop_waits_and_healthy_when_it_starts_again()
    {
        var health = new ServiceHealth("test");
        var seenWhileWaiting = new List<(bool Faulted, string? Reason)>();
        var loop = new ResilientLoop("test", new ListLogger<ResilientLoop>(), new FakeClock(TestData.Start), health)
        {
            Delay = (_, _) =>
            {
                seenWhileWaiting.Add((health.IsFaulted, health.Reason));
                return Task.CompletedTask;
            },
        };
        var runs = 0;
        var healthyAtSecondRun = false;

        await loop.RunAsync(
            _ =>
            {
                runs++;
                if (runs == 2)
                {
                    healthyAtSecondRun = !health.IsFaulted && health.Reason is null;
                    return Task.CompletedTask;
                }

                throw new FormatException("a message with a value");
            },
            CancellationToken.None);

        var seen = Assert.Single(seenWhileWaiting);
        Assert.True(seen.Faulted);
        Assert.Equal("FormatException", seen.Reason);
        Assert.True(healthyAtSecondRun);
        Assert.Equal("test", health.Name);
    }

    [Fact]
    public async Task Cancellation_ends_the_loop_without_an_exception()
    {
        var rig = new Rig();
        using var cancellation = new CancellationTokenSource();

        var run = rig.Loop.RunAsync(token => Task.Delay(Timeout.Infinite, token), cancellation.Token);
        await cancellation.CancelAsync();
        await run;

        Assert.Empty(rig.Waits);
        Assert.Empty(rig.Log.Messages);
        Assert.Equal(0, rig.Health.FaultCount);
    }

    [Fact]
    public async Task Cancellation_during_the_back_off_ends_the_loop_without_an_exception()
    {
        var time = new FakeClock(TestData.Start);
        using var cancellation = new CancellationTokenSource();
        var loop = new ResilientLoop("test", new ListLogger<ResilientLoop>(), time); // the default wait: a timer on the injected clock

        await loop.RunAsync(
            _ =>
            {
                cancellation.Cancel();
                throw new InvalidOperationException("boom");
            },
            cancellation.Token);

        Assert.Equal(1, loop.Health.FaultCount);
    }

    [Fact]
    public async Task The_same_error_is_logged_once_per_minute()
    {
        var rig = new Rig();
        var runs = 0;

        // Failures at 0, 1, 3, 7, 15, 31 and 63 seconds: the first is logged, the next five are the same error within the minute, the last is a minute later.
        await rig.Loop.RunAsync(
            _ =>
            {
                runs++;
                return runs <= 7 ? throw new InvalidOperationException("boom") : Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.Equal(7, rig.Health.FaultCount);
        Assert.Equal(2, rig.Log.Messages.Count);
        Assert.Contains("test failed", rig.Log.Messages[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_different_error_is_logged_at_once()
    {
        var rig = new Rig();
        var runs = 0;

        await rig.Loop.RunAsync(
            _ =>
            {
                runs++;
                return runs switch
                {
                    1 => throw new InvalidOperationException("first"),
                    2 => throw new InvalidOperationException("second"),
                    3 => throw new FormatException("first"),
                    _ => Task.CompletedTask,
                };
            },
            CancellationToken.None);

        Assert.Equal(3, rig.Log.Messages.Count);
    }

    [Fact]
    public async Task A_run_of_a_minute_before_a_failure_starts_the_back_off_again_at_one_second()
    {
        var rig = new Rig();
        var runs = 0;

        await rig.Loop.RunAsync(
            _ =>
            {
                runs++;
                if (runs == 2)
                {
                    rig.Time.Advance(TimeSpan.FromSeconds(61)); // it ran for 61 s, then failed
                }

                return runs <= 2 ? throw new InvalidOperationException("boom") : Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.Equal(new[] { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1) }, rig.Waits.ToArray());
    }
}
