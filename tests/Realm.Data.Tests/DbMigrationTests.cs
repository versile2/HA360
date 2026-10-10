using System.Data.Common;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Realm.Infrastructure.Data;
using Xunit;

namespace Realm.Data.Tests;

// The schema scripts and their runner (02 section 7.1, 7.2, 7.8; 03 section 2.12, R-070). Every test works on its own
// temporary database file. The expectations are typed out of the DDL of 02 section 7.2, not read from the script.
public class DbMigrationTests
{
    private sealed record Col(string Name, string Type, bool NotNull = false, string? Default = null, int Pk = 0);

    private static readonly DateTimeOffset Epoch = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    private static long s_nextTs;

    // PRAGMA table_info order, declared type, NOT NULL, DEFAULT and primary-key position of every column of 02 section 7.2.
    private static readonly Dictionary<string, Col[]> Tables = new(StringComparer.Ordinal)
    {
        ["meta"] = [C("key", "TEXT", pk: 1), C("value", "TEXT", notNull: true)],
        ["members"] =
        [
            C("id", "TEXT", pk: 1), C("life360_id", "TEXT"), C("recording_start", "INTEGER"),
            C("first_seen_utc", "INTEGER", notNull: true), C("last_configured_utc", "INTEGER", notNull: true),
        ],
        ["fixes"] =
        [
            C("id", "INTEGER", pk: 1), C("member_id", "TEXT", notNull: true), C("ts", "INTEGER", notNull: true),
            C("source", "TEXT", notNull: true), C("lat", "REAL", notNull: true), C("lon", "REAL", notNull: true),
            C("acc_m", "REAL"), C("speed_mps", "REAL"), C("heading_deg", "REAL"), C("alt_m", "REAL"),
            C("battery_pct", "INTEGER"), C("charging", "INTEGER"), C("driving", "INTEGER"), C("address", "TEXT"),
            C("track", "INTEGER", notNull: true, dflt: "1"), C("reason", "TEXT"),
        ],
        ["roster"] =
        [
            C("entity_id", "TEXT", pk: 1), C("kind", "TEXT", notNull: true), C("grp", "TEXT", notNull: true),
            C("display_name", "TEXT", notNull: true), C("lore_title", "TEXT"), C("color", "TEXT", notNull: true),
            C("sort_order", "INTEGER", notNull: true), C("source", "TEXT", notNull: true),
            C("first_seen", "INTEGER", notNull: true), C("last_active", "INTEGER", notNull: true), C("auto_moved_at", "INTEGER"),
            C("source_name", "TEXT"), C("source_title", "TEXT"), C("source_color", "TEXT"),
            C("name_override", "TEXT"), C("title_override", "TEXT"), C("color_override", "TEXT"), C("icon", "TEXT"),
            C("keep_history", "INTEGER", notNull: true, dflt: "1"),
        ],
        ["signals"] =
        [
            C("id", "INTEGER", pk: 1), C("member_id", "TEXT", notNull: true), C("ts", "INTEGER", notNull: true),
            C("kind", "TEXT", notNull: true), C("value", "TEXT", notNull: true),
        ],
        ["vehicle_drives"] =
        [
            C("id", "INTEGER", pk: 1), C("vehicle_id", "TEXT", notNull: true),
            C("start_earliest_ts", "INTEGER", notNull: true), C("start_ts", "INTEGER", notNull: true),
            C("end_ts", "INTEGER", notNull: true), C("end_latest_ts", "INTEGER", notNull: true),
            C("odo_start_m", "REAL", notNull: true), C("odo_end_m", "REAL", notNull: true), C("distance_m", "REAL", notNull: true),
            C("fuel_start_pct", "INTEGER"), C("fuel_end_pct", "INTEGER"),
            C("pos_before_lat", "REAL"), C("pos_before_lon", "REAL"), C("pos_after_lat", "REAL"), C("pos_after_lon", "REAL"),
            C("ign_unseen", "INTEGER", notNull: true, dflt: "0"),
        ],
        ["trips"] =
        [
            C("id", "INTEGER", pk: 1), C("member_id", "TEXT", notNull: true),
            C("start_ts", "INTEGER", notNull: true), C("end_ts", "INTEGER", notNull: true), C("duration_s", "INTEGER", notNull: true),
            C("start_lat", "REAL", notNull: true), C("start_lon", "REAL", notNull: true),
            C("end_lat", "REAL", notNull: true), C("end_lon", "REAL", notNull: true),
            C("start_place_id", "TEXT"), C("end_place_id", "TEXT"), C("start_street", "TEXT"), C("end_street", "TEXT"),
            C("distance_m", "REAL", notNull: true), C("distance_gps_m", "REAL", notNull: true),
            C("distance_source", "TEXT", notNull: true), C("vehicle_id", "TEXT"), C("vehicle_drive_id", "INTEGER"),
            C("top_speed_mps", "REAL"), C("top_speed_ts", "INTEGER"), C("top_speed_street", "TEXT"),
            C("speeding_count", "INTEGER"), C("phone_count", "INTEGER"), C("accel_count", "INTEGER"), C("braking_count", "INTEGER"),
            C("quality", "TEXT", notNull: true), C("has_gap", "INTEGER", notNull: true, dflt: "0"),
            C("ended_by", "TEXT", notNull: true), C("source_mask", "TEXT", notNull: true),
            C("algo_version", "INTEGER", notNull: true), C("derive_hash", "TEXT", notNull: true), C("created_ts", "INTEGER", notNull: true),
        ],
        ["trip_events"] =
        [
            C("id", "INTEGER", pk: 1), C("trip_id", "INTEGER", notNull: true), C("kind", "TEXT", notNull: true),
            C("start_ts", "INTEGER", notNull: true), C("end_ts", "INTEGER", notNull: true),
            C("peak", "REAL"), C("lat", "REAL"), C("lon", "REAL"),
        ],
        ["job_state"] =
        [
            C("name", "TEXT", pk: 1), C("cursor", "TEXT"), C("last_run_ts", "INTEGER"), C("ok", "INTEGER"), C("detail", "TEXT"),
        ],
    };

