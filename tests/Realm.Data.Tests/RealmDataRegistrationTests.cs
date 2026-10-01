using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Realm.Domain;
using Realm.Infrastructure.Data;
using Xunit;

namespace Realm.Data.Tests;

// AddRealmData (D58): the host gets the schema step first, then the writer, and the two ports. The host starts hosted services in registration order and stops them in reverse.
public class RealmDataRegistrationTests
{
    [Fact]
    public async Task AddRealmData_registers_the_schema_step_then_the_writer_and_both_ports()
    {
        using var db = new TempDatabase();
        await using var provider = new ServiceCollection()
            .AddLogging()
            .AddSingleton<TimeProvider>(new FakeClock(TestData.Start))
            .AddRealmData(db.FilePath)
            .BuildServiceProvider();

        var hosted = provider.GetServices<IHostedService>().ToArray();

        Assert.Equal(2, hosted.Length);
        Assert.IsType<SchemaBootstrap>(hosted[0]);
        Assert.IsType<DbWriter>(hosted[1]);
        Assert.Same(hosted[1], provider.GetRequiredService<IRealmWriter>());
        Assert.IsType<SqliteRealmQueries>(provider.GetRequiredService<IRealmQueries>());
    }

    [Fact]
    public async Task The_registered_services_create_the_database_store_rows_and_stop_cleanly()
    {
        using var db = new TempDatabase();
        await using var provider = new ServiceCollection()
            .AddLogging()
            .AddSingleton<TimeProvider>(new FakeClock(TestData.Start))
            .AddRealmData(db.FilePath)
            .BuildServiceProvider();
        var hosted = provider.GetServices<IHostedService>().ToArray();

        foreach (var service in hosted)
        {
            await service.StartAsync(CancellationToken.None);
        }

        var writer = provider.GetRequiredService<IRealmWriter>();
        Assert.True(writer.EnqueueFix("king", TestData.Fix(0)));
        await writer.FlushAsync();
        var stored = await provider.GetRequiredService<IRealmQueries>().GetFixesAsync("king", TestData.Start, TestData.Start.AddMinutes(1));
        Assert.Single(stored);
        Assert.Equal("0", TestSql.Text(db.FilePath, "SELECT value FROM meta WHERE key = 'clean_shutdown'"));

        foreach (var service in Enumerable.Reverse(hosted))
        {
            await service.StopAsync(CancellationToken.None);
        }

        Assert.Equal("1", TestSql.Text(db.FilePath, "SELECT value FROM meta WHERE key = 'clean_shutdown'"));
        Assert.Equal("2026-10-01T12:00:00Z", TestSql.Text(db.FilePath, "SELECT value FROM meta WHERE key = 'created_utc'"));
    }
}
