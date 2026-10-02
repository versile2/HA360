using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Realm.Domain;
using Realm.Infrastructure.Backfill;
using Realm.Infrastructure.Data;
using Realm.Infrastructure.Ha;
using Realm.Infrastructure.Hosting;
using Realm.Infrastructure.Ingestion;
using Realm.Infrastructure.Retention;
using Realm.Infrastructure.Stats;
using Realm.TestKit;
using Xunit;

namespace Realm.Web.Tests;

/// <summary>
/// What <c>AddRealmApp</c> composes in Live mode (03 section 2.1): the Live session factory, the data layer first, then the three services that talk to
/// Home Assistant and after them the trip recorder, the backfill and the retention job, which options that fail the validation of 02 section 3.3 keep from
/// starting. The test host points Home Assistant at an address that refuses at once and the database at a temp file, so nothing here reaches outside the process.
/// </summary>
public sealed class RealmLiveCompositionTests
{
    private const string FictionalToken = "fictional-test-token-0002";

    [Fact]
    public async Task InLiveMode_TheFactoryIsTheLiveOne_AndTheDataLayerStartsBeforeTheServicesThatTalkToHomeAssistant()
    {
        await using var host = await RealmTestHost.StartAsync(settings: LiveSettings());

        Assert.IsType<LiveRealmSessionFactory>(host.Services.GetRequiredService<IRealmSessionFactory>());
        Assert.NotNull(host.Services.GetService<IHaGateway>());
        var hosted = HostedServiceTypes(host);
        var order = new List<Type>
        {
            typeof(SchemaBootstrap),
            typeof(DbWriter),
            typeof(HaWebSocketConnection),
            typeof(HaDiscoveryRefresher),
            typeof(IngestionPipeline),
            typeof(TripRecorder),
            typeof(BackfillService),
            typeof(RetentionService),
        };
        Assert.All(order, type => Assert.Contains(type, hosted));
        Assert.Equal(order, hosted.Where(order.Contains).ToList());
    }

    [Fact]
    public async Task InDemoMode_NothingThatTalksToHomeAssistantIsComposed()
    {
        await using var host = await RealmTestHost.StartAsync();

        var hosted = HostedServiceTypes(host);
        Assert.DoesNotContain(typeof(HaWebSocketConnection), hosted);
        Assert.DoesNotContain(typeof(HaDiscoveryRefresher), hosted);
        Assert.DoesNotContain(typeof(IngestionPipeline), hosted);
        Assert.DoesNotContain(typeof(TripRecorder), hosted);
        Assert.DoesNotContain(typeof(BackfillService), hosted);
        Assert.DoesNotContain(typeof(RetentionService), hosted);
        Assert.Null(host.Services.GetService<IHaGateway>());
        Assert.Null(host.Services.GetService<IAvatarSource>());
    }

    [Fact]
    public async Task OptionsThatFailValidation_RefuseTheDataService_ButTheHostStillAnswers()
    {
        var logs = new InMemoryLogSink();
        var settings = LiveSettings();
        settings["Ui:StaleAfterMinutes"] = "120";
        settings["Ui:OfflineAfterHours"] = "1";   // 60 minutes, which is not above 120 (02 section 3.3)

        await using var host = await RealmTestHost.StartAsync(logs, settings);
        using var client = host.CreateClient();
        using var response = await client.GetAsync("healthz");

        var hosted = HostedServiceTypes(host);
        Assert.Contains(typeof(SchemaBootstrap), hosted);
        Assert.DoesNotContain(typeof(HaWebSocketConnection), hosted);
        Assert.DoesNotContain(typeof(HaDiscoveryRefresher), hosted);
        Assert.DoesNotContain(typeof(IngestionPipeline), hosted);
        Assert.DoesNotContain(typeof(TripRecorder), hosted);
        Assert.DoesNotContain(typeof(BackfillService), hosted);
        Assert.DoesNotContain(typeof(RetentionService), hosted);
        Assert.Contains(logs.Entries, entry => entry.Level == LogLevel.Error && entry.Message.Contains("ui_offline_after_hours", StringComparison.Ordinal));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // AddRealmLive is complete on its own (03 section 2.1): the clock its services take is registered by it, and a clock that the host registered first stays.
    [Fact]
    public void AddRealmLive_RegistersTheSystemClock_WhenNoneIsThere()
    {
        var services = new ServiceCollection();

        services.AddRealmLive(Configure(("SUPERVISOR_TOKEN", FictionalToken)));

        using var provider = services.BuildServiceProvider();
        Assert.Same(TimeProvider.System, provider.GetRequiredService<TimeProvider>());
    }

    [Fact]
    public void AddRealmLive_KeepsTheClockThatWasRegisteredFirst()
    {
        var clock = new OtherClock();
        var services = new ServiceCollection().AddSingleton<TimeProvider>(clock);

        services.AddRealmLive(Configure(("SUPERVISOR_TOKEN", FictionalToken)));

        using var provider = services.BuildServiceProvider();
        Assert.Same(clock, provider.GetRequiredService<TimeProvider>());
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(TimeProvider));
    }

    // The database path of 03 section 2.1, which the avatar cache and the Data Protection keys follow: the environment beats Realm:Db, which beats the default.
    [Fact]
    public void TheDatabasePath_IsTheEnvironmentVariable_ThenRealmDb_ThenTheDefault()
    {
        Assert.Equal("/env/realm.db", RealmLiveServiceCollectionExtensions.ResolveDatabasePath(Configure(("REALM_DB", "/env/realm.db"), ("Realm:Db", "/config/realm.db"))));
        Assert.Equal("/config/realm.db", RealmLiveServiceCollectionExtensions.ResolveDatabasePath(Configure(("REALM_DB", ""), ("Realm:Db", "/config/realm.db"))));

        var fallback = RealmLiveServiceCollectionExtensions.ResolveDatabasePath(Configure());   // /data in the add-on, the working directory on a development machine
        Assert.Equal(Directory.Exists("/data") ? "/data/realm.db" : "./realm.db", fallback);
    }

    private static IConfiguration Configure(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(pair => pair.Key, pair => (string?)pair.Value)).Build();

    private sealed class OtherClock : TimeProvider;

    private static Dictionary<string, string?> LiveSettings() => new() { ["SUPERVISOR_TOKEN"] = FictionalToken };

    // Every hosted service the host holds, by type, in the order it starts.
    private static List<Type> HostedServiceTypes(KestrelHost host) => host.Services.GetServices<IHostedService>().Select(service => service.GetType()).ToList();
}
