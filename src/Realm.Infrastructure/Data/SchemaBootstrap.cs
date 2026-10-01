using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Realm.Infrastructure.Data;

/// <summary>
/// The first hosted service (03 section 2.4): before any <see cref="RealmDb"/> exists it applies the embedded schema scripts
/// through <see cref="SchemaRunner"/>, runs <c>PRAGMA quick_check</c> when the previous run did not stop cleanly
/// (<c>meta.clean_shutdown = '0'</c>, 02 section 7.8), quarantines a damaged file as <c>realm.db.corrupt-&lt;utc&gt;</c> and starts
/// again on a new one, and marks this run as not yet cleanly stopped. Any other failure aborts startup: the exception
/// leaves <see cref="StartAsync"/>. The path is the resolved <c>Realm:Db</c> value, so the host registers this class with a factory.
/// </summary>
public sealed class SchemaBootstrap : IHostedService
{
    // SQLite primary result codes: the file is damaged (SQLITE_CORRUPT) or is not a database at all (SQLITE_NOTADB).
    private const int SqliteCorrupt = 11;
    private const int SqliteNotADatabase = 26;

    private readonly string _path;
    private readonly ILogger<SchemaBootstrap> _logger;
    private readonly TimeProvider _time;

    public SchemaBootstrap(string databasePath, ILogger<SchemaBootstrap> logger, TimeProvider time)
    {
        _path = databasePath;
        _logger = logger;
        _time = time;
    }

    /// <summary>Runs the bootstrap synchronously (it is fast) so nothing touches the database before it finishes.</summary>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        Run();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <summary>Prepares the database file and returns its schema version.</summary>
    public int Run()
    {
        var version = TryInitialize();
        if (version is null)
        {
            Quarantine();
            version = TryInitialize()
                ?? throw new InvalidOperationException("A newly created database file was reported damaged");
        }

        return version.Value;
    }

    // Null means the file is damaged and must be quarantined.
    private int? TryInitialize()
    {
        try
        {
            using var connection = RealmDb.OpenConnection(_path, pooling: false);
            if (SchemaRunner.ReadUserVersion(connection) > 0 && WasUncleanShutdown(connection))
            {
                _logger.LogWarning("clean_shutdown = 0 found at start; running quick_check");
                if (!QuickCheckPasses(connection))
                {
                    _logger.LogError("quick_check found damage in the database file");
                    return null;
                }
            }

            var version = SchemaRunner.Apply(connection, _logger);
            SchemaRunner.Execute(connection, "INSERT OR REPLACE INTO meta(key, value) VALUES ('clean_shutdown', '0')");
            return version;
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode is SqliteCorrupt or SqliteNotADatabase)
        {
            _logger.LogError(ex, "The database file is damaged (SQLite error {Code})", ex.SqliteErrorCode);
            return null;
        }
    }

    private static bool WasUncleanShutdown(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM meta WHERE key = 'clean_shutdown'";
        return string.Equals(command.ExecuteScalar() as string, "0", StringComparison.Ordinal);
    }

    private static bool QuickCheckPasses(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA quick_check";
        using var reader = command.ExecuteReader();
        return reader.Read() && string.Equals(reader.GetString(0), "ok", StringComparison.Ordinal) && !reader.Read();
    }

    // Renames the damaged file (and its WAL and shared-memory companions, which would otherwise be replayed into the new file).
    private void Quarantine()
    {
        var stamp = _time.GetUtcNow().ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        var target = $"{_path}.corrupt-{stamp}";
        File.Move(_path, target);
        foreach (var suffix in new[] { "-wal", "-shm" })
        {
            if (File.Exists(_path + suffix))
            {
                File.Move(_path + suffix, target + suffix);
            }
        }

        _logger.LogWarning("Moved the damaged database to {Target}; a new one is created", target);
    }
}
