using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Realm.Infrastructure.Data;
using Xunit;

namespace Realm.Data.Tests;

/// <summary>
/// A bootstrapped temporary database with a <see cref="DbWriter"/> and <see cref="SqliteRealmQueries"/> over the pooled EF factory of production, on a
/// fake clock. Waiting for the writer's own thread uses its <see cref="DbWriter.RowsCommitted"/> event and short polls; the clock moves only when a test says so.
/// </summary>
internal sealed class WriterRig : IAsyncDisposable
{
    private readonly ServiceProvider _provider;
    private readonly Channel<int> _commits = Channel.CreateUnbounded<int>();
    private bool _stopped;

    private WriterRig()
    {
        Db = new TempDatabase();
        Time = new FakeClock(TestData.Start);
        new SchemaBootstrap(Db.FilePath, new ListLogger<SchemaBootstrap>(), Time).Run();
        _provider = new ServiceCollection()
            .AddPooledDbContextFactory<RealmDb>(options => RealmDb.Configure(options, Db.FilePath))
            .BuildServiceProvider();
        var factory = _provider.GetRequiredService<IDbContextFactory<RealmDb>>();
        Log = new ListLogger<DbWriter>();
        Writer = new DbWriter(factory, Time, Log);
        Writer.RowsCommitted += rows => _commits.Writer.TryWrite(rows);
        Queries = new SqliteRealmQueries(factory);
    }

    public TempDatabase Db { get; }

    public FakeClock Time { get; }

    public ListLogger<DbWriter> Log { get; }

    public DbWriter Writer { get; }

    public SqliteRealmQueries Queries { get; }

    public string FilePath => Db.FilePath;

    /// <summary>A rig whose writer has not been started: rows can be queued, nothing is consumed.</summary>
    public static WriterRig Create()
    {
        return new WriterRig();
    }

    /// <summary>A rig with the writer running.</summary>
    public static async Task<WriterRig> StartAsync()
    {
        var rig = new WriterRig();
        await rig.Writer.StartAsync(CancellationToken.None);
        return rig;
    }

    /// <summary>The row count of the next committed batch that has not been awaited yet.</summary>
    public async Task<int> NextCommitAsync()
    {
        return await _commits.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(30));
    }

    public long RowCount(string table)
    {
        return TestSql.Long(FilePath, "SELECT count(*) FROM " + table);
    }

    public async Task StopAsync()
    {
        _stopped = true;
        await Writer.StopAsync(CancellationToken.None);
    }

    /// <summary>Polls a condition that another thread makes true; the clock is not touched.</summary>
    public static async Task EventuallyAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 3000 && !condition(); attempt++)
        {
            await Task.Delay(10);
        }

        Assert.True(condition());
    }

    public async ValueTask DisposeAsync()
    {
        if (!_stopped)
        {
            await Writer.StopAsync(CancellationToken.None);
        }

        Writer.Dispose();
        await _provider.DisposeAsync();
        Db.Dispose();
    }
}
