using Realm.Domain;
using Xunit;

namespace Realm.Data.Tests;

// The read side (02 sections 4.7, 6.5 to 6.7 and 7.5) over rows the writer stored: ranges are half open, order is stated, absence is null or empty.
public class SqliteRealmQueriesTests
{
    private static DateTimeOffset At(int second)
    {
        return TestData.Start.AddSeconds(second);
    }

    [Fact]
    public async Task Fixes_of_one_member_come_back_oldest_first_in_a_half_open_range()
    {
        await using var rig = await WriterRig.StartAsync();
        Assert.True(rig.Writer.EnqueueFix("king", TestData.Fix(0)));
        Assert.True(rig.Writer.EnqueueFix("king", TestData.Fix(20)));
        Assert.True(rig.Writer.EnqueueFix("king", TestData.Fix(10)));
        Assert.True(rig.Writer.EnqueueFix("king", TestData.Fix(10, FixSource.Companion, lat: 38.7)));
        Assert.True(rig.Writer.EnqueueFix("queen", TestData.Fix(10, lat: 38.9)));
        await rig.Writer.FlushAsync();

        var all = await rig.Queries.GetFixesAsync("king", At(0), At(21));
        var window = await rig.Queries.GetFixesAsync("king", At(10), At(20));

        Assert.Equal(new[] { At(0), At(10), At(10), At(20) }, all.Select(fix => fix.Ts).ToArray());
        Assert.Equal(new[] { FixSource.Companion, FixSource.Life360 }, window.Select(fix => fix.Source).ToArray()); // from inclusive, to exclusive
        Assert.Empty(await rig.Queries.GetFixesAsync("king", At(21), At(60)));
        Assert.Empty(await rig.Queries.GetFixesAsync("cryptid", At(0), At(60)));
    }

    [Fact]
    public async Task The_latest_fix_is_per_source_and_null_when_there_is_none()
    {
        await using var rig = await WriterRig.StartAsync();
        Assert.Null(await rig.Queries.GetLatestFixAsync("king", FixSource.Life360));

        Assert.True(rig.Writer.EnqueueFix("king", TestData.Fix(0, lat: 38.1)));
        Assert.True(rig.Writer.EnqueueFix("king", TestData.Fix(30, lat: 38.3)));
        Assert.True(rig.Writer.EnqueueFix("king", TestData.Fix(60, FixSource.Companion, lat: 38.6)));
        await rig.Writer.FlushAsync();

        Assert.Equal(At(30), (await rig.Queries.GetLatestFixAsync("king", FixSource.Life360))?.Ts);
        Assert.Equal(38.3, (await rig.Queries.GetLatestFixAsync("king", FixSource.Life360))?.Lat);
        Assert.Equal(At(60), (await rig.Queries.GetLatestFixAsync("king", FixSource.Companion))?.Ts);
        Assert.Null(await rig.Queries.GetLatestFixAsync("queen", FixSource.Life360));
    }

    [Fact]
    public async Task Fix_times_are_those_of_one_member_source_in_a_half_open_range()
    {
        await using var rig = await WriterRig.StartAsync();
        for (var i = 0; i < 5; i++)
        {
            Assert.True(rig.Writer.EnqueueFix("king", TestData.Fix(i * 10)));
        }

        Assert.True(rig.Writer.EnqueueFix("king", TestData.Fix(15, FixSource.Companion)));
        await rig.Writer.FlushAsync();

        var times = await rig.Queries.GetFixTimesAsync("king", FixSource.Life360, At(10), At(40));

        Assert.Equal(new[] { At(10), At(20), At(30) }, times.ToArray());
        Assert.Equal(new[] { At(15) }, (await rig.Queries.GetFixTimesAsync("king", FixSource.Companion, At(0), At(60))).ToArray());
    }

    [Fact]
    public async Task The_latest_address_fix_is_at_or_before_the_time_and_has_an_address()
    {
        await using var rig = await WriterRig.StartAsync();
        Assert.True(rig.Writer.EnqueueFix("king", TestData.Fix(0, address: "1 Example Rd, Highmeadow")));
        Assert.True(rig.Writer.EnqueueFix("king", TestData.Fix(10, address: null)));
        Assert.True(rig.Writer.EnqueueFix("king", TestData.Fix(20, address: "2 Example Rd, Highmeadow")));
        await rig.Writer.FlushAsync();

        Assert.Equal(At(0), (await rig.Queries.GetLatestAddressFixAsync("king", At(15)))?.Ts); // the fix at 10 has no address
        Assert.Equal(At(20), (await rig.Queries.GetLatestAddressFixAsync("king", At(20)))?.Ts); // at or before: inclusive
        Assert.Equal("2 Example Rd, Highmeadow", (await rig.Queries.GetLatestAddressFixAsync("king", At(99)))?.Address);
        Assert.Null(await rig.Queries.GetLatestAddressFixAsync("king", At(-1)));
        Assert.Null(await rig.Queries.GetLatestAddressFixAsync("queen", At(99)));
    }

