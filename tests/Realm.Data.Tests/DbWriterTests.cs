using Realm.Domain;
using Realm.Infrastructure.Data;
using Xunit;

namespace Realm.Data.Tests;

// The single writer (02 section 7.3, 03 sections 2.4, 2.12 and 2.13): flush at 100 rows or 2 s, INSERT OR IGNORE, the roster UPSERT, back-pressure
// that drops track = 0 rows first, the shutdown drain and the clean_shutdown flag. The clock is fake; only the writer's own thread runs on real time.
public class DbWriterTests
{
    [Fact]
    public async Task The_hundredth_queued_row_starts_a_flush_without_the_timer()
    {
        await using var rig = await WriterRig.StartAsync();

        for (var i = 0; i < 99; i++)
        {
            Assert.True(rig.Writer.EnqueueFix("king", TestData.Fix(i)));
        }

        Assert.Equal(99, rig.Writer.QueueDepth);
        Assert.Equal(0, rig.Writer.CommittedRows);

        Assert.True(rig.Writer.EnqueueFix("king", TestData.Fix(99)));
        var committed = await rig.NextCommitAsync();

        Assert.Equal(100, committed);
        Assert.Equal(100, rig.RowCount("fixes"));
        Assert.Equal(0, rig.Writer.QueueDepth);
        Assert.Equal(100, rig.Writer.CommittedRows);
        Assert.Equal(TestData.Start, rig.Writer.LastCommitUtc);
    }

    [Fact]
    public async Task Fewer_rows_wait_for_the_two_second_timer()
    {
        await using var rig = await WriterRig.StartAsync();
        Assert.True(rig.Writer.EnqueueFix("king", TestData.Fix(0)));
        Assert.True(rig.Writer.EnqueueFix("king", TestData.Fix(1)));
        Assert.True(rig.Writer.EnqueueFix("king", TestData.Fix(2)));

        rig.Time.Advance(DbWriter.FlushInterval - TimeSpan.FromMilliseconds(1));
        Assert.Equal(3, rig.Writer.QueueDepth);
        Assert.Equal(0, rig.Writer.CommittedRows);

        rig.Time.Advance(TimeSpan.FromMilliseconds(1));
        var committed = await rig.NextCommitAsync();

        Assert.Equal(3, committed);
        Assert.Equal(3, rig.RowCount("fixes"));
        Assert.Equal(0, rig.Writer.QueueDepth);
    }

    [Fact]
    public async Task A_fix_is_stored_with_every_column_and_reads_back_unchanged()
    {
        await using var rig = await WriterRig.StartAsync();
        var fix = TestData.Fix(0);

        Assert.True(rig.Writer.EnqueueFix("king", fix));
        await rig.Writer.FlushAsync();

        var stored = Assert.Single(await rig.Queries.GetFixesAsync("king", TestData.Start, TestData.Start.AddMinutes(1)));
        Assert.Equal(fix with { EntityId = string.Empty }, stored);
        Assert.Equal("life360", TestSql.Text(rig.FilePath, "SELECT source FROM fixes"));
        Assert.Equal(TestData.Start.ToUnixTimeMilliseconds(), TestSql.Long(rig.FilePath, "SELECT ts FROM fixes"));
        Assert.Equal(1, TestSql.Long(rig.FilePath, "SELECT track FROM fixes"));
        Assert.True(TestSql.IsNull(rig.FilePath, "SELECT reason FROM fixes"));
        Assert.Equal(1, TestSql.Long(rig.FilePath, "SELECT count(*) FROM members WHERE id = 'king'"));
    }

