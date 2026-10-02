using System.Globalization;
using Microsoft.Data.Sqlite;
using Realm.Infrastructure.Data;

namespace Realm.Data.Tests;

/// <summary>Reads (and, for a failure test, changes) a test database through its own short-lived connection, the way a second process would.</summary>
internal static class TestSql
{
    public static long Long(string path, string sql)
    {
        using var connection = RealmDb.OpenConnection(path, pooling: false);
        return Convert.ToInt64(Scalar(connection, sql), CultureInfo.InvariantCulture);
    }

    public static string? Text(string path, string sql)
    {
        using var connection = RealmDb.OpenConnection(path, pooling: false);
        var value = Scalar(connection, sql);
        return value is null or DBNull ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
    }

    public static bool IsNull(string path, string sql)
    {
        using var connection = RealmDb.OpenConnection(path, pooling: false);
        return Scalar(connection, sql) is null or DBNull;
    }

    public static void Exec(string path, string sql)
    {
        using var connection = RealmDb.OpenConnection(path, pooling: false);
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static object? Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }
}
