using System.Globalization;
using Xunit;

namespace Realm.Domain.Tests;

// Expected values are the rule of 02 (section 1.9 and 5.10) and Python arithmetic (25 mph x 0.44704 = 11.176 m/s), never the code under test.
// A vehicle is a GPS tracker (D113): it moves when its speed is known, fresh and above 1.0 m/s. There is no ignition.
public class VehicleRulesTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-30T21:25:00-05:00", CultureInfo.InvariantCulture);

    // 25 mph with a 3 minute old fix; the same with a 12 minute old one; a fresh reading of zero is a known speed, and not moving.
    [Theory]
    [InlineData(0.0, 3, false, true)]
    [InlineData(11.176, 3, true, true)]
    [InlineData(11.176, 12, false, false)]
    public void T23_a_vehicle_is_moving_only_with_a_fresh_speed_above_one_metre_per_second(double speedMps, int sampleAgeMinutes, bool expectedMoving, bool expectedSpeedKnown)
    {
        var sample = Now.AddMinutes(-sampleAgeMinutes);

        var moving = VehicleRules.IsMoving(speedMps, sample, Now);
        var speed = VehicleRules.FreshSpeedMps(speedMps, sample, Now);

        Assert.Equal(expectedMoving, moving);
        Assert.Equal(expectedSpeedKnown, speed is not null);
        if (expectedMoving)
        {
            Assert.NotNull(speed);
            Assert.Equal(11.176, speed.Value, 3);   // the 11.18 m/s of the vector
        }
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData(0.0, false)]
    [InlineData(0.99, false)]
    [InlineData(1.0, false)]
    [InlineData(1.01, true)]
    [InlineData(35.7632, true)]
    public void The_speed_must_be_known_and_above_one_metre_per_second(double? speedMps, bool expected)
    {
        Assert.Equal(expected, VehicleRules.IsMoving(speedMps, Now.AddMinutes(-1), Now));
    }

    // "No older than 600 s": the limit itself is fresh, a second later is not; no sample time means no age and so no speed.
    [Theory]
    [InlineData(0, true)]
    [InlineData(599, true)]
    [InlineData(600, true)]
    [InlineData(601, false)]
    [InlineData(3600, false)]
    public void The_sample_must_be_no_older_than_600_seconds(int ageSeconds, bool expected)
    {
        var sample = Now.AddSeconds(-ageSeconds);

        Assert.Equal(expected, VehicleRules.IsMoving(11.176, sample, Now));
        Assert.Equal(expected, VehicleRules.FreshSpeedMps(11.176, sample, Now) is not null);
    }

    [Fact]
    public void A_vehicle_without_a_sample_time_has_no_fresh_speed_and_is_not_moving()
    {
        Assert.False(VehicleRules.IsMoving(11.176, null, Now));
        Assert.Null(VehicleRules.FreshSpeedMps(11.176, null, Now));
    }

    [Fact]
    public void The_maximum_age_is_a_parameter_and_defaults_to_the_600_seconds_of_the_spec()
    {
        var sample = Now.AddSeconds(-90);

        Assert.True(VehicleRules.IsMoving(11.176, sample, Now));
        Assert.False(VehicleRules.IsMoving(11.176, sample, Now, maxAgeS: 60));
        Assert.Equal(600, VehicleRules.SpeedMaxAgeS);
        Assert.Equal(1.0, VehicleRules.MovingMinMps);
    }

    // The row never shows a speed without the rule having accepted it, and IsMoving implies a known speed (02 section 1.9).
    [Fact]
    public void IsMoving_implies_a_fresh_known_speed()
    {
        double?[] speeds = [null, 0, 0.5, 1.0, 2.0, 11.176];
        int[] ages = [0, 120, 600, 601, 900];
        foreach (var speed in speeds)
        {
            foreach (var age in ages)
            {
                var sample = Now.AddSeconds(-age);
                if (VehicleRules.IsMoving(speed, sample, Now))
                {
                    Assert.NotNull(VehicleRules.FreshSpeedMps(speed, sample, Now));
                }
            }
        }
    }
}
