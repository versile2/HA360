using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Realm.Domain;
using Realm.Infrastructure.Diagnostics;

namespace Realm.Infrastructure.Data;

/// <summary>The one call that puts the SQLite data layer into the host's services (D58).</summary>
public static class RealmDataServiceCollectionExtensions
{
    /// <summary>
    /// Registers the pooled <see cref="RealmDb"/> factory over <paramref name="databasePath"/> (the resolved <c>Realm:Db</c> value), then the hosted
    /// services <see cref="SchemaBootstrap"/> and <see cref="DbWriter"/> in that order, <see cref="IRealmWriter"/> and <see cref="IRealmQueries"/>.
    /// Hosted services start in registration order and the schema step must be first (03 section 2.4), so call this before registering any other
    /// hosted service. A <see cref="TimeProvider"/> is added when the host has none, and so is the <see cref="ServiceCounters"/> the schema step and the
    /// writer fill in (they take it as an optional argument, so a host that registers its own sees it used).
    /// </summary>
    public static IServiceCollection AddRealmData(this IServiceCollection services, string databasePath)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton(provider => new ServiceCounters(provider.GetRequiredService<TimeProvider>()));
        services.AddPooledDbContextFactory<RealmDb>(options => RealmDb.Configure(options, databasePath));
        services.AddSingleton<IHostedService>(provider => new SchemaBootstrap(
            databasePath,
            provider.GetRequiredService<ILogger<SchemaBootstrap>>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<ServiceCounters>()));
        services.AddSingleton<DbWriter>();
        services.AddSingleton<IRealmWriter>(provider => provider.GetRequiredService<DbWriter>());
        services.AddHostedService(provider => provider.GetRequiredService<DbWriter>());
        services.AddSingleton<IRealmQueries, SqliteRealmQueries>();
        return services;
    }
}