    [Fact]
    public async Task Fixes_are_insert_or_ignore_on_member_time_and_source()
    {
        await using var rig = await WriterRig.StartAsync();

        Assert.True(rig.Writer.EnqueueFix("king", TestData.Fix(0, lat: 38.5)));
        Assert.True(rig.Writer.EnqueueFix("king", TestData.Fix(0, lat: 39.0))); // same member, time and source: the first row stays
        Assert.True(rig.Writer.EnqueueFix("king", TestData.Fix(0, FixSource.Companion, lat: 38.7))); // another source is another row
        Assert.True(rig.Writer.EnqueueFix("queen", TestData.Fix(0, lat: 38.9))); // another member is another row
        await rig.Writer.FlushAsync();

        Assert.Equal(3, rig.RowCount("fixes"));
        Assert.Equal(38.5, (await rig.Queries.GetLatestFixAsync("king", FixSource.Life360))?.Lat);

        Assert.True(rig.Writer.EnqueueFix("king", TestData.Fix(0, lat: 40.0))); // a repeat in a later flush changes nothing either
        await rig.Writer.FlushAsync();

        Assert.Equal(3, rig.RowCount("fixes"));
        Assert.Equal(38.5, (await rig.Queries.GetLatestFixAsync("king", FixSource.Life360))?.Lat);
    }

    [Fact]
    public async Task A_fix_outside_the_track_is_stored_with_track_0_and_its_reason()
    {
        await using var rig = await WriterRig.StartAsync();

        Assert.True(rig.Writer.EnqueueFix("king", TestData.Fix(0), inTrack: false, reason: TrackReason.Accuracy));
        Assert.True(rig.Writer.EnqueueFix("king", TestData.Fix(1), inTrack: false, reason: TrackReason.Spike));
        Assert.True(rig.Writer.EnqueueFix("king", TestData.Fix(2), inTrack: false, reason: TrackReason.Priority));
        Assert.True(rig.Writer.EnqueueFix("king", TestData.Fix(3), inTrack: false, reason: TrackReason.Dropped));
        Assert.True(rig.Writer.EnqueueFix("king", TestData.Fix(4), inTrack: true, reason: TrackReason.Spike)); // a reason only means something for track = 0
        await rig.Writer.FlushAsync();

        Assert.Equal("acc", ReasonAt(rig, 0));
        Assert.Equal("spike", ReasonAt(rig, 1));
        Assert.Equal("priority", ReasonAt(rig, 2));
        Assert.Null(ReasonAt(rig, 3));
        Assert.Null(ReasonAt(rig, 4));
        Assert.Equal(4, TestSql.Long(rig.FilePath, "SELECT count(*) FROM fixes WHERE track = 0"));
        Assert.Equal(1, TestSql.Long(rig.FilePath, "SELECT count(*) FROM fixes WHERE track = 1"));
    }

    [Fact]
    public async Task A_bad_coordinate_is_refused()
    {
        await using var rig = await WriterRig.StartAsync();
        var writer = rig.Writer;

        Assert.False(writer.EnqueueFix("king", TestData.Fix(0, lat: double.NaN)));
        Assert.False(writer.EnqueueFix("king", TestData.Fix(0) with { Lon = double.PositiveInfinity }));
        Assert.Equal(0, writer.QueueDepth);
    }

    private static RosterEntry RosterRow(string entityId, RosterGroup group, int order, string? lore = null, DateTimeOffset? autoMoved = null) =>
        new(entityId, entityId.StartsWith("person.", StringComparison.Ordinal) ? RosterKind.Person : RosterKind.Tracker, group, "Name " + entityId, lore, "#E8BC4E", order, "Home Assistant + Life360", TestData.Start, TestData.Start.AddHours(1), autoMoved);

    [Fact]
    public async Task The_owners_overrides_and_the_source_values_are_stored_apart_and_read_back()
    {
        await using var rig = await WriterRig.StartAsync();
        var entry = RosterRow("person.alden", RosterGroup.People, 0, "The King") with
        {
            SourceName = "Alden (HA)",
            SourceTitle = null,
            SourceColor = "#112233",
            NameOverride = "Name person.alden",
            TitleOverride = "The King",
            ColorOverride = "#E8BC4E",
            Icon = "glyph:pet",
        };

        await rig.Writer.WriteRosterAsync([entry], CancellationToken.None);

        var stored = Assert.Single(await rig.Queries.GetRosterAsync(CancellationToken.None));
        Assert.Equal("Alden (HA)", stored.SourceName);
        Assert.Null(stored.SourceTitle);
        Assert.Equal("#112233", stored.SourceColor);
        Assert.Equal("Name person.alden", stored.NameOverride);
        Assert.Equal("The King", stored.TitleOverride);
        Assert.Equal("#E8BC4E", stored.ColorOverride);
        Assert.Equal("glyph:pet", stored.Icon);
        Assert.True(stored.IsCustomised);
    }

