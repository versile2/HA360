using System.Globalization;
using Xunit;

namespace Realm.Domain.Tests;

// The fixture clock is Wed 2026-09-30 21:25 CDT. The fixture members were last heard 0, 1, 3 and 42 minutes
// earlier (king, queen, jester, cryptid). Defaults: ui_stale_after_minutes 30, ui_offline_after_hours 24,
// ui_vehicle_stale_after_minutes 45, fusion_stale_grace_minutes 10, a 30-minute Life360 heartbeat.
public class FreshnessTests
{
    private const int StaleAfterMinutes = 30;
    private const int OfflineAfterHours = 24;
    private const int VehicleStaleAfterMinutes = 45;

    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-30T21:25:00-05:00", CultureInfo.InvariantCulture);

    private static TimeSpan DefaultStaleAfter() => FreshnessRules.StaleAfter(StaleAfterMinutes, TimeSpan.FromMinutes(30));

    // Fix times of one source: the first at a fixed start, each later one the given number of minutes after the previous.
    private static DateTimeOffset[] FixTimes(params int[] gapsMinutes)
    {
        var times = new List<DateTimeOffset> { Now.AddDays(-1) };
        foreach (var gap in gapsMinutes)
        {
            times.Add(times[^1].AddMinutes(gap));
        }

        return times.ToArray();
    }

    // StaleAfter = max(ui_stale_after_minutes, heartbeat + grace). The configured value is a floor, not a replacement.
    [Theory]
    [InlineData(30, 30, 40)]
    [InlineData(30, 5, 30)]
    [InlineData(30, 20, 30)]
    [InlineData(30, 21, 31)]
    [InlineData(30, 60, 70)]
    [InlineData(45, 30, 45)]
    [InlineData(45, 40, 50)]
    public void Stale_threshold_is_the_larger_of_the_option_and_heartbeat_plus_ten_minutes(int uiMinutes, int heartbeatMinutes, int expectedMinutes)
    {
        var staleAfter = FreshnessRules.StaleAfter(uiMinutes, TimeSpan.FromMinutes(heartbeatMinutes));

        Assert.Equal(TimeSpan.FromMinutes(expectedMinutes), staleAfter);
    }

    [Fact]
    public void Grace_is_a_parameter_with_a_default_of_ten_minutes()
    {
        Assert.Equal(10, FreshnessRules.DefaultGraceMinutes);
        Assert.Equal(TimeSpan.FromMinutes(30), FreshnessRules.StaleAfter(30, TimeSpan.FromMinutes(30), graceMinutes: 0));
        Assert.Equal(TimeSpan.FromMinutes(55), FreshnessRules.StaleAfter(30, TimeSpan.FromMinutes(30), graceMinutes: 25));
    }

    [Fact]
    public void The_default_threshold_is_40_minutes()
    {
        Assert.Equal(TimeSpan.FromMinutes(40), DefaultStaleAfter());
    }

    // The fixture: 0, 1 and 3 minutes are Fresh, the 42-minute-old member is Stale (42 is above 40).
    [Theory]
    [InlineData(0, Freshness.Fresh)]
    [InlineData(60, Freshness.Fresh)]
    [InlineData(180, Freshness.Fresh)]
    [InlineData(42 * 60, Freshness.Stale)]
    public void Fixture_members_by_age(int ageSeconds, Freshness expected)
    {
        var freshness = FreshnessRules.ForMember(MemberKind.Live, Now, Now.AddSeconds(-ageSeconds), DefaultStaleAfter(), OfflineAfterHours);

        Assert.Equal(expected, freshness);
    }

    // Stale means older than the threshold: exactly 40 minutes is still Fresh.
    [Theory]
    [InlineData(40 * 60, Freshness.Fresh)]
    [InlineData((40 * 60) + 1, Freshness.Stale)]
    [InlineData(24 * 3600, Freshness.Stale)]
    [InlineData((24 * 3600) + 1, Freshness.Offline)]
    [InlineData(30 * 3600, Freshness.Offline)]
    public void Member_thresholds_are_exclusive(int ageSeconds, Freshness expected)
    {
        var freshness = FreshnessRules.ForMember(MemberKind.Live, Now, Now.AddSeconds(-ageSeconds), DefaultStaleAfter(), OfflineAfterHours);

        Assert.Equal(expected, freshness);
    }

