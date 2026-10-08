using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Realm.Domain;

namespace Realm.Infrastructure.Data;

/// <summary>
/// The read side of the live data layer over the SQLite file (<see cref="IRealmQueries"/>). Each call opens its own short-lived connection from the
/// pooled factory, so a read never waits for <see cref="DbWriter"/> (WAL, 02 section 7.1). The queries are plain SQL over the tables of 02 section 7.2;
/// every time is stored and compared as Unix milliseconds, so the range scans use the indexes.
/// </summary>
public sealed class SqliteRealmQueries : IRealmQueries
{
    // The column order of ReadFix.
    private const string FixColumns = "ts, source, lat, lon, acc_m, speed_mps, heading_deg, alt_m, battery_pct, charging, driving, address";

    private readonly IDbContextFactory<RealmDb> _factory;

    public SqliteRealmQueries(IDbContextFactory<RealmDb> factory)
    {
        _factory = factory;
    }

    public Task<IReadOnlyList<StatsTrip>> GetTripsAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, string? memberId, CancellationToken cancellationToken = default)
    {
        return QueryAsync(
            "SELECT member_id, start_ts, end_ts, distance_m, quality, distance_source, top_speed_mps, top_speed_ts, top_speed_street, "
            + "speeding_count, phone_count, start_place_id, end_place_id, start_street, end_street, start_lat, start_lon, end_lat, end_lon "
            + "FROM trips WHERE start_ts >= @from AND start_ts < @to AND (@member IS NULL OR member_id = @member) "
            + "ORDER BY start_ts DESC, id DESC",
            ReadTrip,
            cancellationToken,
            ("@from", SqlValues.Millis(fromUtc)),
            ("@to", SqlValues.Millis(toUtc)),
            ("@member", SqlValues.Text(memberId)));
    }

    public async Task<IReadOnlyDictionary<string, DateTimeOffset>> GetRecordingStartsAsync(CancellationToken cancellationToken = default)
    {
        var rows = await QueryAsync(
            "SELECT id, recording_start FROM members WHERE recording_start IS NOT NULL",
            reader => KeyValuePair.Create(reader.GetString(0), SqlValues.FromMillis(reader.GetInt64(1))),
            cancellationToken);
        return rows.ToDictionary(row => row.Key, row => row.Value, StringComparer.Ordinal);
    }

    public Task<IReadOnlyList<RawFix>> GetFixesAsync(string memberId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default)
    {
        return QueryAsync(
            $"SELECT {FixColumns} FROM fixes WHERE member_id = @member AND ts >= @from AND ts < @to ORDER BY ts, source",
            ReadFix,
            cancellationToken,
            ("@member", memberId),
            ("@from", SqlValues.Millis(fromUtc)),
            ("@to", SqlValues.Millis(toUtc)));
    }

    public async Task<RawFix?> GetLatestFixAsync(string memberId, FixSource source, CancellationToken cancellationToken = default)
    {
        var rows = await QueryAsync(
            $"SELECT {FixColumns} FROM fixes WHERE member_id = @member AND source = @source ORDER BY ts DESC LIMIT 1",
            ReadFix,
            cancellationToken,
            ("@member", memberId),
            ("@source", FixSourceText.ToText(source)));
        return rows.Count == 0 ? null : rows[0];
    }

    public Task<IReadOnlyList<DateTimeOffset>> GetFixTimesAsync(string memberId, FixSource source, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default)
    {
        return QueryAsync(
            "SELECT ts FROM fixes WHERE member_id = @member AND source = @source AND ts >= @from AND ts < @to ORDER BY ts",
            reader => SqlValues.FromMillis(reader.GetInt64(0)),
            cancellationToken,
            ("@member", memberId),
            ("@source", FixSourceText.ToText(source)),
            ("@from", SqlValues.Millis(fromUtc)),
            ("@to", SqlValues.Millis(toUtc)));
    }

    public async Task<RawFix?> GetLatestAddressFixAsync(string memberId, DateTimeOffset atOrBeforeUtc, CancellationToken cancellationToken = default)
    {
        var rows = await QueryAsync(
            $"SELECT {FixColumns} FROM fixes WHERE member_id = @member AND ts <= @at AND address IS NOT NULL ORDER BY ts DESC LIMIT 1",
            ReadFix,
            cancellationToken,
            ("@member", memberId),
            ("@at", SqlValues.Millis(atOrBeforeUtc)));
        return rows.Count == 0 ? null : rows[0];
    }

    public Task<IReadOnlyList<PhoneSignal>> GetPhoneSignalsAsync(string memberId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default)
    {
        return QueryAsync(
            "SELECT ts, kind, value FROM signals WHERE member_id = @member AND ts >= @from AND ts < @to AND kind <> 'activity' ORDER BY ts, id",
            ReadSignal,
            cancellationToken,
            ("@member", memberId),
            ("@from", SqlValues.Millis(fromUtc)),
            ("@to", SqlValues.Millis(toUtc)));
    }

    public Task<IReadOnlyList<RosterEntry>> GetRosterAsync(CancellationToken cancellationToken = default)
    {
        return QueryAsync(
            "SELECT entity_id, kind, grp, display_name, lore_title, color, sort_order, source, first_seen, last_active, auto_moved_at "
            + "FROM roster ORDER BY CASE grp WHEN 'people' THEN 0 WHEN 'vehicles' THEN 1 ELSE 2 END, sort_order, entity_id",
            ReadRoster,
            cancellationToken);
    }

    private static RosterEntry ReadRoster(DbDataReader reader)
    {
        return new RosterEntry(
            EntityId: reader.GetString(0),
            Kind: RosterText.ParseKind(reader.GetString(1)),
            Group: RosterText.ParseGroup(reader.GetString(2)),
            DisplayName: reader.GetString(3),
            LoreTitle: SqlValues.NullableText(reader, 4),
            Color: reader.GetString(5),
            SortOrder: reader.GetInt32(6),
            Source: reader.GetString(7),
            FirstSeenUtc: SqlValues.FromMillis(reader.GetInt64(8)),
            LastActiveUtc: SqlValues.FromMillis(reader.GetInt64(9)),
            AutoMovedUtc: SqlValues.NullableMillis(reader, 10));
    }

    private static StatsTrip ReadTrip(DbDataReader reader)
    {
        return new StatsTrip(
            MemberId: reader.GetString(0),
            StartUtc: SqlValues.FromMillis(reader.GetInt64(1)),
            EndUtc: SqlValues.FromMillis(reader.GetInt64(2)),
            Meters: reader.GetDouble(3),
            Quality: string.Equals(reader.GetString(4), "coarse", StringComparison.Ordinal) ? TripQuality.Coarse : TripQuality.Dense,
            DistanceBasis: string.Equals(reader.GetString(5), "odometer", StringComparison.Ordinal) ? DistanceBasis.Odometer : DistanceBasis.Gps,
            TopSpeedMps: SqlValues.NullableReal(reader, 6),
            TopSpeedAtUtc: SqlValues.NullableMillis(reader, 7),
            TopSpeedStreet: SqlValues.NullableText(reader, 8),
            SpeedingCount: SqlValues.NullableInteger(reader, 9),
            PhoneCount: SqlValues.NullableInteger(reader, 10),
            StartPlaceId: SqlValues.NullableText(reader, 11),
            EndPlaceId: SqlValues.NullableText(reader, 12),
            StartStreet: SqlValues.NullableText(reader, 13),
            EndStreet: SqlValues.NullableText(reader, 14),
            StartLat: SqlValues.NullableReal(reader, 15),
            StartLon: SqlValues.NullableReal(reader, 16),
            EndLat: SqlValues.NullableReal(reader, 17),
            EndLon: SqlValues.NullableReal(reader, 18));
    }

    // The table keeps no entity id and no battery reading time (02 section 7.2), so those two stay empty.
    private static RawFix ReadFix(DbDataReader reader)
    {
        return new RawFix(
            EntityId: string.Empty,
            Source: FixSourceText.Parse(reader.GetString(1)),
            Ts: SqlValues.FromMillis(reader.GetInt64(0)),
            Lat: reader.GetDouble(2),
            Lon: reader.GetDouble(3),
            AccuracyM: SqlValues.NullableReal(reader, 4),
            SpeedMps: SqlValues.NullableReal(reader, 5),
            HeadingDeg: SqlValues.NullableReal(reader, 6),
            AltitudeM: SqlValues.NullableReal(reader, 7),
            BatteryPct: SqlValues.NullableInteger(reader, 8),
            Charging: SqlValues.NullableFlag(reader, 9),
            BatteryAsOfUtc: null,
            Driving: SqlValues.NullableFlag(reader, 10),
            Address: SqlValues.NullableText(reader, 11));
    }

    private static PhoneSignal ReadSignal(DbDataReader reader)
    {
        var kind = reader.GetString(1) switch
        {
            "screen" => PhoneSignalKind.Screen,
            "locked" => PhoneSignalKind.Locked,
            _ => PhoneSignalKind.AndroidAuto,
        };
        bool? isOn = reader.GetString(2) switch
        {
            "on" => true,
            "off" => false,
            _ => null,
        };
        return new PhoneSignal(SqlValues.FromMillis(reader.GetInt64(0)), kind, isOn);
    }

    private async Task<IReadOnlyList<T>> QueryAsync<T>(
        string sql,
        Func<DbDataReader, T> map,
        CancellationToken cancellationToken,
        params (string Name, object Value)[] parameters)
    {
        await using var session = await DbSession.OpenAsync(_factory, cancellationToken);
        await using var command = session.Connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<T>();
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(map(reader));
        }

        return rows;
    }
}
