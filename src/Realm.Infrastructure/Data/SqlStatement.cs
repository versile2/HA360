using Microsoft.Data.Sqlite;

namespace Realm.Infrastructure.Data;

/// <summary>
/// One parameterised statement prepared once and run for many rows inside a transaction. The parameter names are given once; <see cref="Run"/> sets
/// the values by position (use <see cref="SqlValues"/> for them) and executes.
/// </summary>
internal sealed class SqlStatement : IDisposable
{
    private readonly SqliteCommand _command;

    public SqlStatement(SqliteConnection connection, SqliteTransaction? transaction, string sql, params string[] parameterNames)
    {
        _command = connection.CreateCommand();
        _command.Transaction = transaction;
        _command.CommandText = sql;
        foreach (var name in parameterNames)
        {
            _command.Parameters.AddWithValue(name, DBNull.Value);
        }
    }

    /// <summary>Executes with these values and returns the number of rows the statement changed (0 when <c>OR IGNORE</c> skipped it).</summary>
    public int Run(params object[] values)
    {
        for (var i = 0; i < values.Length; i++)
        {
            _command.Parameters[i].Value = values[i];
        }

        return _command.ExecuteNonQuery();
    }

    public void Dispose()
    {
        _command.Dispose();
    }
}