    // PRAGMA index_list plus index_info: origin (c = CREATE INDEX, u = UNIQUE constraint, pk = text primary key), then the columns.
    // Only explicit indexes carry their name; the automatic ones are named by SQLite.
    private static readonly Dictionary<string, string[]> Indexes = new(StringComparer.Ordinal)
    {
        ["meta"] = ["pk unique (key)"],
        ["members"] = ["pk unique (id)"],
        ["fixes"] = ["u unique (member_id, ts, source)"],
        ["roster"] = ["pk unique (entity_id)"],
        ["signals"] = ["u unique (member_id, ts, kind)"],
        ["vehicle_drives"] = ["u unique (vehicle_id, start_ts)"],
        ["trips"] = ["u unique (member_id, start_ts)", "c ix_trips_start plain (start_ts)"],
        ["trip_events"] = ["c ix_trip_events_trip plain (trip_id)"],
        ["job_state"] = ["pk unique (name)"],
    };

    private const string ToMembers = "member_id -> members.id (update NO ACTION, delete NO ACTION)";

    // PRAGMA foreign_key_list: column, parent table and column, ON UPDATE and ON DELETE.
    private static readonly Dictionary<string, string[]> ForeignKeys = new(StringComparer.Ordinal)
    {
        ["meta"] = [],
        ["members"] = [],
        ["fixes"] = [ToMembers],
        ["roster"] = [],
        ["signals"] = [ToMembers],
        ["vehicle_drives"] = [],
        ["trips"] = [ToMembers, "vehicle_drive_id -> vehicle_drives.id (update NO ACTION, delete NO ACTION)"],
        ["trip_events"] = ["trip_id -> trips.id (update NO ACTION, delete CASCADE)"],
        ["job_state"] = [],
    };

