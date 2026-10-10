using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Realm.Domain;
using Realm.Infrastructure.Avatars;
using Realm.Infrastructure.Backfill;
using Realm.Infrastructure.Data;
using Realm.Infrastructure.Diagnostics;
using Realm.Infrastructure.Ha;
using Realm.Infrastructure.Ingestion;
using Realm.Infrastructure.Options;
using Realm.Infrastructure.Places;
using Realm.Infrastructure.Retention;
using Realm.Infrastructure.Roster;
using Realm.Infrastructure.Stats;

namespace Realm.Infrastructure.Hosting;

/// <summary>
/// The Live side of the composition (03 section 2.1): <see cref="AddRealmOptionsFile"/> puts the add-on options into the configuration,
/// <see cref="AddRealmLive"/> registers everything that talks to Home Assistant and the database.
/// </summary>
public static class RealmLiveServiceCollectionExtensions
{
    /// <summary>
    /// The configuration key under which <see cref="AddRealmOptionsFile"/> reports an options file it could not use (a message that names a key, never a
    /// value). <see cref="AddRealmLive"/> turns it into a refusal to start the data service (02 section 3.3).
    /// </summary>
    public const string OptionsFileErrorKey = "Realm:OptionsFileError";

    private const string DefaultOptionsPath = "/data/options.json";
    private const string DefaultDatabasePath = "/data/realm.db";
    private const string DefaultDataDirectory = "/data";
    private const string DevelopmentDatabasePath = "./realm.db";
    private static readonly TimeSpan AvatarTimeout = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Maps the Supervisor's <c>options.json</c> (<c>Realm:OptionsPath</c>, env <c>REALM_OPTIONS</c>) onto the configuration paths of 02 section 3.4 with
    /// <see cref="OptionsBinding.Flatten"/>, as a source that sits just below the environment variables, so that an environment variable beats the file
    /// (03 section 2.1). Nothing is watched: the Supervisor does not hot-reload options. A missing file adds nothing; a file that cannot be read or has a
    /// value of the wrong type adds <see cref="OptionsFileErrorKey"/> and the host still starts.
    /// </summary>
    public static void AddRealmOptionsFile(this ConfigurationManager configuration)
    {
        IReadOnlyDictionary<string, string?> data;
        try
        {
            var path = FirstNonEmpty(configuration["REALM_OPTIONS"], configuration["Realm:OptionsPath"]) ?? DefaultOptionsPath;
            if (!File.Exists(path))
            {
                return;
            }

            using var stream = File.OpenRead(path);
            using var document = JsonDocument.Parse(stream);
            data = OptionsBinding.Flatten(document.RootElement);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or FormatException)
        {
            // A FormatException names the key and never the value (OptionsBinding); the others are replaced by a fixed sentence.
            var message = ex is FormatException ? ex.Message : "The options file could not be read as JSON";
            data = new Dictionary<string, string?> { [OptionsFileErrorKey] = message };
        }

        var sources = configuration.Sources;
        var index = sources.Count;
        for (var i = sources.Count - 1; i >= 0; i--)
        {
            if (sources[i] is EnvironmentVariablesConfigurationSource { Prefix: null or "" })
            {
                index = i;
                break;
            }
        }

        sources.Insert(index, new MemoryConfigurationSource { InitialData = data });
    }

