using Realm.Domain;
using Xunit;

namespace Realm.Domain.Tests;

/// <summary>0.3.1, D125: the moves of a tracker are derived from its stored fixes alone (no trip detector). Coordinates are fictional; 0.01 degrees of latitude is about 1.11 km.</summary>
public sealed class MovementDeriverTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);
    private const double Lat = 31.0990;
    private const double Lon = -85.3410;

    private static RawFix Fix(int minute, double dLat = 0, double? accuracy = 10) =>
        new("device_tracker.pickup", FixSource.Companion, Noon.AddMinutes(minute), Lat + dLat, Lon, AccuracyM: accuracy);

    [Fact]
    public void AParkedTracker_HasNoMoves()
    {
        var fixes = Enumerable.Range(0, 10).Select(i => Fix(i * 30, i % 2 * 0.0002)).ToList();

        Assert.Empty(MovementDeriver.Derive("tracker_pickup", fixes));
    }

    [Fact]
    public void AMove_StartsAtTheLastRestingFix_AndEndsWhereTheTrackerRestedAgain()
    {
        var fixes = new[]
        {
            Fix(0), Fix(30),                       // parked
            Fix(35, 0.005), Fix(40, 0.01), Fix(45, 0.02),   // moving
            Fix(50, 0.02), Fix(56, 0.02),          // parked again
        };

        var move = Assert.Single(MovementDeriver.Derive("tracker_pickup", fixes));

        Assert.Equal("tracker_pickup", move.MemberId);
        Assert.Equal(Noon.AddMinutes(30), move.StartUtc);
        Assert.Equal(Noon.AddMinutes(45), move.EndUtc);
        Assert.InRange(move.Meters, 2100, 2300);
        Assert.Equal(Lat + 0.02, move.EndLat);
        Assert.Null(move.TopSpeedMps);
        Assert.Null(move.SpeedingCount);
        Assert.Null(move.PhoneCount);
    }

    [Fact]
    public void AMoveWithFixesMoreThanFiveMinutesApart_IsCoarse_AndOneWithCloseFixesIsDense()
    {
        var coarse = new[] { Fix(0), Fix(30, 0.02), Fix(60, 0.02) };
        var dense = new[] { Fix(0), Fix(2, 0.005), Fix(4, 0.01), Fix(6, 0.015), Fix(8, 0.02), Fix(12, 0.02) };

        Assert.Equal(TripQuality.Coarse, Assert.Single(MovementDeriver.Derive("t", coarse)).Quality);
        Assert.Equal(TripQuality.Dense, Assert.Single(MovementDeriver.Derive("t", dense)).Quality);
    }

    [Fact]
    public void JitterUnderTheMinimumPath_IsNotAMove()
    {
        // 0.0016 degrees is about 178 m: it starts a move (150 m), but the path is under 250 m.
        var fixes = new[] { Fix(0), Fix(5, 0.0016), Fix(10, 0.0016), Fix(20, 0.0016) };

        Assert.Empty(MovementDeriver.Derive("t", fixes));
    }

    [Fact]
    public void FixesMoreThanSixHoursApart_AreNotJoined()
    {
        var fixes = new[] { Fix(0), Fix(7 * 60, 0.05), Fix(7 * 60 + 30, 0.05) };

        Assert.Empty(MovementDeriver.Derive("t", fixes));
    }

    [Fact]
    public void AFixWithAPoorAccuracy_IsIgnored_AndTheInputOrderDoesNotMatter()
    {
        var fixes = new[] { Fix(60, 0.02), Fix(0), Fix(30, 0.4, accuracy: 900), Fix(30), Fix(90, 0.02) };

        var move = Assert.Single(MovementDeriver.Derive("t", fixes));

        Assert.Equal(Noon.AddMinutes(30), move.StartUtc);
        Assert.Equal(Noon.AddMinutes(60), move.EndUtc);
        Assert.InRange(move.Meters, 2100, 2300);
    }

    [Fact]
    public void AMoveStillGoingOnAtTheEndOfTheData_EndsAtTheNewestFix()
    {
        var fixes = new[] { Fix(0), Fix(2, 0.005), Fix(4, 0.01), Fix(6, 0.015) };

        var move = Assert.Single(MovementDeriver.Derive("t", fixes));

        Assert.Equal(Noon.AddMinutes(6), move.EndUtc);
    }

    [Fact]
    public void NoFixesOrOne_HaveNoMoves()
    {
        Assert.Empty(MovementDeriver.Derive("t", []));
        Assert.Empty(MovementDeriver.Derive("t", [Fix(0)]));
    }
}