    public static TheoryData<string> TableNames
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var name in Tables.Keys)
            {
                data.Add(name);
            }

            return data;
        }
    }

    [Fact]
    public void The_ddl_is_embedded_as_Sql_0001_init_sql()
    {
        var scripts = SchemaRunner.LoadEmbeddedScripts();

        Assert.Contains("Sql/0001_init.sql", typeof(SchemaRunner).Assembly.GetManifestResourceNames());
        Assert.Contains(scripts, s => s.Version == 1 && s.Name == "Sql/0001_init.sql" && s.Sql.Contains("CREATE TABLE meta"));
        Assert.Equal(scripts.Select(s => s.Version).Order().ToArray(), scripts.Select(s => s.Version).ToArray());
    }

    [Fact]
    public void The_roster_script_is_embedded_as_Sql_0002_roster_sql()
    {
        var scripts = SchemaRunner.LoadEmbeddedScripts();

        Assert.Contains("Sql/0002_roster.sql", typeof(SchemaRunner).Assembly.GetManifestResourceNames());
        Assert.Contains(scripts, s => s.Version == 2 && s.Name == "Sql/0002_roster.sql" && s.Sql.Contains("CREATE TABLE roster", StringComparison.Ordinal));
    }

    [Fact]
    public void The_override_script_is_embedded_as_Sql_0003_roster_overrides_sql()
    {
        var scripts = SchemaRunner.LoadEmbeddedScripts();

        Assert.Contains("Sql/0003_roster_overrides.sql", typeof(SchemaRunner).Assembly.GetManifestResourceNames());
        Assert.Contains(scripts, s => s.Version == 3 && s.Name == "Sql/0003_roster_overrides.sql");
    }

    [Fact]
    public void The_tracker_history_script_is_embedded_as_Sql_0004_and_adds_keep_history_on_for_every_existing_row()
    {
        var scripts = SchemaRunner.LoadEmbeddedScripts();
        Assert.Contains(scripts, s => s.Version == 4 && s.Name == "Sql/0004_tracker_history.sql");

        using var db = new TempDatabase();
        using var connection = RealmDb.OpenConnection(db.FilePath, pooling: false);
        Assert.Equal(3, SchemaRunner.Apply(connection, scripts.Where(s => s.Version <= 3).ToArray(), NullLogger.Instance));
        Exec(connection, "INSERT INTO roster(entity_id, kind, grp, display_name, lore_title, color, sort_order, source, first_seen, last_active) VALUES ('device_tracker.t', 'tracker', 'vehicles', 'Wagon', NULL, '#E8BC4E', 0, 'Home Assistant', 1, 2)");

        Assert.Equal(HighestScriptNumber(), SchemaRunner.Apply(connection, scripts, NullLogger.Instance));

        Assert.Equal(1, ScalarInt(connection, "SELECT keep_history FROM roster WHERE entity_id = 'device_tracker.t'"));
    }

    // 0.2.1 (D117): a roster row of 0.2.0 keeps its name, title and colour; they count as the owner's until the first discovery.
    [Fact]
    public void Upgrading_a_0_2_0_roster_row_turns_its_name_and_title_into_overrides()
    {
        using var db = new TempDatabase();
        using var connection = RealmDb.OpenConnection(db.FilePath, pooling: false);
        var scripts = SchemaRunner.LoadEmbeddedScripts();
        Assert.Equal(2, SchemaRunner.Apply(connection, scripts.Where(s => s.Version <= 2).ToArray(), NullLogger.Instance));
        Exec(connection, "INSERT INTO roster(entity_id, kind, grp, display_name, lore_title, color, sort_order, source, first_seen, last_active) VALUES ('person.a', 'person', 'people', 'Alden', 'The King', '#E8BC4E', 0, 'Home Assistant', 1, 2)");

        Assert.Equal(HighestScriptNumber(), SchemaRunner.Apply(connection, scripts, NullLogger.Instance));

        Assert.Equal(1, ScalarInt(connection, "SELECT count(*) FROM roster WHERE source_name = 'Alden' AND name_override = 'Alden' AND title_override = 'The King' AND source_color = '#E8BC4E' AND color_override IS NULL AND icon IS NULL"));
    }

    // 0.2.0 (D114): a database of 0.1 has vehicle_samples; the new script drops it and adds the roster, and keeps everything else.
    [Fact]
    public void Upgrading_from_the_first_script_drops_vehicle_samples_and_adds_the_roster()
    {
        using var db = new TempDatabase();
        using var connection = RealmDb.OpenConnection(db.FilePath, pooling: false);
        var scripts = SchemaRunner.LoadEmbeddedScripts();
        Assert.Equal(1, SchemaRunner.Apply(connection, scripts.Where(s => s.Version == 1).ToArray(), NullLogger.Instance));
        Exec(connection, "INSERT INTO members(id, first_seen_utc, last_configured_utc) VALUES ('king', 1, 2)");
        Exec(connection, "INSERT INTO vehicle_samples(vehicle_id, ts, fuel_pct) VALUES ('wagon', 1, 55)");
        Assert.Equal(1, ScalarInt(connection, "SELECT count(*) FROM vehicle_samples"));

        var version = SchemaRunner.Apply(connection, scripts, NullLogger.Instance);

        Assert.Equal(HighestScriptNumber(), version);
        Assert.Equal(0, ScalarInt(connection, "SELECT count(*) FROM sqlite_master WHERE name = 'vehicle_samples'"));
        Assert.Equal(1, ScalarInt(connection, "SELECT count(*) FROM sqlite_master WHERE name = 'roster'"));
        Assert.Equal(1, ScalarInt(connection, "SELECT count(*) FROM members"));
    }

    [Fact]
    public void The_database_holds_exactly_the_tables_and_explicit_indexes_of_the_ddl()
    {
        using var db = new TempDatabase();
        Bootstrap(db).Run();
        using var connection = RealmDb.OpenConnection(db.FilePath, pooling: false);

        Assert.Equal(
            Tables.Keys.Order(StringComparer.Ordinal).ToArray(),
            Names(connection, "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name"));
        Assert.Equal(
            new[] { "ix_trip_events_trip", "ix_trips_start" },
            Names(connection, "SELECT name FROM sqlite_master WHERE type = 'index' AND name NOT LIKE 'sqlite_autoindex_%' ORDER BY name"));
        Assert.Equal(0, ScalarInt(connection, "SELECT count(*) FROM vehicle_drives")); // created now, empty in v1 (02 section 7.2)
    }

    [Theory]
    [MemberData(nameof(TableNames))]
    public void Columns_match_the_ddl(string table)
    {
        using var db = new TempDatabase();
        Bootstrap(db).Run();
        using var connection = RealmDb.OpenConnection(db.FilePath, pooling: false);

        var actual = new List<Col>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = $"PRAGMA table_info({table})";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                // cid, name, type, notnull, dflt_value, pk
                actual.Add(new Col(
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetInt32(3) == 1,
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.GetInt32(5)));
            }
        }

        Assert.Equal(Tables[table], actual.ToArray());
    }

    [Theory]
    [MemberData(nameof(TableNames))]
    public void Indexes_and_unique_constraints_match_the_ddl(string table)
    {
        using var db = new TempDatabase();
        Bootstrap(db).Run();
        using var connection = RealmDb.OpenConnection(db.FilePath, pooling: false);

        var listed = new List<(string Name, bool Unique, string Origin)>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = $"PRAGMA index_list({table})";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                // seq, name, unique, origin, partial
                listed.Add((reader.GetString(1), reader.GetInt32(2) == 1, reader.GetString(3)));
            }
        }

        var actual = new List<string>();
        foreach (var (name, unique, origin) in listed)
        {
            var columns = new List<string>();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = $"PRAGMA index_info('{name}')";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    // seqno, cid, name
                    columns.Add(reader.GetString(2));
                }
            }

            var kind = unique ? "unique" : "plain";
            var label = origin == "c" ? $"{origin} {name} {kind}" : $"{origin} {kind}";
            actual.Add($"{label} ({string.Join(", ", columns)})");
        }

        Assert.Equal(
            Indexes[table].Order(StringComparer.Ordinal).ToArray(),
            actual.Order(StringComparer.Ordinal).ToArray());
    }

    [Theory]
    [MemberData(nameof(TableNames))]
    public void Foreign_keys_match_the_ddl(string table)
    {
        using var db = new TempDatabase();
        Bootstrap(db).Run();
        using var connection = RealmDb.OpenConnection(db.FilePath, pooling: false);

        var actual = new List<string>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = $"PRAGMA foreign_key_list({table})";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                // id, seq, table, from, to, on_update, on_delete, match
                actual.Add($"{reader.GetString(3)} -> {reader.GetString(2)}.{reader.GetString(4)} (update {reader.GetString(5)}, delete {reader.GetString(6)})");
            }
        }

        Assert.Equal(
            ForeignKeys[table].Order(StringComparer.Ordinal).ToArray(),
            actual.Order(StringComparer.Ordinal).ToArray());
    }

    [Theory]
    [InlineData("fixes.source", "life360", true)]
    [InlineData("fixes.source", "companion", true)]
    [InlineData("fixes.source", "manual", false)]
    [InlineData("signals.kind", "screen", true)]
    [InlineData("signals.kind", "locked", true)]
    [InlineData("signals.kind", "activity", true)]
    [InlineData("signals.kind", "android_auto", true)]
    [InlineData("signals.kind", "wifi", false)]
    [InlineData("trips.distance_source", "gps", true)]
    [InlineData("trips.distance_source", "odometer", true)]
    [InlineData("trips.distance_source", "estimate", false)]
    [InlineData("trips.quality", "dense", true)]
    [InlineData("trips.quality", "coarse", true)]
    [InlineData("trips.quality", "sparse", false)]
    [InlineData("trips.ended_by", "stop", true)]
    [InlineData("trips.ended_by", "nofix", true)]
    [InlineData("trips.ended_by", "coarse", true)]
    [InlineData("trips.ended_by", "timeout", false)]
    [InlineData("trip_events.kind", "speeding", true)]
    [InlineData("trip_events.kind", "phone", true)]
    [InlineData("trip_events.kind", "braking", false)]
    [InlineData("roster.kind", "person", true)]
    [InlineData("roster.kind", "tracker", true)]
    [InlineData("roster.kind", "vehicle", false)]
    [InlineData("roster.grp", "people", true)]
    [InlineData("roster.grp", "vehicles", true)]
    [InlineData("roster.grp", "not_tracked", true)]
    [InlineData("roster.grp", "hidden", false)]
    public void Check_constraints_accept_only_the_listed_values(string column, string value, bool accepted)
    {
        using var db = new TempDatabase();
        Bootstrap(db).Run();
        using var connection = RealmDb.OpenConnection(db.FilePath, pooling: false);
        Exec(connection, "INSERT INTO members(id, first_seen_utc, last_configured_utc) VALUES ('m1', 1, 2)");
        Exec(connection, InsertTrip()); // the parent of the trip_events row

        var sql = column switch
        {
            "fixes.source" => $"INSERT INTO fixes(member_id, ts, source, lat, lon) VALUES ('m1', {NextTs()}, '{value}', 0, 0)",
            "signals.kind" => $"INSERT INTO signals(member_id, ts, kind, value) VALUES ('m1', {NextTs()}, '{value}', 'on')",
            "trips.distance_source" => InsertTrip(distanceSource: value),
            "trips.quality" => InsertTrip(quality: value),
            "trips.ended_by" => InsertTrip(endedBy: value),
            "trip_events.kind" => $"INSERT INTO trip_events(trip_id, kind, start_ts, end_ts) VALUES (1, '{value}', 0, 1)",
            "roster.kind" => $"INSERT INTO roster(entity_id, kind, grp, display_name, color, sort_order, source, first_seen, last_active) VALUES ('person.a', '{value}', 'people', 'A', '#E8BC4E', 0, 'Home Assistant', 1, 2)",
            "roster.grp" => $"INSERT INTO roster(entity_id, kind, grp, display_name, color, sort_order, source, first_seen, last_active) VALUES ('person.a', 'person', '{value}', 'A', '#E8BC4E', 0, 'Home Assistant', 1, 2)",
            _ => throw new ArgumentOutOfRangeException(nameof(column), column, "No such check constraint in 02 section 7.2"),
        };

        if (accepted)
        {
            Exec(connection, sql);
        }
        else
        {
            var error = Assert.Throws<SqliteException>(() => Exec(connection, sql));
            Assert.Equal(19, error.SqliteErrorCode);
            Assert.Contains("CHECK constraint failed", error.Message);
        }
    }

    [Fact]
    public void Foreign_keys_are_enforced_and_deleting_a_trip_deletes_its_events()
    {
        using var db = new TempDatabase();
        Bootstrap(db).Run();
        using var connection = RealmDb.OpenConnection(db.FilePath, pooling: false);

        var orphan = Assert.Throws<SqliteException>(
            () => Exec(connection, "INSERT INTO fixes(member_id, ts, source, lat, lon) VALUES ('nobody', 1, 'life360', 0, 0)"));
        Assert.Equal(19, orphan.SqliteErrorCode);
        Assert.Contains("FOREIGN KEY constraint failed", orphan.Message);

        Exec(connection, "INSERT INTO members(id, first_seen_utc, last_configured_utc) VALUES ('m1', 1, 2)");
        Exec(connection, InsertTrip());
        var tripId = ScalarInt(connection, "SELECT id FROM trips");
        Exec(connection, FormattableString.Invariant($"INSERT INTO trip_events(trip_id, kind, start_ts, end_ts) VALUES ({tripId}, 'speeding', 0, 1)"));
        Assert.Equal(1, ScalarInt(connection, "SELECT count(*) FROM trip_events"));

        Exec(connection, FormattableString.Invariant($"DELETE FROM trips WHERE id = {tripId}"));

        Assert.Equal(0, ScalarInt(connection, "SELECT count(*) FROM trip_events"));
    }

    [Fact]
    public void User_version_meta_schema_version_and_the_highest_script_number_agree()
    {
        using var db = new TempDatabase();
        var version = Bootstrap(db).Run();
        var highest = HighestScriptNumber();
        using var connection = RealmDb.OpenConnection(db.FilePath, pooling: false);

        Assert.Equal(highest, version);
        Assert.Equal(highest, ScalarInt(connection, "PRAGMA user_version"));
        Assert.Equal(highest.ToString(CultureInfo.InvariantCulture), ScalarString(connection, "SELECT value FROM meta WHERE key = 'schema_version'"));
    }

    [Fact]
    public void Running_the_bootstrap_again_is_a_no_op()
    {
        using var db = new TempDatabase();
        var first = new ListLogger<SchemaBootstrap>();
        Bootstrap(db, first).Run();
        int schemaCookie;
        using (var connection = RealmDb.OpenConnection(db.FilePath, pooling: false))
        {
            Exec(connection, "INSERT INTO members(id, first_seen_utc, last_configured_utc) VALUES ('m1', 1, 2)");
            Exec(connection, "UPDATE meta SET value = '1' WHERE key = 'clean_shutdown'"); // what a graceful stop writes
            schemaCookie = ScalarInt(connection, "PRAGMA schema_version"); // SQLite's own counter of schema changes
        }

        var second = new ListLogger<SchemaBootstrap>();
        var version = Bootstrap(db, second).Run();

        Assert.Equal(HighestScriptNumber(), version);
        Assert.Contains(first.Messages, m => m.Contains("Applied schema script"));
        Assert.DoesNotContain(second.Messages, m => m.Contains("Applied schema script"));
        using var after = RealmDb.OpenConnection(db.FilePath, pooling: false);
        Assert.Equal(schemaCookie, ScalarInt(after, "PRAGMA schema_version"));
        Assert.Equal(1, ScalarInt(after, "SELECT count(*) FROM members"));
        Assert.Equal(version.ToString(CultureInfo.InvariantCulture), ScalarString(after, "SELECT value FROM meta WHERE key = 'schema_version'"));
    }

    [Fact]
    public async Task The_hosted_service_applies_the_schema_on_start_and_marks_the_run_as_not_yet_stopped()
    {
        using var db = new TempDatabase();
        var bootstrap = Bootstrap(db);

        await bootstrap.StartAsync(CancellationToken.None);
        await bootstrap.StopAsync(CancellationToken.None);

        using var connection = RealmDb.OpenConnection(db.FilePath, pooling: false);
        Assert.Equal(HighestScriptNumber(), ScalarInt(connection, "PRAGMA user_version"));
        Assert.Equal("0", ScalarString(connection, "SELECT value FROM meta WHERE key = 'clean_shutdown'"));
    }

    [Fact]
    public void A_database_newer_than_the_app_refuses_to_start_and_is_left_alone()
    {
        using var db = new TempDatabase();
        Bootstrap(db).Run();
        var newer = HighestScriptNumber() + 1;
        using (var connection = RealmDb.OpenConnection(db.FilePath, pooling: false))
        {
            Exec(connection, FormattableString.Invariant($"PRAGMA user_version = {newer}"));
        }

        var logger = new ListLogger<SchemaBootstrap>();
        var error = Assert.Throws<InvalidOperationException>(() => Bootstrap(db, logger).Run());

        Assert.Contains("database is newer than this add-on", error.Message);
        Assert.Contains(logger.Messages, m => m.Contains("database is newer than this add-on"));
        Assert.Empty(db.QuarantinedNames());
        using var after = RealmDb.OpenConnection(db.FilePath, pooling: false);
        Assert.Equal(newer, ScalarInt(after, "PRAGMA user_version"));
    }

    [Fact]
    public void A_file_that_is_not_a_database_is_quarantined_and_replaced()
    {
        using var db = new TempDatabase();
        File.WriteAllText(db.FilePath, string.Concat(Enumerable.Repeat("this is not a database\n", 400)));
        var now = new DateTimeOffset(2026, 10, 1, 12, 30, 45, TimeSpan.Zero);
        var logger = new ListLogger<SchemaBootstrap>();

        var version = Bootstrap(db, logger, new FixedClock(now)).Run();

        Assert.Equal(new[] { "realm.db.corrupt-20261001T123045Z" }, db.QuarantinedNames());
        Assert.StartsWith("this is not a database", File.ReadAllText(db.FilePath + ".corrupt-20261001T123045Z"));
        Assert.Equal(HighestScriptNumber(), version);
        Assert.Contains(logger.Messages, m => m.Contains("Moved the damaged database"));
        using var connection = RealmDb.OpenConnection(db.FilePath, pooling: false);
        Assert.Equal(Tables.Count, ScalarInt(connection, "SELECT count(*) FROM sqlite_master WHERE type = 'table'"));
    }

    [Fact]
    public void A_damaged_database_found_by_quick_check_after_an_unclean_shutdown_is_quarantined()
    {
        using var db = new TempDatabase();
        Bootstrap(db).Run(); // leaves meta.clean_shutdown = '0', as a crash would
        DamageRootPageOf(db, "fixes");
        var now = new DateTimeOffset(2026, 10, 1, 13, 5, 9, TimeSpan.Zero);
        var logger = new ListLogger<SchemaBootstrap>();

        var version = Bootstrap(db, logger, new FixedClock(now)).Run();

        Assert.Contains(logger.Messages, m => m.Contains("clean_shutdown = 0"));
        Assert.Equal(new[] { "realm.db.corrupt-20261001T130509Z" }, db.QuarantinedNames());
        Assert.Equal(HighestScriptNumber(), version);
        using var connection = RealmDb.OpenConnection(db.FilePath, pooling: false);
        Assert.Equal("ok", ScalarString(connection, "PRAGMA quick_check"));
        Assert.Equal(Tables.Count, ScalarInt(connection, "SELECT count(*) FROM sqlite_master WHERE type = 'table'"));
    }

    [Fact]
    public void A_clean_shutdown_skips_quick_check()
    {
        using var db = new TempDatabase();
        Bootstrap(db).Run();
        using (var connection = RealmDb.OpenConnection(db.FilePath, pooling: false))
        {
            Exec(connection, "UPDATE meta SET value = '1' WHERE key = 'clean_shutdown'"); // what DbWriter writes on a graceful stop
        }

        DamageRootPageOf(db, "fixes");
        var logger = new ListLogger<SchemaBootstrap>();

        var version = Bootstrap(db, logger).Run();

        Assert.Equal(HighestScriptNumber(), version);
        Assert.Empty(db.QuarantinedNames());
        Assert.DoesNotContain(logger.Messages, m => m.Contains("quick_check"));
    }

    [Fact]
    public void The_engine_pragmas_of_02_7_1_are_applied_on_every_connection_open()
    {
        using var db = new TempDatabase();
        Bootstrap(db).Run();

        using (var first = RealmDb.OpenConnection(db.FilePath, pooling: false))
        {
            AssertEnginePragmas(first);
        }

        using (var second = RealmDb.OpenConnection(db.FilePath, pooling: false))
        {
            AssertEnginePragmas(second);
        }

        // journal_mode and auto_vacuum live in the file: a connection that applied nothing sees them too.
        using var plain = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = db.FilePath, Pooling = false }.ToString());
        plain.Open();
        Assert.Equal("wal", ScalarString(plain, "PRAGMA journal_mode"));
        Assert.Equal(2, ScalarInt(plain, "PRAGMA auto_vacuum"));
    }

    [Fact]
    public void Connections_that_EF_opens_through_RealmDb_get_the_same_pragmas()
    {
        using var db = new TempDatabase();
        Bootstrap(db).Run();
        var options = new DbContextOptionsBuilder<RealmDb>();
        RealmDb.Configure(options, db.FilePath);

        using var context = new RealmDb(options.Options);
        context.Database.OpenConnection();

        AssertEnginePragmas(context.Database.GetDbConnection());
    }

    [Fact]
    public void A_failing_script_rolls_back_its_tables_and_its_version()
    {
        using var db = new TempDatabase();
        using var connection = RealmDb.OpenConnection(db.FilePath, pooling: false);
        var scripts = new[]
        {
            new SchemaRunner.Script(1, "Sql/0001_one.sql", "CREATE TABLE meta (key TEXT PRIMARY KEY, value TEXT NOT NULL); CREATE TABLE one (x INTEGER);"),
            new SchemaRunner.Script(2, "Sql/0002_two.sql", "CREATE TABLE two (x INTEGER); INSERT INTO no_such_table VALUES (1);"),
        };

        var error = Assert.Throws<SqliteException>(() => SchemaRunner.Apply(connection, scripts, NullLogger.Instance));

        Assert.Contains("no_such_table", error.Message);
        Assert.Equal(1, ScalarInt(connection, "PRAGMA user_version"));
        Assert.Equal("1", ScalarString(connection, "SELECT value FROM meta WHERE key = 'schema_version'"));
        Assert.Equal(1, ScalarInt(connection, "SELECT count(*) FROM sqlite_master WHERE name = 'one'"));
        Assert.Equal(0, ScalarInt(connection, "SELECT count(*) FROM sqlite_master WHERE name = 'two'"));
        Exec(connection, "BEGIN IMMEDIATE"); // the connection is not left inside the failed transaction
        Exec(connection, "ROLLBACK");
    }

    [Fact]
    public void A_failure_after_the_version_write_still_restores_the_old_version()
    {
        using var db = new TempDatabase();
        using var connection = RealmDb.OpenConnection(db.FilePath, pooling: false);
        var scripts = new[]
        {
            new SchemaRunner.Script(1, "Sql/0001_one.sql", "CREATE TABLE meta (key TEXT PRIMARY KEY, value TEXT NOT NULL);"),
            // Drops meta, so the runner's own "schema_version" write fails after PRAGMA user_version = 2 already ran.
            new SchemaRunner.Script(2, "Sql/0002_two.sql", "CREATE TABLE two (x INTEGER); DROP TABLE meta;"),
        };

        var error = Assert.Throws<SqliteException>(() => SchemaRunner.Apply(connection, scripts, NullLogger.Instance));

        Assert.Contains("meta", error.Message);
        Assert.Equal(1, ScalarInt(connection, "PRAGMA user_version"));
        Assert.Equal("1", ScalarString(connection, "SELECT value FROM meta WHERE key = 'schema_version'"));
        Assert.Equal(0, ScalarInt(connection, "SELECT count(*) FROM sqlite_master WHERE name = 'two'"));
    }

    [Fact]
    public void Only_scripts_newer_than_the_stored_version_run_and_in_ascending_order()
    {
        using var db = new TempDatabase();
        using var connection = RealmDb.OpenConnection(db.FilePath, pooling: false);
        var one = new SchemaRunner.Script(1, "Sql/0001_one.sql", "CREATE TABLE meta (key TEXT PRIMARY KEY, value TEXT NOT NULL); CREATE TABLE one (x INTEGER);");
        var two = new SchemaRunner.Script(2, "Sql/0002_two.sql", "CREATE TABLE two (x INTEGER);");
        var three = new SchemaRunner.Script(3, "Sql/0003_three.sql", "CREATE TABLE three AS SELECT x FROM two;"); // fails unless script 2 ran first

        Assert.Equal(1, SchemaRunner.Apply(connection, new[] { one }, NullLogger.Instance));
        // Script 1 would fail with "table meta already exists" if it ran again; the list is deliberately out of order.
        Assert.Equal(3, SchemaRunner.Apply(connection, new[] { three, one, two }, NullLogger.Instance));

        Assert.Equal(3, ScalarInt(connection, "PRAGMA user_version"));
        Assert.Equal("3", ScalarString(connection, "SELECT value FROM meta WHERE key = 'schema_version'"));
        Assert.Equal(3, ScalarInt(connection, "SELECT count(*) FROM sqlite_master WHERE name IN ('one', 'two', 'three')"));
    }

    private static SchemaBootstrap Bootstrap(TempDatabase db, ListLogger<SchemaBootstrap>? logger = null, TimeProvider? time = null)
    {
        return new SchemaBootstrap(db.FilePath, logger ?? new ListLogger<SchemaBootstrap>(), time ?? new FixedClock(Epoch));
    }

    // The highest NNNN among the embedded Sql/NNNN_*.sql resources, parsed here and not through the runner.
    private static int HighestScriptNumber()
    {
        return typeof(SchemaRunner).Assembly.GetManifestResourceNames()
            .Select(name => Regex.Match(name, @"^Sql/(\d{4})_"))
            .Where(match => match.Success)
            .Select(match => int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture))
            .Max();
    }

    private static Col C(string name, string type, bool notNull = false, string? dflt = null, int pk = 0)
    {
        return new Col(name, type, notNull, dflt, pk);
    }

    private static long NextTs()
    {
        return Interlocked.Increment(ref s_nextTs);
    }

    private static string InsertTrip(string distanceSource = "gps", string quality = "dense", string endedBy = "stop")
    {
        return "INSERT INTO trips(member_id, start_ts, end_ts, duration_s, start_lat, start_lon, end_lat, end_lon, distance_m, distance_gps_m, "
            + "distance_source, quality, ended_by, source_mask, algo_version, derive_hash, created_ts) "
            + $"VALUES ('m1', {NextTs()}, 1, 1, 0, 0, 0, 0, 0, 0, '{distanceSource}', '{quality}', '{endedBy}', 'life360', 1, 'h', 1)";
    }

    // Overwrites the root page of a table with 0xFF bytes: the schema still parses, only a walk of that table's tree notices.
    private static void DamageRootPageOf(TempDatabase db, string table)
    {
        long rootPage;
        int pageSize;
        using (var connection = RealmDb.OpenConnection(db.FilePath, pooling: false))
        {
            rootPage = Convert.ToInt64(
                Scalar(connection, $"SELECT rootpage FROM sqlite_master WHERE type = 'table' AND name = '{table}'"),
                CultureInfo.InvariantCulture);
            pageSize = ScalarInt(connection, "PRAGMA page_size");
            Exec(connection, "PRAGMA wal_checkpoint(TRUNCATE)"); // every page is in the main file before it is damaged
        }

        var junk = new byte[pageSize];
        Array.Fill(junk, (byte)0xFF);
        using var stream = new FileStream(db.FilePath, FileMode.Open, FileAccess.Write);
        stream.Position = (rootPage - 1) * pageSize;
        stream.Write(junk, 0, junk.Length);
    }

    // The values 02 section 7.1 asks for, read back with the PRAGMA names (SQLite reports enumerations as numbers).
    private static void AssertEnginePragmas(DbConnection connection)
    {
        Assert.Equal("wal", ScalarString(connection, "PRAGMA journal_mode"));
        Assert.Equal(1, ScalarInt(connection, "PRAGMA synchronous")); // NORMAL
        Assert.Equal(1, ScalarInt(connection, "PRAGMA foreign_keys"));
        Assert.Equal(5000, ScalarInt(connection, "PRAGMA busy_timeout"));
        Assert.Equal(2, ScalarInt(connection, "PRAGMA temp_store")); // MEMORY
        Assert.Equal(-8192, ScalarInt(connection, "PRAGMA cache_size")); // 8 MB
        Assert.Equal(2, ScalarInt(connection, "PRAGMA auto_vacuum")); // INCREMENTAL
    }

    private static object? Scalar(DbConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    private static int ScalarInt(DbConnection connection, string sql)
    {
        return Convert.ToInt32(Scalar(connection, sql), CultureInfo.InvariantCulture);
    }

    private static string? ScalarString(DbConnection connection, string sql)
    {
        return Convert.ToString(Scalar(connection, sql), CultureInfo.InvariantCulture);
    }

    private static string[] Names(DbConnection connection, string sql)
    {
        var names = new List<string>();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            names.Add(reader.GetString(0));
        }

        return names.ToArray();
    }

    private static void Exec(DbConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
