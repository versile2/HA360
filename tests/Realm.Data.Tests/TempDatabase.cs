using Microsoft.Data.Sqlite;

namespace Realm.Data.Tests;

/// <summary>One temporary folder per test holding <c>realm.db</c> (and whatever SQLite or the quarantine puts beside it); deleted on Dispose.</summary>
internal sealed class TempDatabase : IDisposable
{
    private readonly string _folder;

    public TempDatabase()
    {
        _folder = Path.Combine(Path.GetTempPath(), "realm-data-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
        FilePath = Path.Combine(_folder, "realm.db");
    }

    /// <summary>The database file; it does not exist until something opens it.</summary>
    public string FilePath { get; }

    /// <summary>The names (not paths) of the files in the folder that start with <c>realm.db.corrupt-</c>: the quarantined copies.</summary>
    public string[] QuarantinedNames()
    {
        return Directory.GetFiles(_folder, "realm.db.corrupt-*").Select(file => Path.GetFileName(file)).Order(StringComparer.Ordinal).ToArray();
    }

    public void Dispose()
    {
        // Pooled connections (the EF path) keep the file open until the pool is cleared.
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // Best effort: a leftover temp folder must not fail the test that already finished.
        }
    }
}
