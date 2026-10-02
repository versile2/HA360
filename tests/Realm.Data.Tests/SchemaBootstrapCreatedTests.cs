using Realm.Infrastructure.Data;
using Xunit;

namespace Realm.Data.Tests;

// D58: SchemaBootstrap writes meta.created_utc (ISO 8601 UTC) when it creates the database, and only then.
public class SchemaBootstrapCreatedTests
{
    private const string CreatedSql = "SELECT value FROM meta WHERE key = 'created_utc'";

    [Fact]
    public void A_new_database_gets_created_utc_once()
    {
        using var db = new TempDatabase();
        var time = new FakeClock(new DateTimeOffset(2026, 10, 1, 12, 30, 5, TimeSpan.Zero));
        new SchemaBootstrap(db.FilePath, new ListLogger<SchemaBootstrap>(), time).Run();

        Assert.Equal("2026-10-01T12:30:05Z", TestSql.Text(db.FilePath, CreatedSql));

        time.Advance(TimeSpan.FromDays(3));
        new SchemaBootstrap(db.FilePath, new ListLogger<SchemaBootstrap>(), time).Run(); // the next start

        Assert.Equal("2026-10-01T12:30:05Z", TestSql.Text(db.FilePath, CreatedSql));
    }

    [Fact]
    public void An_existing_database_without_created_utc_is_not_given_one()
    {
        using var db = new TempDatabase();
        var time = new FakeClock(TestData.Start);
        new SchemaBootstrap(db.FilePath, new ListLogger<SchemaBootstrap>(), time).Run();
        TestSql.Exec(db.FilePath, "DELETE FROM meta WHERE key = 'created_utc'");

        new SchemaBootstrap(db.FilePath, new ListLogger<SchemaBootstrap>(), time).Run();

        Assert.True(TestSql.IsNull(db.FilePath, CreatedSql));
    }
}