    [Fact]
    public async Task Keep_history_is_stored_with_the_roster_row_and_is_on_by_default()
    {
        await using var rig = await WriterRig.StartAsync();

        await rig.Writer.WriteRosterAsync(
            [RosterRow("device_tracker.pickup", RosterGroup.Vehicles, 0) with { KeepHistory = false }, RosterRow("device_tracker.wagon", RosterGroup.Vehicles, 1)],
            CancellationToken.None);

        var stored = await rig.Queries.GetRosterAsync(CancellationToken.None);
        Assert.False(Assert.Single(stored, e => e.EntityId == "device_tracker.pickup").KeepHistory);
        Assert.True(Assert.Single(stored, e => e.EntityId == "device_tracker.wagon").KeepHistory);

        await rig.Writer.WriteRosterAsync([RosterRow("device_tracker.pickup", RosterGroup.Vehicles, 0)], CancellationToken.None);

        Assert.True(Assert.Single(await rig.Queries.GetRosterAsync(CancellationToken.None), e => e.EntityId == "device_tracker.pickup").KeepHistory);
    }

    [Fact]
    public async Task The_roster_is_stored_by_entity_id_and_a_second_write_replaces_the_row()
    {
        await using var rig = await WriterRig.StartAsync();

        await rig.Writer.WriteRosterAsync([RosterRow("person.alden", RosterGroup.People, 0, "The King"), RosterRow("device_tracker.pickup", RosterGroup.NotTracked, 0, autoMoved: TestData.Start.AddDays(1))], CancellationToken.None);

        Assert.Equal(2, rig.RowCount("roster"));
        var stored = await rig.Queries.GetRosterAsync(CancellationToken.None);
        var alden = Assert.Single(stored, e => e.EntityId == "person.alden");
        Assert.Equal(RosterGroup.People, alden.Group);
        Assert.Equal(RosterKind.Person, alden.Kind);
        Assert.Equal("The King", alden.LoreTitle);
        Assert.Equal("#E8BC4E", alden.Color);
        Assert.Equal("Home Assistant + Life360", alden.Source);
        Assert.Equal(TestData.Start, alden.FirstSeenUtc);
        Assert.Equal(TestData.Start.AddHours(1), alden.LastActiveUtc);
        Assert.Null(alden.AutoMovedUtc);
        var pickup = Assert.Single(stored, e => e.EntityId == "device_tracker.pickup");
        Assert.Equal(RosterGroup.NotTracked, pickup.Group);
        Assert.Equal(RosterKind.Tracker, pickup.Kind);
        Assert.Null(pickup.LoreTitle);
        Assert.Equal(TestData.Start.AddDays(1), pickup.AutoMovedUtc);

        await rig.Writer.WriteRosterAsync([RosterRow("person.alden", RosterGroup.Vehicles, 3, "Renamed")], CancellationToken.None);

        Assert.Equal(2, rig.RowCount("roster"));
        var again = Assert.Single(await rig.Queries.GetRosterAsync(CancellationToken.None), e => e.EntityId == "person.alden");
        Assert.Equal(RosterGroup.Vehicles, again.Group);
        Assert.Equal(3, again.SortOrder);
        Assert.Equal("Renamed", again.LoreTitle);
    }

