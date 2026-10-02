using System.Data.Common;

namespace Realm.Infrastructure.Data;

/// <summary>
/// Conversions between the .NET values of the domain and what the SQLite columns of 02 section 7.2 hold: every instant is a Unix-millisecond
/// integer, a boolean is 0 or 1, and "unknown" is SQL NULL. Writing the instant as an integer here (never a <see cref="DateTimeOffset"/>) keeps range scans on the index.
/// </summary>
internal static class SqlValues
{
    public static object Text(string? value)
    {
        return value is null ? DBNull.Value : value;
    }

    public static object Real(double? value)
    {
        return value is { } number ? number : DBNull.Value;
    }

    public static object Integer(long? value)
    {
        return value is { } number ? number : DBNull.Value;
    }

    public static object Flag(bool? value)
    {
        return value is { } flag ? (flag ? 1L : 0L) : DBNull.Value;
    }

    public static object Millis(DateTimeOffset value)
    {
        return value.ToUnixTimeMilliseconds();
    }

    public static object Millis(DateTimeOffset? value)
    {
        return value is { } instant ? instant.ToUnixTimeMilliseconds() : DBNull.Value;
    }

    public static DateTimeOffset FromMillis(long value)
    {
        return DateTimeOffset.FromUnixTimeMilliseconds(value);
    }

    public static double? NullableReal(DbDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : reader.GetDouble(ordinal);
    }

    public static int? NullableInteger(DbDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
    }

    public static bool? NullableFlag(DbDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal) != 0;
    }

    public static string? NullableText(DbDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    public static DateTimeOffset? NullableMillis(DbDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : FromMillis(reader.GetInt64(ordinal));
    }
}
