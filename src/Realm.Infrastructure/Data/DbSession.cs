using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Realm.Infrastructure.Data;

/// <summary>
/// One short-lived lease of a database connection: a context from the pooled factory, whose connection is opened through EF so the PRAGMAs of
/// 02 section 7.1 run, handed out as a plain <see cref="SqliteConnection"/>. EF is only the connection source here: 02 section 7.3 asks for
/// <c>INSERT OR IGNORE</c> and <c>ON CONFLICT DO UPDATE</c>, which are written as SQL, and the model has no entities (D31).
/// </summary>
internal sealed class DbSession : IAsyncDisposable
{
    private readonly RealmDb _context;

    private DbSession(RealmDb context, SqliteConnection connection)
    {
        _context = context;
        Connection = connection;
    }

    public SqliteConnection Connection { get; }

    public static async Task<DbSession> OpenAsync(IDbContextFactory<RealmDb> factory, CancellationToken cancellationToken)
    {
        var context = await factory.CreateDbContextAsync(cancellationToken);
        try
        {
            await context.Database.OpenConnectionAsync(cancellationToken);
            return new DbSession(context, (SqliteConnection)context.Database.GetDbConnection());
        }
        catch
        {
            await context.DisposeAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _context.Database.CloseConnectionAsync();
        }
        finally
        {
            await _context.DisposeAsync();
        }
    }
}