    [Fact]
    public async Task Signals_are_insert_or_ignore_and_keep_their_kind_and_value()
    {
        await using var rig = await WriterRig.StartAsync();

        Assert.True(rig.Writer.EnqueueSignal("king", new PhoneSignal(TestData.Start, PhoneSignalKind.Screen, true)));
        Assert.True(rig.Writer.EnqueueSignal("king", new PhoneSignal(TestData.Start, PhoneSignalKind.Screen, false))); // same member, time and kind: ignored
        Assert.True(rig.Writer.EnqueueSignal("king", new PhoneSignal(TestData.Start, PhoneSignalKind.Locked, false)));
        Assert.True(rig.Writer.EnqueueSignal("king", new PhoneSignal(TestData.Start.AddSeconds(5), PhoneSignalKind.AndroidAuto, null)));
        await rig.Writer.FlushAsync();

        Assert.Equal(3, rig.RowCount("signals"));
        var signals = await rig.Queries.GetPhoneSignalsAsync("king", TestData.Start, TestData.Start.AddMinutes(1));
        Assert.Equal(
            new[]
            {
                new PhoneSignal(TestData.Start, PhoneSignalKind.Screen, true),
                new PhoneSignal(TestData.Start, PhoneSignalKind.Locked, false),
                new PhoneSignal(TestData.Start.AddSeconds(5), PhoneSignalKind.AndroidAuto, null),
            },
            signals.ToArray());
        Assert.Equal("unavailable", TestSql.Text(rig.FilePath, "SELECT value FROM signals WHERE kind = 'android_auto'"));
    }

    [Fact]
    public async Task A_meta_row_is_inserted_and_replaced_by_its_key_and_an_empty_one_is_refused()
    {
        await using var rig = await WriterRig.StartAsync();

        Assert.True(rig.Writer.EnqueueMeta("ha_time_zone", "America/Chicago"));
        Assert.True(rig.Writer.EnqueueMeta("ha_time_zone", "Europe/London")); // the key exists: replaced, in the order queued
        Assert.False(rig.Writer.EnqueueMeta("ha_time_zone", string.Empty));
        Assert.False(rig.Writer.EnqueueMeta(string.Empty, "Europe/London"));
        await rig.Writer.FlushAsync();

        Assert.Equal("Europe/London", TestSql.Text(rig.FilePath, "SELECT value FROM meta WHERE key = 'ha_time_zone'"));
        Assert.Equal(1, TestSql.Long(rig.FilePath, "SELECT count(*) FROM meta WHERE key = 'ha_time_zone'"));
    }

    [Fact]
    public async Task A_full_queue_drops_track_0_rows_first_and_never_grows()
    {
        await using var rig = WriterRig.Create(); // not started: nothing is consumed while the queue fills
        var writer = rig.Writer;
        for (var i = 0; i < 9_990; i++)
        {
            Assert.True(writer.EnqueueFix("king", TestData.Fix(i)));
        }

        for (var i = 9_990; i < 10_000; i++)
        {
            Assert.True(writer.EnqueueFix("king", TestData.Fix(i), inTrack: false, reason: TrackReason.Accuracy));
        }

        Assert.Equal(DbWriter.QueueCapacity, writer.QueueDepth);
        Assert.Equal(0, writer.DroppedRows);

        // Full: a new diagnostic row is the one that is dropped.
        Assert.False(writer.EnqueueFix("king", TestData.Fix(10_000), inTrack: false, reason: TrackReason.Spike));
        Assert.Equal(1, writer.DroppedRows);

        // Full: a row that matters takes the place of the oldest diagnostic row.
        Assert.True(writer.EnqueueFix("king", TestData.Fix(10_001)));
        Assert.Equal(DbWriter.QueueCapacity, writer.QueueDepth);
        Assert.Equal(2, writer.DroppedRows);
        for (var i = 0; i < 9; i++)
        {
            Assert.True(writer.EnqueueFix("king", TestData.Fix(10_002 + i)));
        }

        Assert.Equal(DbWriter.QueueCapacity, writer.QueueDepth);
        Assert.Equal(11, writer.DroppedRows);

        // No diagnostic row is left: now a row that matters is refused too, whatever its kind.
        Assert.False(writer.EnqueueFix("king", TestData.Fix(20_000)));
        Assert.False(writer.EnqueueSignal("king", new PhoneSignal(TestData.Start, PhoneSignalKind.Screen, true)));
        Assert.Equal(DbWriter.QueueCapacity, writer.QueueDepth);
        Assert.Equal(13, writer.DroppedRows);
        Assert.Contains(rig.Log.Messages, message => message.Contains("rows dropped so far", StringComparison.Ordinal));

        // What is left is written, and none of it is a diagnostic row.
        await writer.StartAsync(CancellationToken.None);
        var committed = await rig.NextCommitAsync();

        Assert.Equal(DbWriter.QueueCapacity, committed);
        Assert.Equal(DbWriter.QueueCapacity, rig.RowCount("fixes"));
        Assert.Equal(0, TestSql.Long(rig.FilePath, "SELECT count(*) FROM fixes WHERE track = 0"));
        await rig.StopAsync();
    }