    /// <summary>
    /// Registers the Live services: the SQLite data layer first (its hosted services must start first), then the HA connection, the discovery refresher and
    /// the ingestion pipeline, then the trip recorder, the backfill and the retention job, the statistics service, the data source and its session factory,
    /// and the avatar proxy, the time zone self-check and the diagnostics of <c>diagnostics.json</c> (<see cref="DiagnosticsSnapshotBuilder"/>). Options that
    /// fail the validation of 02 section 3.3 refuse the data service: nothing but a logged reason starts, the state is built as <c>NotConfigured</c> so that
    /// Home Assistant reads Unavailable at once (the first-data error of 01 section 8.6, not Reconnecting for ever), and the diagnostics say so.
    /// </summary>
    /// <param name="configuration">Where <c>Realm:Ha:*</c>, <c>Realm:Db</c>, <c>SUPERVISOR_TOKEN</c> and the mapped options live.</param>
    /// <param name="demoFactory">The session factory for a circuit that asked for Demo with <c>?demo=1</c>; null when this host cannot make Demo sessions.</param>
    public static IServiceCollection AddRealmLive(this IServiceCollection services, IConfiguration configuration, IRealmSessionFactory? demoFactory = null)
    {
        var settings = LiveSettings.Read(configuration);
        var refused = settings.Errors.Count > 0;

        services.TryAddSingleton(TimeProvider.System);   // the state, the REST client, the connection, the refresher and the avatar service all take one
        services.AddRealmData(settings.DatabasePath);
        services.AddSingleton(settings.Options);
        services.AddSingleton(provider => RealmState.CreateInitial(settings.Options, provider.GetRequiredService<TimeProvider>(), refused));
        services.AddSingleton<DiscoveryState>();
        services.AddSingleton<RosterService>();
        services.AddSingleton<ZoneRefreshSignal>();
        services.AddSingleton<ZoneService>(provider => new ZoneService(
            provider.GetRequiredService<IHaGateway>(),
            provider.GetRequiredService<ZoneRefreshSignal>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<ILogger<ZoneService>>()));
        services.AddSingleton<IPlaceEditor>(provider => provider.GetRequiredService<ZoneService>());
        services.AddSingleton<ChangeNotifier>();
        services.AddSingleton<StatsService>();
        services.AddSingleton<HaDataSource>();
        services.AddSingleton<IRealmSessionFactory>(provider => new LiveRealmSessionFactory(provider.GetRequiredService<HaDataSource>(), demoFactory));

        services.AddSingleton(provider => new HaRestClient(
            NewHaClient(settings.RestBase),
            new HaRestOptions { BaseAddress = settings.RestBase, Token = settings.Token },
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<ILogger<HaRestClient>>(),
            provider.GetRequiredService<ServiceCounters>()));
        services.AddSingleton(provider =>
        {
            var connection = new HaWebSocketConnection(
                new HaWebSocketOptions { Endpoint = settings.WebSocket, Token = settings.Token },
                (item, token) => provider.GetRequiredService<IngestionPipeline>().EnqueueAsync(new FeedItem(item), token),
                provider.GetRequiredService<TimeProvider>(),
                provider.GetRequiredService<ILogger<HaWebSocketConnection>>(),
                provider.GetRequiredService<ServiceCounters>());
            connection.StatusChanged += status => provider.GetRequiredService<IngestionPipeline>().ApplyConnectionStatus(status);
            return connection;
        });
        services.AddSingleton(provider => new HaGateway(provider.GetRequiredService<HaWebSocketConnection>(), provider.GetRequiredService<HaRestClient>()));
        services.AddSingleton<IHaGateway>(provider => provider.GetRequiredService<HaGateway>());
        services.AddSingleton(provider => new HaDiscoveryRefresher(
            provider.GetRequiredService<IHaGateway>(),
            provider.GetRequiredService<RosterService>(),
            provider.GetRequiredService<DiscoveryState>(),
            (item, token) => provider.GetRequiredService<IngestionPipeline>().EnqueueAsync(item, token),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<ILogger<HaDiscoveryRefresher>>(),
            provider.GetRequiredService<ServiceCounters>(),
            provider.GetRequiredService<ZoneRefreshSignal>()));
        services.AddSingleton<RealmStateHydrator>();
        services.AddSingleton<IngestionPipeline>();
        services.AddSingleton<TripRecorder>();
        services.AddSingleton<BackfillService>();
        services.AddSingleton<RetentionService>();

        services.AddSingleton<IAvatarSource>(provider => new AvatarService(
            provider.GetRequiredService<DiscoveryState>(),
            provider.GetRequiredService<IHaGateway>(),
            NewLife360Client(),
            settings.AvatarCacheDirectory,
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<ILogger<AvatarService>>(),
            provider.GetRequiredService<ServiceCounters>()));

        // diagnostics.json (03 section 2.11): answered in Live from the counters and the current snapshot. A refused host has no websocket and no pipeline to
        // ask, so its websocket reads notConfigured (the state is already Unavailable, R3-07) and nobody has a stale threshold of their own.
        services.AddSingleton<IDiagnostics>(provider =>
        {
            Func<HaConnectionStatus?> websocket = static () => null;
            Func<IReadOnlyDictionary<string, int>> thresholds = static () => new Dictionary<string, int>();
            if (!refused)
            {
                var connection = provider.GetRequiredService<HaWebSocketConnection>();
                websocket = () => connection.Status;
                thresholds = provider.GetRequiredService<IngestionPipeline>().StaleAfterMinutes;
            }

            return new DiagnosticsSnapshotBuilder(
                provider.GetRequiredService<RealmState>(),
                settings.Options,
                provider.GetRequiredService<ServiceCounters>(),
                provider.GetRequiredService<TimeProvider>(),
                settings.DatabasePath,
                websocket,
                thresholds);
        });

        // The options are reported first, whatever follows. A refusal starts none of the services that talk to Home Assistant or work from what it reports.
        services.AddHostedService(provider => new OptionsReport(settings.Errors, settings.Warnings, provider.GetRequiredService<ILogger<OptionsReport>>()));

        // The time zone self-check of 03 section 5.1 runs whatever the options say: it is about this machine, and its answer is a warning of diagnostics.json.
        services.AddHostedService(provider => new ZoneDataSelfCheck(provider.GetRequiredService<ServiceCounters>(), provider.GetRequiredService<ILogger<ZoneDataSelfCheck>>()));
        if (!refused)
        {
            services.AddHostedService(provider => provider.GetRequiredService<HaWebSocketConnection>());
            services.AddHostedService(provider => provider.GetRequiredService<HaDiscoveryRefresher>());
            services.AddHostedService(provider => provider.GetRequiredService<IngestionPipeline>());

            // After the pipeline: the recorder subscribes to its trips, the backfill waits for the first discovery, the prune waits five minutes.
            services.AddHostedService(provider => provider.GetRequiredService<TripRecorder>());
            services.AddHostedService(provider => provider.GetRequiredService<BackfillService>());
            services.AddHostedService(provider => provider.GetRequiredService<RetentionService>());
        }

        return services;
    }