    [Theory]
    [InlineData(2 * 3600, Freshness.Stale)]
    [InlineData((2 * 3600) + 1, Freshness.Offline)]
    public void Offline_threshold_follows_the_option(int ageSeconds, Freshness expected)
    {
        var freshness = FreshnessRules.ForMember(MemberKind.Live, Now, Now.AddSeconds(-ageSeconds), DefaultStaleAfter(), offlineAfterHours: 2);

        Assert.Equal(expected, freshness);
    }

    [Fact]
    public void A_member_that_never_reported_has_no_fix()
    {
        Assert.Equal(Freshness.NoFix, FreshnessRules.ForMember(MemberKind.Live, Now, null, DefaultStaleAfter(), OfflineAfterHours));
    }

    [Fact]
    public void A_static_member_is_always_static()
    {
        Assert.Equal(Freshness.Static, FreshnessRules.ForMember(MemberKind.Static, Now, null, DefaultStaleAfter(), OfflineAfterHours));
        Assert.Equal(Freshness.Static, FreshnessRules.ForMember(MemberKind.Static, Now, Now.AddDays(-30), DefaultStaleAfter(), OfflineAfterHours));
    }

    // A healthy stationary Life360 phone reports every 30 minutes: it is not Stale at 35 minutes, only past 40.
    [Fact]
    public void A_member_on_the_30_minute_heartbeat_is_not_stale_between_heartbeats()
    {
        Assert.Equal(Freshness.Fresh, FreshnessRules.ForMember(MemberKind.Live, Now, Now.AddMinutes(-35), DefaultStaleAfter(), OfflineAfterHours));
        Assert.Equal(Freshness.Stale, FreshnessRules.ForMember(MemberKind.Live, Now, Now.AddMinutes(-41), DefaultStaleAfter(), OfflineAfterHours));
    }

    [Fact]
    public void Heartbeat_is_30_minutes_when_nothing_can_be_observed()
    {
        var thirty = TimeSpan.FromMinutes(30);

        Assert.Equal(thirty, FreshnessRules.Heartbeat([], StaleAfterMinutes));
        Assert.Equal(thirty, FreshnessRules.Heartbeat(new[] { Array.Empty<DateTimeOffset>() }, StaleAfterMinutes));
        Assert.Equal(thirty, FreshnessRules.Heartbeat(new[] { FixTimes() }, StaleAfterMinutes));
    }

    // Only gaps longer than 5 minutes are a stationary heartbeat; a driving cadence of seconds or a 5-minute gap is not.
    [Fact]
    public void Heartbeat_ignores_gaps_of_5_minutes_or_less()
    {
        Assert.Equal(TimeSpan.FromMinutes(30), FreshnessRules.Heartbeat(new[] { FixTimes(1, 1, 5, 1, 5) }, StaleAfterMinutes));
        Assert.Equal(TimeSpan.FromMinutes(30), FreshnessRules.Heartbeat(new[] { FixTimes(1, 30, 30, 1, 30, 1) }, StaleAfterMinutes));
        Assert.Equal(TimeSpan.FromMinutes(12), FreshnessRules.Heartbeat(new[] { FixTimes(1, 12, 5, 12, 1) }, StaleAfterMinutes));
    }

    [Fact]
    public void Heartbeat_is_the_median_not_the_mean()
    {
        // Gaps over 5 minutes: 6, 6, 60. The mean is 24, the median is 6.
        Assert.Equal(TimeSpan.FromMinutes(6), FreshnessRules.Heartbeat(new[] { FixTimes(6, 60, 6) }, StaleAfterMinutes));
        // An odd number of gaps takes the middle one: 10, 20, 60 give 20.
        Assert.Equal(TimeSpan.FromMinutes(20), FreshnessRules.Heartbeat(new[] { FixTimes(60, 10, 20) }, StaleAfterMinutes));
        // An even number of gaps takes the middle of the two central ones: 10 and 20 give 15.
        Assert.Equal(TimeSpan.FromMinutes(15), FreshnessRules.Heartbeat(new[] { FixTimes(10, 20) }, StaleAfterMinutes));
    }