    [Fact]
    public async Task Concurrent_producers_lose_and_duplicate_nothing()
    {
        await using var rig = await WriterRig.StartAsync();
        var writer = rig.Writer;

        var producers = Enumerable.Range(0, 4).Select(producer => Task.Run(() =>
        {
            for (var i = 0; i < 500; i++)
            {
                Assert.True(writer.EnqueueFix("king", TestData.Fix((producer * 500) + i)));
            }
        }));
        await Task.WhenAll(producers);
        await writer.FlushAsync();

        Assert.Equal(2_000, rig.RowCount("fixes"));
        Assert.Equal(2_000, TestSql.Long(rig.FilePath, "SELECT count(DISTINCT ts) FROM fixes"));
        Assert.Equal(0, writer.QueueDepth);
        Assert.Equal(2_000, writer.CommittedRows);
    }

    // CR2-011: RowsCommitted is raised after the commit. A subscriber that throws used to turn the committed run into a failed flush: the rows were put back,
    // committed again (idempotent) and the event threw again, so the writer faulted for ever. Now the failure is logged once and nothing else changes.
    [Fact]
    public async Task A_subscriber_that_throws_neither_faults_the_writer_nor_starves_the_other_subscribers()
    {
        await using var rig = await WriterRig.StartAsync();
        var seen = new List<int>();
        rig.Writer.RowsCommitted += _ => throw new InvalidOperationException("a bug in a subscriber");
        rig.Writer.RowsCommitted += rows =>
        {
            lock (seen)
            {
                seen.Add(rows);
            }
        };

        for (var i = 0; i < DbWriter.FlushRows; i++)
        {
            Assert.True(rig.Writer.EnqueueFix("king", TestData.Fix(i)));
        }

        Assert.Equal(DbWriter.FlushRows, await rig.NextCommitAsync());
        await WriterRig.EventuallyAsync(() => SeenCount(seen) == 1);

        // Past the 1 s back-off of a faulted loop and a 2 s flush: with the old code the rows would be committed again here.
        rig.Time.Advance(TimeSpan.FromSeconds(5));
        Assert.True(rig.Writer.EnqueueFix("king", TestData.Fix(DbWriter.FlushRows)));
        rig.Time.Advance(DbWriter.FlushInterval);
        await WriterRig.EventuallyAsync(() => SeenCount(seen) == 2);

        Assert.False(rig.Writer.Health.IsFaulted);
        Assert.Equal(0, rig.Writer.Health.FaultCount);
        Assert.Equal(DbWriter.FlushRows + 1, rig.Writer.CommittedRows);
        Assert.Equal(DbWriter.FlushRows + 1, rig.RowCount("fixes"));
        Assert.Equal(0, rig.Writer.QueueDepth);
        lock (seen)
        {
            Assert.Equal(new[] { DbWriter.FlushRows, 1 }, seen.ToArray());
        }

        Assert.Single(rig.Log.Messages, message => message.Contains("RowsCommitted subscriber failed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Shutdown_drains_the_queue_marks_the_database_clean_and_checkpoints_the_wal()
    {
        await using var rig = await WriterRig.StartAsync();
        Assert.Equal("0", TestSql.Text(rig.FilePath, "SELECT value FROM meta WHERE key = 'clean_shutdown'")); // the bootstrap's mark for this run
        for (var i = 0; i < 5; i++)
        {
            Assert.True(rig.Writer.EnqueueFix("king", TestData.Fix(i)));
        }

        Assert.True(rig.Writer.EnqueueSignal("king", new PhoneSignal(TestData.Start, PhoneSignalKind.Locked, true)));

        await rig.StopAsync(); // no clock tick: the drain, not the timer, writes them

        Assert.Equal(0, LogBytes(rig.FilePath));   // wal_checkpoint(TRUNCATE) left the log empty (or SQLite already removed the file)
        Assert.Equal(5, rig.RowCount("fixes"));
        Assert.Equal(1, rig.RowCount("signals"));
        Assert.Equal(0, rig.Writer.QueueDepth);
        Assert.Equal("1", TestSql.Text(rig.FilePath, "SELECT value FROM meta WHERE key = 'clean_shutdown'"));
    }

    [Fact]
    public async Task A_row_offered_after_shutdown_is_refused_and_a_trip_close_says_so()
    {
        await using var rig = await WriterRig.StartAsync();
        await rig.StopAsync();

        Assert.False(rig.Writer.EnqueueFix("king", TestData.Fix(0)));
        Assert.False(rig.Writer.EnqueueSignal("king", new PhoneSignal(TestData.Start, PhoneSignalKind.Locked, true)));
        Assert.Equal(2, rig.Writer.DroppedRows);
        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Writer.WriteTripAsync("king", TestData.Trip(0), 1, "h"));
        Assert.Equal(0, rig.RowCount("fixes"));
    }

    [Fact]
    public async Task A_failed_commit_keeps_the_rows_and_the_writer_recovers_after_its_back_off()
    {
        await using var rig = await WriterRig.StartAsync();
        TestSql.Exec(rig.FilePath, "CREATE TRIGGER refuse_fixes BEFORE INSERT ON fixes BEGIN SELECT RAISE(ABORT, 'refused'); END");
        for (var i = 0; i < 3; i++)
        {
            Assert.True(rig.Writer.EnqueueFix("king", TestData.Fix(i)));
        }

        rig.Time.Advance(DbWriter.FlushInterval);
        await WriterRig.EventuallyAsync(() => rig.Writer.Health.IsFaulted);

        Assert.Equal(3, rig.Writer.QueueDepth); // nothing was lost
        Assert.Equal(0, rig.RowCount("fixes"));
        Assert.Equal(nameof(Microsoft.Data.Sqlite.SqliteException), rig.Writer.Health.Reason);
        Assert.Equal(0, rig.Writer.CommittedRows);

        TestSql.Exec(rig.FilePath, "DROP TRIGGER refuse_fixes");
        for (var attempt = 0; attempt < 3000 && rig.Writer.CommittedRows == 0; attempt++)
        {
            rig.Time.Advance(TimeSpan.FromSeconds(1)); // the 1 s back-off of ResilientLoop, then the next 2 s flush
            await Task.Delay(10);
        }

        Assert.Equal(3, rig.Writer.CommittedRows);
        Assert.Equal(3, rig.RowCount("fixes"));
        Assert.Equal(0, rig.Writer.QueueDepth);
        Assert.True(rig.Writer.Health.FaultCount >= 1);
    }

    [Fact]
    public async Task A_shutdown_that_cannot_write_the_queue_does_not_mark_the_database_clean()
    {
        await using var rig = await WriterRig.StartAsync();
        TestSql.Exec(rig.FilePath, "CREATE TRIGGER refuse_fixes BEFORE INSERT ON fixes BEGIN SELECT RAISE(ABORT, 'refused'); END");
        Assert.True(rig.Writer.EnqueueFix("king", TestData.Fix(0)));
        Assert.True(rig.Writer.EnqueueFix("king", TestData.Fix(1)));

        await rig.StopAsync();

        Assert.Equal("0", TestSql.Text(rig.FilePath, "SELECT value FROM meta WHERE key = 'clean_shutdown'")); // the next start runs quick_check
        Assert.Equal(0, rig.RowCount("fixes"));
        Assert.Contains(rig.Log.Messages, message => message.Contains("clean_shutdown stays 0", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_trip_close_is_written_after_the_rows_queued_before_it()
    {
        await using var rig = await WriterRig.StartAsync();
        Assert.True(rig.Writer.EnqueueFix("king", TestData.Fix(0)));
        Assert.True(rig.Writer.EnqueueFix("king", TestData.Fix(1)));

        var written = await rig.Writer.WriteTripAsync("king", TestData.Trip(0), 3, "hash-1");

        Assert.True(written);
        Assert.Equal(2, rig.RowCount("fixes")); // committed before the close returned
        Assert.Equal(1, rig.RowCount("trips"));
        Assert.Equal(2, rig.RowCount("trip_events"));
        var start = TestData.Start.ToUnixTimeMilliseconds();
        Assert.Equal("12345.5", TripColumn(rig, "distance_m"));
        Assert.Equal("12345.5", TripColumn(rig, "distance_gps_m"));
        Assert.Equal("gps", TripColumn(rig, "distance_source"));
        Assert.Equal("dense", TripColumn(rig, "quality"));
        Assert.Equal("stop", TripColumn(rig, "ended_by"));
        Assert.Equal("companion,life360", TripColumn(rig, "source_mask"));
        Assert.Equal("3", TripColumn(rig, "algo_version"));
        Assert.Equal("hash-1", TripColumn(rig, "derive_hash"));
        Assert.Equal("1200", TripColumn(rig, "duration_s"));
        Assert.Equal("0", TripColumn(rig, "has_gap"));
        Assert.Equal("home", TripColumn(rig, "start_place_id"));
        Assert.Null(TripColumn(rig, "end_place_id"));
        Assert.Null(TripColumn(rig, "vehicle_id"));
        Assert.Null(TripColumn(rig, "accel_count"));
        Assert.Null(TripColumn(rig, "braking_count"));
        Assert.Equal(start.ToString(System.Globalization.CultureInfo.InvariantCulture), TripColumn(rig, "created_ts"));
        Assert.Equal("36.5", TestSql.Text(rig.FilePath, "SELECT peak FROM trip_events WHERE kind = 'speeding'"));
        Assert.Equal("38.55", TestSql.Text(rig.FilePath, "SELECT lat FROM trip_events WHERE kind = 'speeding'"));
        Assert.Equal("42", TestSql.Text(rig.FilePath, "SELECT peak FROM trip_events WHERE kind = 'phone'"));
        Assert.True(TestSql.IsNull(rig.FilePath, "SELECT lat FROM trip_events WHERE kind = 'phone'"));
    }

    [Fact]
    public async Task A_trip_of_the_same_member_and_start_is_written_once()
    {
        await using var rig = await WriterRig.StartAsync();

        Assert.True(await rig.Writer.WriteTripAsync("king", TestData.Trip(0), 3, "hash-1"));
        Assert.False(await rig.Writer.WriteTripAsync("king", TestData.Trip(0), 4, "hash-2")); // nothing changes, not even the stamp
        Assert.True(await rig.Writer.WriteTripAsync("queen", TestData.Trip(0), 3, "hash-1")); // another member
        Assert.True(await rig.Writer.WriteTripAsync("king", TestData.Trip(60), 3, "hash-1")); // another start

        Assert.Equal(3, rig.RowCount("trips"));
        Assert.Equal(6, rig.RowCount("trip_events"));
        Assert.Equal(
            "3",
            TestSql.Text(rig.FilePath, $"SELECT algo_version FROM trips WHERE member_id = 'king' AND start_ts = {TestData.Start.ToUnixTimeMilliseconds()}"));
    }

    // The size of the write-ahead log in bytes. A log without a file is an empty one: SQLite removes the file when the last connection closes, which another
    // test clearing the pools can cause at any moment. One stat, so the file cannot vanish between a check for it and a read of its length.
    private static long LogBytes(string databasePath)
    {
        try
        {
            return new FileInfo(databasePath + "-wal").Length;
        }
        catch (FileNotFoundException)
        {
            return 0;
        }
    }

    private static int SeenCount(List<int> seen)
    {
        lock (seen)
        {
            return seen.Count;
        }
    }

    private static string? ReasonAt(WriterRig rig, int second)
    {
        var ts = TestData.Start.AddSeconds(second).ToUnixTimeMilliseconds();
        return TestSql.Text(rig.FilePath, $"SELECT reason FROM fixes WHERE ts = {ts}");
    }

    private static string? TripColumn(WriterRig rig, string column)
    {
        return TestSql.Text(rig.FilePath, $"SELECT {column} FROM trips WHERE member_id = 'king'");
    }
}
