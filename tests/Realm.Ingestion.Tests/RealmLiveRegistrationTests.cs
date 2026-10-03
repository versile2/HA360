using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Realm.Domain;
using Realm.Infrastructure.Backfill;
using Realm.Infrastructure.Data;
using Realm.Infrastructure.Diagnostics;
using Realm.Infrastructure.Ha;
using Realm.Infrastructure.Hosting;
using Realm.Infrastructure.Ingestion;
using Realm.Infrastructure.Retention;
using Realm.Infrastructure.Stats;
using Realm.TestKit;
using Xunit;

namespace Realm.Ingestion.Tests;

/// <summary>
/// 03 section 2.1: <see cref="RealmLiveServiceCollectionExtensions.AddRealmLive"/> wires the trip and statistics services into the Live host: the whole graph
/// resolves, the recorder, the backfill and the retention job start after the pipeline whose trips and hydrated members they depend on, and options that were
/// refused start none of them. Options that were refused also build the state as <c>NotConfigured</c>, so Home Assistant reads Unavailable at once and not
/// Reconnecting for ever (R3-07), and the Live host answers <c>diagnostics.json</c> from <see cref="DiagnosticsSnapshotBuilder"/> over one set of
/// <see cref="ServiceCounters"/>. Nothing is started here, so no socket and no database file is touched.
/// </summary>
public sealed class RealmLiveRegistrationTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 2, 15, 0, 0, TimeSpan.Zero);

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

    // ---- R3-07: a refused host is Unavailable at once ---------------------------------------------------------------

    [Fact]
    public void RefusedOptions_BuildTheStateAsNotConfigured_SoHomeAssistantReadsUnavailableAtOnce()
    {
        var clock = new ManualTimeProvider(Start);
        using var provider = Build(Refused(), clock);

        var home = Assert.Single(provider.GetRequiredService<RealmState>().Current.Connections, connection => connection.Name == ConnectionNames.HomeAssistant);

        Assert.Equal(ConnectionState.Unavailable, home.State);   // not Reconnecting for the first 15 s, and not for ever: nothing is ever going to replace this snapshot
        Assert.Null(home.LastSyncUtc);
    }

    [Fact]
    public void ValidOptions_StillStartWithHomeAssistantBeingReachedForTheFirstTime()
    {
        var clock = new ManualTimeProvider(Start);
        using var provider = Build([], clock);

        var home = Assert.Single(provider.GetRequiredService<RealmState>().Current.Connections, connection => connection.Name == ConnectionNames.HomeAssistant);

        Assert.Equal(ConnectionState.Reconnecting, home.State);   // the pipeline's first status says the same, and turns it Unavailable after 15 s
    }

    [Fact]
    public void ARefusedHostsDiagnostics_RaiseHaUnavailable_AndSayTheWebsocketIsNotConfigured()
    {
        using var provider = Build(Refused(), new ManualTimeProvider(Start));

        var snapshot = provider.GetRequiredService<IDiagnostics>().GetSnapshot();

        Assert.Equal("live", snapshot.Mode);
        Assert.Equal(["ha_unavailable"], snapshot.Warnings);
        Assert.Equal("notConfigured", snapshot.Ha.WebsocketState);
        Assert.Equal(ConnectionState.Unavailable, snapshot.Connections.Single(connection => connection.Name == ConnectionNames.HomeAssistant).State);
    }

    // ---- diagnostics.json in Live ------------------------------------------------------------------------------------

    [Fact]
    public void TheLiveHost_RegistersTheLiveDiagnostics_OverTheOneSetOfCounters()
    {
        using var provider = Build([], new ManualTimeProvider(Start));
        var diagnostics = provider.GetRequiredService<IDiagnostics>();
        var counters = provider.GetRequiredService<ServiceCounters>();

        var before = diagnostics.GetSnapshot();
        counters.RecordIngestSkipped();   // a service somewhere in the host skipped an item
        var after = diagnostics.GetSnapshot();

        Assert.IsType<DiagnosticsSnapshotBuilder>(diagnostics);
        Assert.Same(diagnostics, provider.GetRequiredService<IDiagnostics>());
        Assert.Same(counters, provider.GetRequiredService<ServiceCounters>());
        Assert.Equal("live", before.Mode);
        Assert.Equal("connecting", before.Ha.WebsocketState);   // the connection has not started, so it is where a new one begins
        Assert.Empty(before.Warnings);
        Assert.Equal(["ingest_drops"], after.Warnings);
        Assert.Equal(1, after.Ingestion.Dropped);
    }

    [Fact]
    public void TheLiveHost_HostsTheTimeZoneSelfCheck_AfterTheDataLayer_EvenWhenTheOptionsWereRefused()
    {
        foreach (var settings in new[] { new Dictionary<string, string?>(), Refused() })
        {
            using var provider = Build(settings, new ManualTimeProvider(Start));

            var hosted = provider.GetServices<IHostedService>().Select(service => service.GetType()).ToList();

            Assert.Contains(typeof(ZoneDataSelfCheck), hosted);
            Assert.True(hosted.IndexOf(typeof(ZoneDataSelfCheck)) > hosted.IndexOf(typeof(DbWriter)));
        }
    }

    [Fact]
    public void TheServicesThatCount_ShareTheOneSetOfCounters_TheDataLayerRegistered()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Realm:Db"] = Path.Combine(Path.GetTempPath(), "realm-registration-tests-" + Guid.NewGuid().ToString("N"), "realm.db") })
            .Build();
        services.AddRealmLive(configuration);

        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(ServiceCounters));
    }

    private static Dictionary<string, string?> Refused() =>
        new() { [RealmLiveServiceCollectionExtensions.OptionsFileErrorKey] = "The options file could not be read as JSON" };

    private static ServiceProvider Build(Dictionary<string, string?> settings, TimeProvider? clock = null)
    {
        settings["Realm:Db"] = Path.Combine(Path.GetTempPath(), "realm-registration-tests-" + Guid.NewGuid().ToString("N"), "realm.db");   // never created: nothing is started
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        if (clock is not null)
        {
            services.AddSingleton(clock);   // the first clock registered stays: AddRealmLive adds the system clock only when there is none
        }

        services.AddRealmLive(configuration);
        return services.BuildServiceProvider();
    }
}
