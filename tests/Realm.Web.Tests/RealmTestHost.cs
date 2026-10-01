using System.Diagnostics;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Realm.TestKit;
using Realm.Web.Hosting;

namespace Realm.Web.Tests;

/// <summary>
/// Starts the Realm pipeline on a real Kestrel host, composed with the same calls as <c>Program.cs</c> except <c>MapStaticAssets</c>
/// (a hand-started host has no static-web-assets manifest). The services come from <c>AddRealmApp</c>, the method <c>Program.cs</c> uses
/// too, so the two cannot drift apart. It adds two test-only endpoints: <c>echo</c> reports what the pipeline did to the request, and
/// <c>boom</c> throws. A test that needs more endpoints, such as the Razor components, passes them in <c>mapEndpoints</c>, and one that needs
/// to replace a service, such as the avatar source, passes <c>configureServices</c>, which runs after <c>AddRealmApp</c>.
/// </summary>
/// <remarks>
/// Every host logs into an <see cref="InMemoryLogSink"/> (the one the test passes, or its own), with exceptions written out in full.
/// <see cref="EnsureSuccessAsync"/> turns a failed response into an exception that carries those logged errors, because the response of a
/// failed request is only a generic 500 (03 section 5.2).
/// </remarks>
internal static class RealmTestHost
{
    // A path that never exists, so a developer's own /data/options.json cannot change the mode a test sees.
    private static readonly string NoOptionsFile = Path.Combine(AppContext.BaseDirectory, "no-options.json");

    // The sink of each started host, so a helper that only holds the host can read what the server logged.
    private static readonly ConditionalWeakTable<KestrelHost, InMemoryLogSink> Sinks = new();

    public static async Task<KestrelHost> StartAsync(
        InMemoryLogSink? logs = null,
        IReadOnlyDictionary<string, string?>? settings = null,
        Action<WebApplication>? mapEndpoints = null,
        Action<IServiceCollection>? configureServices = null)
    {
        var sink = logs ?? new InMemoryLogSink();
        var host = await KestrelHost.StartAsync(
            builder =>
            {
                builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Realm:OptionsPath"] = NoOptionsFile,

                    // A Live host (a test that sets a token) starts the data layer and the three HA services. Its database is a file of its own in the
                    // temp directory, and Home Assistant is an address that refuses at once, so no test depends on /data or resolves "supervisor".
                    ["Realm:Db"] = Path.Combine(Path.GetTempPath(), "realm-web-test-" + Guid.NewGuid().ToString("N") + ".db"),
                    ["Realm:Ha:WebSocketUrl"] = "ws://127.0.0.1:1/websocket",
                    ["Realm:Ha:RestBaseUrl"] = "http://127.0.0.1:1/api/",
                });
                if (settings is not null)
                {
                    builder.Configuration.AddInMemoryCollection(settings);
                }

                builder.Logging.AddProvider(new ExceptionTextLogProvider(sink));

                var runtime = RuntimeOptions.Detect(builder.Configuration, builder.Environment);
                builder.Services.AddRealmApp(runtime, builder.Configuration);
                configureServices?.Invoke(builder.Services);
            },
            app =>
            {
                app.UseRealmPipeline();
                app.MapRealmEndpoints();
                app.MapGet("echo", (HttpRequest request) => Results.Json(new RequestEcho(
                    request.Scheme,
                    request.Host.Value ?? string.Empty,
                    request.PathBase.Value ?? string.Empty,
                    request.Path.Value ?? string.Empty)));
                app.MapGet("boom", Boom);
                mapEndpoints?.Invoke(app);
            });

        Sinks.Add(host, sink);
        return host;
    }

    private static IResult Boom() => throw new InvalidOperationException("secret detail that must not reach the response");

    /// <summary>Calls <c>echo</c> with the given request headers and returns what the pipeline made of them.</summary>
    public static async Task<RequestEcho> GetEchoAsync(KestrelHost host, params (string Name, string Value)[] headers)
    {
        using var client = host.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "echo");
        foreach (var (name, value) in headers)
        {
            request.Headers.TryAddWithoutValidation(name, value);
        }

        using var response = await client.SendAsync(request);
        await EnsureSuccessAsync(host, response);
        return await response.Content.ReadFromJsonAsync<RequestEcho>()
            ?? throw new InvalidOperationException("The echo endpoint returned no body.");
    }

    /// <summary>
    /// Like <see cref="HttpResponseMessage.EnsureSuccessStatusCode"/>, but the exception also lists what the host logged at warning level
    /// or above, exceptions and stack traces included, so a 500 shows the server-side cause and not just its status code.
    /// </summary>
    public static async Task EnsureSuccessAsync(KestrelHost host, HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var logged = "The server logged no warning or error.";
        if (Sinks.TryGetValue(host, out var sink))
        {
            if ((int)response.StatusCode >= 500)
            {
                await WaitForLoggedErrorAsync(sink);
            }

            var problems = sink.Entries
                .Where(entry => entry.Level >= LogLevel.Warning)
                .Select(entry => $"[{entry.Level}] {entry.Category}: {entry.Message}")
                .ToList();
            if (problems.Count > 0)
            {
                logged = $"The server logged:{Environment.NewLine}{string.Join(Environment.NewLine, problems)}";
            }
        }

        throw new HttpRequestException(
            $"{(int)response.StatusCode} ({response.ReasonPhrase}) from {response.RequestMessage?.RequestUri}. {logged}",
            null,
            response.StatusCode);
    }

    // The exception handler writes the 500 first and logs the exception afterwards, so on a cold host the client can have the response
    // before the entry exists. A failing test waits for it (at most five seconds); a passing test never gets here.
    private static async Task WaitForLoggedErrorAsync(InMemoryLogSink sink)
    {
        var waited = Stopwatch.StartNew();
        while (waited.Elapsed < TimeSpan.FromSeconds(5) && !sink.Entries.Any(entry => entry.Level >= LogLevel.Error))
        {
            await Task.Delay(20);
        }
    }
}
