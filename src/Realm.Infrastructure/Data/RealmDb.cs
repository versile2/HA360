using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Realm.Infrastructure.Data;

/// <summary>
/// The EF Core context over the SQLite file (02 section 7.1). EF is only the object mapper: the schema is created by the
/// embedded scripts (<see cref="SchemaRunner"/>), never by <c>Migrate()</c> or <c>EnsureCreated()</c> (D31, D38, R-070).
/// This class also owns how every connection to the file is opened, so the engine settings of 02 section 7.1 are applied in one place.
/// </summary>
public sealed class RealmDb : DbContext
{
    // 02 section 7.1. auto_vacuum comes first on purpose: on a new file, journal_mode = WAL writes page 1 and locks
    // auto_vacuum at NONE for good, while auto_vacuum before WAL gives INCREMENTAL (verified against SQLite 3.45).
    // On an existing file it changes nothing and raises no error.
    private const string Pragmas =
        "PRAGMA auto_vacuum = INCREMENTAL; " +
        "PRAGMA journal_mode = WAL; " +
        "PRAGMA synchronous = NORMAL; " +
        "PRAGMA foreign_keys = ON; " +
        "PRAGMA busy_timeout = 5000; " +
        "PRAGMA temp_store = MEMORY; " +
        "PRAGMA cache_size = -8192;";

    public RealmDb(DbContextOptions<RealmDb> options)
        : base(options)
    {
    }

    /// <summary>
    /// Opens a <see cref="SqliteConnection"/> to the database file with the PRAGMAs of 02 section 7.1 applied.
    /// The caller disposes it. <paramref name="pooling"/> false makes Dispose really close the file, which the schema
    /// bootstrap needs so the WAL is checkpointed and a damaged file can be renamed.
    /// </summary>
    public static SqliteConnection OpenConnection(string path, bool pooling = true)
    {
        var connection = new SqliteConnection(ConnectionString(path, pooling));
        try
        {
            connection.Open();
            ApplyPragmas(connection);
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Points <paramref name="options"/> at the database file and applies the PRAGMAs of 02 section 7.1 every time EF
    /// opens a connection (for example <c>AddPooledDbContextFactory&lt;RealmDb&gt;(o =&gt; RealmDb.Configure(o, path))</c>).
    /// </summary>
    public static DbContextOptionsBuilder Configure(DbContextOptionsBuilder options, string path)
    {
        return options
            .UseSqlite(ConnectionString(path, pooling: true))
            .AddInterceptors(new PragmaInterceptor());
    }

    private static string ConnectionString(string path, bool pooling)
    {
        return new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Cache = SqliteCacheMode.Private,
            Pooling = pooling,
        }.ToString();
    }

    private static void ApplyPragmas(DbConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = Pragmas;
        command.ExecuteNonQuery();
    }

    private sealed class PragmaInterceptor : DbConnectionInterceptor
    {
        public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
        {
            ApplyPragmas(connection);
        }

        public override Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            ApplyPragmas(connection);
            return Task.CompletedTask;
        }
    }
}
