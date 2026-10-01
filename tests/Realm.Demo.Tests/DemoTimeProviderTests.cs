using System.Globalization;
using Realm.Domain;
using Xunit;

namespace Realm.Demo.Tests;

// The Demo clock (02 section 9.1, R3-006): frozen at the anchor or at ?now, advancing in real time only under ha-down, and
// its timers are the system ones so a PeriodicTimer ticker still fires.
public class DemoTimeProviderTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-30T21:25:00-05:00", CultureInfo.InvariantCulture);

    [Fact]
    public async Task A_frozen_clock_never_moves_and_reports_a_utc_instant()
    {
        var clock = new DemoTimeProvider(Now);

        var first = clock.GetUtcNow();
        await Task.Delay(30);
        var second = clock.GetUtcNow();

        Assert.Equal(Now, first);
        Assert.Equal(first, second);
        Assert.Equal(TimeSpan.Zero, first.Offset);
    }

    [Fact]
    public async Task An_advancing_clock_starts_at_the_instant_and_runs_in_real_time()
    {
        var clock = new DemoTimeProvider(Now, advancing: true);

        var advanced = false;
        for (var attempt = 0; attempt < 500 && !advanced; attempt++)
        {
            await Task.Delay(10);
            advanced = clock.GetUtcNow() > Now;
        }

        Assert.True(advanced);
        Assert.InRange(clock.GetUtcNow(), Now, Now.AddMinutes(5));
    }

    // MinuteTicker's PeriodicTimer is built from CreateTimer; under a frozen clock it must still fire.
    [Fact]
    public async Task A_timer_fires_under_a_frozen_clock()
    {
        var clock = new DemoTimeProvider(Now);
        var fired = new TaskCompletionSource();

        using var timer = clock.CreateTimer(_ => fired.TrySetResult(), null, TimeSpan.FromMilliseconds(10), Timeout.InfiniteTimeSpan);
        await fired.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(fired.Task.IsCompletedSuccessfully);
        Assert.Equal(Now, clock.GetUtcNow());
    }

    [Fact]
    public void The_factory_freezes_the_session_clock_at_the_anchor_by_default()
    {
        var factory = new DemoRealmSessionFactory();

        Assert.Equal(Now, DemoDataSource.Anchor);
        Assert.Equal(Now, factory.Create(null).Time.GetUtcNow());
        Assert.Equal(Now, factory.Create(new DemoUrlParams(null, [])).Time.GetUtcNow());
    }

    [Fact]
    public void The_factory_freezes_the_session_clock_at_the_now_parameter()
    {
        var chosen = DateTimeOffset.Parse("2026-10-02T08:00:00-05:00", CultureInfo.InvariantCulture);

        var session = new DemoRealmSessionFactory().Create(new DemoUrlParams(chosen, ["all-sources"]));

        Assert.Equal(chosen, session.Time.GetUtcNow());
        Assert.Equal(chosen, session.Time.GetUtcNow());
    }

    [Fact]
    public async Task Under_the_ha_down_variant_the_session_clock_runs_in_real_time_from_the_start_instant()
    {
        var session = new DemoRealmSessionFactory().Create(new DemoUrlParams(null, ["all-sources", "ha-down"]));

        var advanced = false;
        for (var attempt = 0; attempt < 500 && !advanced; attempt++)
        {
            await Task.Delay(10);
            advanced = session.Time.GetUtcNow() > Now;
        }

        Assert.True(advanced);
        Assert.InRange(session.Time.GetUtcNow(), Now, Now.AddMinutes(5));
    }
}
