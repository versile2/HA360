using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Realm.Infrastructure.Data;

/// <summary>
/// Applies the embedded schema scripts <c>Sql/NNNN_*.sql</c> to an open database (02 section 7.1, D31, D38, R-070).
/// <c>PRAGMA user_version</c> is the single source of truth and <c>meta.schema_version</c> mirrors it. Each script runs in
/// its own <c>BEGIN IMMEDIATE</c> transaction together with the two version writes (SQLite makes <c>user_version</c>
/// transactional: a rollback restores the old value), so a failing script changes nothing. A database whose version is
/// higher than the newest script belongs to a newer add-on and is refused.
/// </summary>
public static class SchemaRunner
{
    private static readonly Regex ScriptName = new(@"^Sql/(\d{4})_.+\.sql$", RegexOptions.CultureInvariant);

    /// <summary>One schema script: its number NNNN, its logical resource name and its SQL text.</summary>
    public sealed record Script(int Version, string Name, string Sql);

    /// <summary>The scripts embedded in this assembly, ascending by number. Two scripts with one number are an error.</summary>
    public static IReadOnlyList<Script> LoadEmbeddedScripts()
    {
        var assembly = typeof(SchemaRunner).Assembly;
        var scripts = new List<Script>();
        foreach (var name in assembly.GetManifestResourceNames())
        {
            var match = ScriptName.Match(name);
            if (!match.Success)
            {
                continue;
            }

            using var stream = assembly.GetManifestResourceStream(name)
                ?? throw new InvalidOperationException($"Embedded schema script {name} could not be opened");
            using var reader = new StreamReader(stream);
            var version = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            scripts.Add(new Script(version, name, reader.ReadToEnd()));
        }

        scripts.Sort((left, right) => left.Version.CompareTo(right.Version));
        for (var i = 1; i < scripts.Count; i++)
        {
            if (scripts[i].Version == scripts[i - 1].Version)
            {
                throw new InvalidOperationException($"Schema scripts {scripts[i - 1].Name} and {scripts[i].Name} have the same number");
            }
        }

        return scripts;
    }

    /// <summary>Applies the embedded scripts newer than the database's <c>user_version</c>; returns the resulting version.</summary>
    public static int Apply(SqliteConnection connection, ILogger logger)
    {
        return Apply(connection, LoadEmbeddedScripts(), logger);
    }

    /// <summary>
    /// Applies the given scripts newer than the database's <c>user_version</c>, in ascending order, one transaction each;
    /// returns the resulting version. Throws <see cref="InvalidOperationException"/> when the database is newer than every script.
    /// </summary>
    public static int Apply(SqliteConnection connection, IReadOnlyList<Script> scripts, ILogger logger)
    {
        var current = ReadUserVersion(connection);
        var latest = 0;
        foreach (var script in scripts)
        {
            latest = Math.Max(latest, script.Version);
        }

        if (current > latest)
        {
            logger.LogError("database is newer than this add-on: schema version {Current}, this build knows {Latest}", current, latest);
            throw new InvalidOperationException(
                FormattableString.Invariant($"database is newer than this add-on: schema version {current}, this build knows {latest}"));
        }

        foreach (var script in scripts.Where(s => s.Version > current).OrderBy(s => s.Version))
        {
            ApplyScript(connection, script);
            logger.LogInformation("Applied schema script {Script}; schema version is now {Version}", script.Name, script.Version);
        }

        return Math.Max(current, latest);
    }

    internal static int ReadUserVersion(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version";
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    internal static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static void ApplyScript(SqliteConnection connection, Script script)
    {
        Execute(connection, "BEGIN IMMEDIATE");
        try
        {
            Execute(connection, script.Sql);
            Execute(connection, FormattableString.Invariant($"PRAGMA user_version = {script.Version}"));
            Execute(connection, FormattableString.Invariant($"INSERT OR REPLACE INTO meta(key, value) VALUES ('schema_version', '{script.Version}')"));
            Execute(connection, "COMMIT");
        }
        catch
        {
            try
            {
                Execute(connection, "ROLLBACK");
            }
            catch (SqliteException)
            {
                // SQLite already rolled the transaction back (for example SQLITE_FULL); the original error is rethrown below.
            }

            throw;
        }
    }
}