    [Fact]
    public async Task Phone_signals_exclude_activity_rows_and_keep_unavailable_as_null()
    {
        await using var rig = await WriterRig.StartAsync();
        Assert.True(rig.Writer.EnqueueSignal("king", new PhoneSignal(At(0), PhoneSignalKind.Screen, true)));
        Assert.True(rig.Writer.EnqueueSignal("king", new PhoneSignal(At(5), PhoneSignalKind.Screen, false)));
        Assert.True(rig.Writer.EnqueueSignal("king", new PhoneSignal(At(6), PhoneSignalKind.Locked, null)));
        Assert.True(rig.Writer.EnqueueSignal("king", new PhoneSignal(At(60), PhoneSignalKind.AndroidAuto, true)));
        await rig.Writer.FlushAsync();
        // The activity transitions are written by a later slice; the table accepts them and the phone-use input must not see them.
        TestSql.Exec(
            rig.FilePath,
            $"INSERT INTO signals(member_id, ts, kind, value) VALUES ('king', {At(3).ToUnixTimeMilliseconds()}, 'activity', 'in_vehicle')");

        var signals = await rig.Queries.GetPhoneSignalsAsync("king", At(0), At(60));

        Assert.Equal(
            new[]
            {
                new PhoneSignal(At(0), PhoneSignalKind.Screen, true),
                new PhoneSignal(At(5), PhoneSignalKind.Screen, false),
                new PhoneSignal(At(6), PhoneSignalKind.Locked, null),
            },
            signals.ToArray());
    }

    [Fact]
    public async Task Trips_come_back_newest_first_for_one_member_or_everyone_in_a_half_open_range()
    {
        await using var rig = await WriterRig.StartAsync();
        var coarse = TestData.Trip(30, TripQuality.Coarse) with
        {
            TopSpeedMps = null,
            TopSpeedAtUtc = null,
            TopSpeedStreet = null,
            SpeedingCount = null,
            PhoneCount = null,
            SpeedingEpisodes = [],
            PhoneEvents = [],
        };
        Assert.True(await rig.Writer.WriteTripAsync("king", TestData.Trip(0), 1, "h"));
        Assert.True(await rig.Writer.WriteTripAsync("king", coarse, 1, "h"));
        Assert.True(await rig.Writer.WriteTripAsync("queen", TestData.Trip(10), 1, "h"));
        Assert.True(await rig.Writer.WriteTripAsync("king", TestData.Trip(60), 1, "h"));
        var from = TestData.Start;
        var to = TestData.Start.AddMinutes(60);

        var everyone = await rig.Queries.GetTripsAsync(from, to, null);
        var king = await rig.Queries.GetTripsAsync(from, to, "king");

        Assert.Equal(new[] { "king", "queen", "king" }, everyone.Select(trip => trip.MemberId).ToArray());
        Assert.Equal(new[] { TestData.Start.AddMinutes(30), TestData.Start }, king.Select(trip => trip.StartUtc).ToArray());
        Assert.Empty(await rig.Queries.GetTripsAsync(from, to, "jester"));
        Assert.Equal(
            new StatsTrip(
                "king",
                TestData.Start,
                TestData.Start.AddMinutes(20),
                12345.5,
                TripQuality.Dense,
                DistanceBasis.Gps,
                31.25,
                TestData.Start.AddMinutes(9),
                "County Rd 1",
                1,
                1,
                "home",
                null,
                "Example Rd",
                null),
            king[1]);
        var coarseRow = king[0];
        Assert.Equal(TripQuality.Coarse, coarseRow.Quality);
        Assert.Null(coarseRow.TopSpeedMps);
        Assert.Null(coarseRow.TopSpeedAtUtc);
        Assert.Null(coarseRow.SpeedingCount);
        Assert.Null(coarseRow.PhoneCount);
    }

    [Fact]
    public async Task The_recording_start_is_the_earliest_fix_even_when_an_older_one_arrives_later()
    {
        await using var rig = await WriterRig.StartAsync();
        Assert.True(rig.Writer.EnqueueFix("king", TestData.Fix(100)));
        Assert.True(rig.Writer.EnqueueFix("king", TestData.Fix(50, FixSource.Companion)));
        Assert.True(rig.Writer.EnqueueFix("queen", TestData.Fix(70)));
        await rig.Writer.FlushAsync();

        var first = await rig.Queries.GetRecordingStartsAsync();

        Assert.Equal(At(50), first["king"]);
        Assert.Equal(At(70), first["queen"]);

        // A gap-fill (02 section 8) stores older fixes later: the start moves back. A newer fix never moves it forward.
        Assert.True(rig.Writer.EnqueueFix("king", TestData.Fix(-600)));
        Assert.True(rig.Writer.EnqueueFix("queen", TestData.Fix(500)));
        await rig.Writer.FlushAsync();

        var second = await rig.Queries.GetRecordingStartsAsync();

        Assert.Equal(At(-600), second["king"]);
        Assert.Equal(At(70), second["queen"]);
    }

    [Fact]
    public async Task A_member_with_a_trip_but_no_fix_has_no_recording_start()
    {
        await using var rig = await WriterRig.StartAsync();
        Assert.True(await rig.Writer.WriteTripAsync("jester", TestData.Trip(0), 1, "h"));

        var starts = await rig.Queries.GetRecordingStartsAsync();

        Assert.Empty(starts);
        Assert.Equal(1, rig.RowCount("members"));
    }
}
