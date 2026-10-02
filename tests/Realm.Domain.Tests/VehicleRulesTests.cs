using System.Globalization;
using System.Text.Json;
using Xunit;

namespace Realm.Domain.Tests;

// Expected values are the rule and vector T23 of 02 (section 1.9 and 5.10) and Python arithmetic (25 mph x 0.44704 = 11.176 m/s), never the code under test.
public class VehicleRulesTests
{
    private const string Prefix = "fordpass_test";

    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-30T21:25:00-05:00", CultureInfo.InvariantCulture);

    // ---- T23 (02 section 5.10), through the parser: the FordPass sensors are what the vehicle row is built from ---------------------

    // ignition On with the speed sensor at 0 (parked, brake on); then On with 25 mph and a 3 minute old sample; then On with 25 mph and a 12 minute old one.
    [Theory]
    [InlineData("0", 3, false, true)]    // a fresh reading of zero is a known speed, and not moving
    [InlineData("25", 3, true, true)]
    [InlineData("25", 12, false, false)]
    public void T23_a_vehicle_is_moving_only_with_the_ignition_on_and_a_fresh_speed_above_one_metre_per_second(
        string speedMph,
        int sampleAgeMinutes,
        bool expectedMoving,
        bool expectedSpeedKnown)
    {
        var sample = Now.AddMinutes(-sampleAgeMinutes);
        var state = FixParser.ParseVehicleState(
            Prefix,
            [
                Sensor("ignitionstatus", "ON", sample),
                Sensor("speed", speedMph, sample, ("unit_of_measurement", "mph")),
                Sensor("lastrefresh", sample.ToString("O", CultureInfo.InvariantCulture), sample),
            ]);

        var moving = VehicleRules.IsMoving(state.Ignition, state.SpeedMps, state.LastUpdateUtc, Now);
        var speed = VehicleRules.FreshSpeedMps(state.SpeedMps, state.LastUpdateUtc, Now);

        Assert.Equal(expectedMoving, moving);
        Assert.Equal(expectedSpeedKnown, speed is not null);
        if (expectedMoving)
        {
            Assert.NotNull(speed);
            Assert.Equal(11.176, speed.Value, 3);   // the 11.18 m/s of the vector
        }
    }

    // ---- the three conditions, one at a time ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData(IgnitionState.On, true)]
    [InlineData(IgnitionState.Off, false)]
    [InlineData(IgnitionState.Accessory, false)]
    [InlineData(IgnitionState.RemoteStart, false)]
    [InlineData(null, false)]
    public void Only_the_ignition_on_counts_and_a_speed_alone_never_means_moving(IgnitionState? ignition, bool expected)
    {
        Assert.Equal(expected, VehicleRules.IsMoving(ignition, 11.176, Now.AddMinutes(-1), Now));
    }

    // Ignition alone never means moving (L9, L16): On with no speed reading, or a speed that is not above 1.0 m/s.
    [Theory]
    [InlineData(null, false)]
    [InlineData(0.0, false)]
    [InlineData(0.99, false)]
    [InlineData(1.0, false)]
    [InlineData(1.01, true)]
    [InlineData(35.7632, true)]
    public void The_speed_must_be_known_and_above_one_metre_per_second(double? speedMps, bool expected)
    {
        Assert.Equal(expected, VehicleRules.IsMoving(IgnitionState.On, speedMps, Now.AddMinutes(-1), Now));
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

        Assert.Equal(expected, VehicleRules.IsMoving(IgnitionState.On, 11.176, sample, Now));
        Assert.Equal(expected, VehicleRules.FreshSpeedMps(11.176, sample, Now) is not null);
    }

    [Fact]
    public void A_vehicle_without_a_sample_time_has_no_fresh_speed_and_is_not_moving()
    {
        Assert.False(VehicleRules.IsMoving(IgnitionState.On, 11.176, null, Now));
        Assert.Null(VehicleRules.FreshSpeedMps(11.176, null, Now));
    }

    [Fact]
    public void The_maximum_age_is_a_parameter_and_defaults_to_the_600_seconds_of_the_spec()
    {
        var sample = Now.AddSeconds(-90);

        Assert.True(VehicleRules.IsMoving(IgnitionState.On, 11.176, sample, Now));
        Assert.False(VehicleRules.IsMoving(IgnitionState.On, 11.176, sample, Now, maxAgeS: 60));
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
                if (VehicleRules.IsMoving(IgnitionState.On, speed, sample, Now))
                {
                    Assert.NotNull(VehicleRules.FreshSpeedMps(speed, sample, Now));
                }
            }
        }
    }

    private static HaEntitySnapshot Sensor(string name, string state, DateTimeOffset lastUpdated, params (string Key, object? Value)[] attributes)
    {
        var map = new Dictionary<string, JsonElement>();
        foreach (var (key, value) in attributes)
        {
            map[key] = JsonSerializer.SerializeToElement(value);
        }

        return new HaEntitySnapshot($"sensor.{Prefix}_{name}", state, map, lastUpdated, lastUpdated);
    }
}
