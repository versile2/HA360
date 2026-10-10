using Realm.Domain;
using Realm.Infrastructure.Ha;
using Xunit;

namespace Realm.Ingestion.Tests;

/// <summary>
/// 0.3.0, D123: the Location History reads of the statistics service over a real database. A day is read from the stored fixes and trips inside its local bounds, a member that is
/// not tracked (or unknown) has no history, and a day outside the retained range reads as the nearest day inside it. Every id is fictional.
/// </summary>
public sealed class HistoryServiceTests
{
    private const double HomeLat = 31.0990;
    private const double HomeLon = -85.3410;
    private static readonly DateOnly Today = new(2026, 10, 2);

    private static RawFix Fix(DateTimeOffset at, double lat = HomeLat) =>
        new(Plans.KingTracker, FixSource.Companion, at, lat, HomeLon, AccuracyM: 15, SpeedMps: 0);

    [Fact]
    public async Task ADay_IsReadFromTheFixesInsideItsBounds_AndTheStayIsDerived()
    {
        await using var rig = await StoreRig.StartAsync();
        await rig.DiscoverAsync(King());
        var yesterday = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        var fixes = Enumerable.Range(0, 7).Select(i => Fix(yesterday.AddHours(9 + i), HomeLat + (i % 2 * 0.00002))).ToList();
        // A fix of another day must not leak into this one.
        fixes.Add(Fix(yesterday.AddDays(-3).AddHours(9)));
        await rig.StoreFixesAsync("king", fixes);

        var day = await rig.Stats.GetHistoryDayAsync("king", new DateOnly(2026, 10, 1), CancellationToken.None);

        Assert.NotNull(day);
        Assert.Equal(new DateOnly(2026, 10, 1), day.Day);
        Assert.Equal(yesterday, day.StartUtc);
        Assert.Equal(yesterday.AddDays(1), day.EndUtc);
        Assert.True(day.Recorded);
        var stay = Assert.Single(day.Stays);
        Assert.Equal(yesterday.AddHours(9), stay.StartUtc);
    }

    [Fact]
    public async Task ADayWithNothingStored_IsEmptyAndNotRecorded()
    {
        await using var rig = await StoreRig.StartAsync();
        await rig.DiscoverAsync(King());

        var day = await rig.Stats.GetHistoryDayAsync("king", Today.AddDays(-2), CancellationToken.None);

        Assert.NotNull(day);
        Assert.False(day.Recorded);
        Assert.Empty(day.Entries);
    }

    [Fact]
    public async Task AnUnknownMember_HasNoHistory()
    {
        await using var rig = await StoreRig.StartAsync();
        await rig.DiscoverAsync(King());

        Assert.Null(await rig.Stats.GetHistoryDayAsync("nobody", Today, CancellationToken.None));
        Assert.Empty(await rig.Stats.GetHistoryRangeAsync("nobody", Today.AddDays(-6), Today, CancellationToken.None));
    }

    [Fact]
    public async Task ADayBeforeTheRetainedRange_ReadsAsTheOldestDayKept()
    {
        await using var rig = await StoreRig.StartAsync();
        await rig.DiscoverAsync(King());

        var day = await rig.Stats.GetHistoryDayAsync("king", Today.AddDays(-5000), CancellationToken.None);

        Assert.NotNull(day);
        Assert.True(day.Day > Today.AddDays(-5000));
        Assert.True(day.Day < Today);
    }

    [Fact]
    public async Task TheRange_IsNewestFirst_AndHasOneEntryPerDay()
    {
        await using var rig = await StoreRig.StartAsync();
        await rig.DiscoverAsync(King());

        var days = await rig.Stats.GetHistoryRangeAsync("king", Today.AddDays(-6), Today, CancellationToken.None);

        Assert.Equal(7, days.Count);
        Assert.Equal(Today, days[0].Day);
        Assert.Equal(Today.AddDays(-6), days[^1].Day);
    }

    private static ResolvedMember King() => Plans.Member("king", life360: Plans.KingTracker);
}
