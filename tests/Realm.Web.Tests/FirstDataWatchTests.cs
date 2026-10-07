using Realm.Domain;
using Realm.TestKit;
using Realm.Web.Components.Shell;
using Xunit;

namespace Realm.Web.Tests;

/// <summary>
/// <see cref="FirstDataWatch"/> (01 sections 3.8 and 8.6, [AC-49b]): the stage moves to Slow at 8 s and to Failed at 20 s on the session's own clock, only forward, only once begun, and never
/// after the dispose. The clock is a <see cref="ManualTimeProvider"/>, so nothing here waits.
/// </summary>
public sealed class FirstDataWatchTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 30, 21, 25, 30, TimeSpan.Zero);

    [Fact(DisplayName = "[AC-49b] The stage is Waiting before Begin, Slow after 8 s and Failed after 20 s")]
    public void Stage_MovesAtEightAndTwentySeconds()
    {
        var clock = new ManualTimeProvider(Start);
        using var watch = new FirstDataWatch(clock);
        var raised = 0;
        watch.Changed += () => raised++;

        clock.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(FirstDataStage.Waiting, watch.Stage);

        watch.Begin();
        watch.Begin();
        clock.Advance(TimeSpan.FromSeconds(7.9));
        Assert.Equal(FirstDataStage.Waiting, watch.Stage);

        clock.Advance(TimeSpan.FromSeconds(0.2));
        Assert.Equal(FirstDataStage.Slow, watch.Stage);

        clock.Advance(TimeSpan.FromSeconds(12));
        Assert.Equal(FirstDataStage.Failed, watch.Stage);
        Assert.Equal(2, raised);
    }

    [Fact(DisplayName = "[AC-49b] Nothing is raised after the dispose")]
    public void Dispose_StopsTheTimers()
    {
        var clock = new ManualTimeProvider(Start);
        var watch = new FirstDataWatch(clock);
        var raised = 0;
        watch.Changed += () => raised++;
        watch.Begin();

        watch.Dispose();
        clock.Advance(TimeSpan.FromSeconds(60));

        Assert.Equal(0, raised);
        Assert.Equal(FirstDataStage.Waiting, watch.Stage);
    }

    [Fact(DisplayName = "[AC-49b] The first snapshot is pending while there are no people, no vehicles and no places")]
    public void IsPending_IsTrueForTheEmptySnapshot()
    {
        Assert.True(FirstDataWatch.IsPending([], [], []));
    }
}