    [Fact]
    public void Heartbeat_does_not_depend_on_the_order_of_the_stored_fixes()
    {
        var times = FixTimes(10, 20, 30);
        var reversed = times.OrderByDescending(t => t).ToArray();

        Assert.Equal(FreshnessRules.Heartbeat(new[] { times }, StaleAfterMinutes), FreshnessRules.Heartbeat(new[] { reversed }, StaleAfterMinutes));
        Assert.Equal(TimeSpan.FromMinutes(20), FreshnessRules.Heartbeat(new[] { reversed }, StaleAfterMinutes));
    }

    // A member with two sources takes the largest heartbeat among them.
    [Fact]
    public void Heartbeat_is_the_largest_of_the_sources()
    {
        var heartbeat = FreshnessRules.Heartbeat(new[] { FixTimes(12, 12, 12), FixTimes(30, 30), Array.Empty<DateTimeOffset>() }, StaleAfterMinutes);

        Assert.Equal(TimeSpan.FromMinutes(30), heartbeat);
    }

    // A battery-saver phone that reports every two hours must not stretch the threshold to 130 minutes:
    // the heartbeat is capped at twice the option, 60 minutes, so a member goes Stale after 70.
    [Fact]
    public void Heartbeat_is_capped_at_twice_the_stale_option()
    {
        var times = FixTimes(120, 120, 120);

        var heartbeat = FreshnessRules.Heartbeat(new[] { times }, StaleAfterMinutes);
        var staleAfter = FreshnessRules.StaleAfter(StaleAfterMinutes, heartbeat);

        Assert.Equal(TimeSpan.FromMinutes(60), heartbeat);
        Assert.Equal(TimeSpan.FromMinutes(70), staleAfter);
        Assert.Equal(Freshness.Fresh, FreshnessRules.ForMember(MemberKind.Live, Now, Now.AddMinutes(-70), staleAfter, OfflineAfterHours));
        Assert.Equal(Freshness.Stale, FreshnessRules.ForMember(MemberKind.Live, Now, Now.AddMinutes(-71), staleAfter, OfflineAfterHours));
        Assert.Equal(TimeSpan.FromMinutes(90), FreshnessRules.Heartbeat(new[] { times }, uiStaleAfterMinutes: 45));
    }

    // The fixture's vehicle was refreshed 20 minutes before the clock: Fresh, because the 45-minute floor exceeds FordPass's 20-minute poll.
    [Theory]
    [InlineData(0, Freshness.Fresh)]
    [InlineData(20 * 60, Freshness.Fresh)]
    [InlineData(45 * 60, Freshness.Fresh)]
    [InlineData((45 * 60) + 1, Freshness.Stale)]
    [InlineData(10 * 24 * 3600, Freshness.Stale)]
    public void Vehicle_threshold_is_exclusive(int ageSeconds, Freshness expected)
    {
        Assert.Equal(expected, FreshnessRules.ForVehicle(Now, Now.AddSeconds(-ageSeconds), VehicleStaleAfterMinutes));
    }

    [Fact]
    public void Vehicle_threshold_follows_the_option()
    {
        Assert.Equal(Freshness.Fresh, FreshnessRules.ForVehicle(Now, Now.AddMinutes(-5), vehicleStaleAfterMinutes: 5));
        Assert.Equal(Freshness.Stale, FreshnessRules.ForVehicle(Now, Now.AddMinutes(-6), vehicleStaleAfterMinutes: 5));
    }

    [Fact]
    public void A_vehicle_with_no_update_time_has_no_fix()
    {
        Assert.Equal(Freshness.NoFix, FreshnessRules.ForVehicle(Now, null, VehicleStaleAfterMinutes));
    }
}
