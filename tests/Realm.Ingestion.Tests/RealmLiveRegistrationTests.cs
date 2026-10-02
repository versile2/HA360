using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Realm.Domain;
using Realm.Infrastructure.Backfill;
using Realm.Infrastructure.Data;
using Realm.Infrastructure.Ha;
using Realm.Infrastructure.Hosting;
using Realm.Infrastructure.Ingestion;
using Realm.Infrastructure.Retention;
using Realm.Infrastructure.Stats;
using Xunit;

namespace Realm.Ingestion.Tests;

/// <summary>
/// 03 section 2.1: <see cref="RealmLiveServiceCollectionExtensions.AddRealmLive"/> wires the trip and statistics services into the Live host: the whole graph
/// resolves, the recorder, the backfill and the retention job start after the pipeline whose trips and hydrated members they depend on, and options that were
/// refused start none of them. Nothing is started here, so no socket and no database file is touched.
/// </summary>
public sealed class RealmLiveRegistrationTests
{
    private static readonly Type[] ExpectedOrder =
    [
        typeof(SchemaBootstrap),
        typeof(DbWriter),
        typeof(HaWebSocketConnection),
        typeof(HaDiscoveryRefresher),
        typeof(IngestionPipeline),
        typeof(TripRecorder),
        typeof(BackfillService),
        typeof(RetentionService),
    ];

    [Fact]
    public void TheWholeGraphResolves_AndTheServicesAreHostedInTheOrderTheyNeed()
    {
        using var provider = Build([]);

        var hosted = provider.GetServices<IHostedService>().Select(service => service.GetType()).ToList();

        Assert.Equal(ExpectedOrder, hosted.Where(ExpectedOrder.Contains).ToList());
        Assert.NotNull(provider.GetRequiredService<HaDataSource>());
        Assert.Same(provider.GetRequiredService<StatsService>(), provider.GetRequiredService<StatsService>());
        Assert.Same(provider.GetRequiredService<RealmStateHydrator>(), provider.GetRequiredService<RealmStateHydrator>());
        Assert.Same(provider.GetRequiredService<DbWriter>(), provider.GetRequiredService<IRealmWriter>());
    }

    [Fact]
    public void RefusedOptions_StartNoneOfThem()
    {
        using var provider = Build(new Dictionary<string, string?> { [RealmLiveServiceCollectionExtensions.OptionsFileErrorKey] = "The options file could not be read as JSON" });

        var hosted = provider.GetServices<IHostedService>().Select(service => service.GetType()).ToList();

        Assert.DoesNotContain(typeof(IngestionPipeline), hosted);
        Assert.DoesNotContain(typeof(TripRecorder), hosted);
        Assert.DoesNotContain(typeof(BackfillService), hosted);
        Assert.DoesNotContain(typeof(RetentionService), hosted);
    }

    private static ServiceProvider Build(Dictionary<string, string?> settings)
    {
        settings["Realm:Db"] = Path.Combine(Path.GetTempPath(), "realm-registration-tests-" + Guid.NewGuid().ToString("N"), "realm.db");   // never created: nothing is started
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRealmLive(configuration);
        return services.BuildServiceProvider();
    }
}