    // Redirects are never followed and the per-attempt timeout belongs to HaRestClient (on its clock).
    private static HttpClient NewHaClient(Uri baseAddress) =>
        new(new SocketsHttpHandler { AllowAutoRedirect = false, PooledConnectionLifetime = TimeSpan.FromMinutes(5) })
        {
            BaseAddress = baseAddress,
            Timeout = Timeout.InfiniteTimeSpan,
        };

    // The client of the Life360 avatar pictures: no base address, no default header, no token, and a redirect is a failure (03 section 10.4).
    private static HttpClient NewLife360Client() =>
        new(new SocketsHttpHandler { AllowAutoRedirect = false, PooledConnectionLifetime = TimeSpan.FromMinutes(5) })
        {
            Timeout = AvatarTimeout,
        };

    /// <summary>
    /// The path of the SQLite file: env <c>REALM_DB</c>, else <c>Realm:Db</c>, else <c>/data/realm.db</c> where the add-on's <c>/data</c> exists, else
    /// <c>./realm.db</c> for local development (03 section 2.1). The files that belong beside it (the avatar cache, the Data Protection keys of 03 section
    /// 5.7) are kept in its directory.
    /// </summary>
    public static string ResolveDatabasePath(IConfiguration configuration) =>
        FirstNonEmpty(configuration["REALM_DB"], configuration["Realm:Db"])
        ?? (Directory.Exists(DefaultDataDirectory) ? DefaultDatabasePath : DevelopmentDatabasePath);

    private static string? FirstNonEmpty(params string?[] values) => values.FirstOrDefault(value => !string.IsNullOrEmpty(value));

    /// <summary>What <see cref="AddRealmLive"/> reads from the configuration.</summary>
    private sealed record LiveSettings(
        string DatabasePath,
        string AvatarCacheDirectory,
        Uri WebSocket,
        Uri RestBase,
        string? Token,
        RealmOptions Options,
        IReadOnlyList<string> Errors,
        IReadOnlyList<string> Warnings)
    {
        public static LiveSettings Read(IConfiguration configuration)
        {
            var errors = new List<string>();
            if (configuration[OptionsFileErrorKey] is { Length: > 0 } fileError)
            {
                errors.Add(fileError);
            }

            RealmOptions options;
            try
            {
                options = OptionsBinding.Bind(configuration);
            }
            catch (FormatException ex)
            {
                // The message names the key, never the value.
                errors.Add(ex.Message);
                options = OptionsBinding.Defaults;
            }

            var validation = OptionsValidator.Validate(options);
            errors.AddRange(validation.Errors);

            var database = ResolveDatabasePath(configuration);
            var cache = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(database)) ?? ".", "cache", "avatars");

            return new LiveSettings(
                database,
                cache,
                Address(configuration["Realm:Ha:WebSocketUrl"], HaWebSocketOptions.SupervisorEndpoint, "Realm:Ha:WebSocketUrl", [Uri.UriSchemeWs, Uri.UriSchemeWss]),
                WithTrailingSlash(Address(configuration["Realm:Ha:RestBaseUrl"], HaRestOptions.SupervisorBaseAddress, "Realm:Ha:RestBaseUrl", [Uri.UriSchemeHttp, Uri.UriSchemeHttps])),
                FirstNonEmpty(configuration["Realm:Ha:Token"], configuration["SUPERVISOR_TOKEN"]),
                options,
                errors,
                validation.Warnings);
        }

        // A hosting address that is set but wrong stops the start with the key's name, never its value (it may carry a credential).
        private static Uri Address(string? value, Uri fallback, string key, string[] schemes)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return fallback;
            }

            return Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) && schemes.Contains(uri.Scheme, StringComparer.Ordinal)
                ? uri
                : throw new InvalidOperationException(key + " is not an address with one of the schemes " + string.Join(", ", schemes));
        }

        // Every REST path is relative to the base, so without a trailing slash its last segment would be replaced (03 section 2.1).
        private static Uri WithTrailingSlash(Uri uri) =>
            uri.AbsolutePath.EndsWith('/') ? uri : new UriBuilder(uri) { Path = uri.AbsolutePath + "/" }.Uri;
    }

    /// <summary>Logs what the options validation found, once, at start. It never throws and has nothing to stop.</summary>
    private sealed class OptionsReport(IReadOnlyList<string> errors, IReadOnlyList<string> warnings, ILogger<OptionsReport> logger) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
            foreach (var error in errors)
            {
                logger.LogError("The options were refused and the data service did not start: {Problem}", error);
            }

            foreach (var warning in warnings)
            {
                logger.LogWarning("An option needs attention: {Problem}", warning);
            }

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
