using System.Net.Http.Json;
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
/// (a hand-started host has no static-web-assets manifest). It adds two test-only endpoints: <c>echo</c> reports what the pipeline
/// did to the request, and <c>boom</c> throws.
/// </summary>
internal static class RealmTestHost
{
    // A path that never exists, so a developer's own /data/options.json cannot change the mode a test sees.
    private static readonly string NoOptionsFile = Path.Combine(AppContext.BaseDirectory, "no-options.json");

    public static Task<KestrelHost> StartAsync(InMemoryLogSink? logs = null, IReadOnlyDictionary<string, string?>? settings = null)
    {
        return KestrelHost.StartAsync(
            builder =>
            {
                builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Realm:OptionsPath"] = NoOptionsFile });
                if (settings is not null)
                {
                    builder.Configuration.AddInMemoryCollection(settings);
                }

                if (logs is not null)
                {
                    builder.Logging.AddProvider(logs);
                }

                var runtime = RuntimeOptions.Detect(builder.Configuration, builder.Environment);
                builder.Services.AddRealmWeb(runtime);
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
            });
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
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<RequestEcho>()
            ?? throw new InvalidOperationException("The echo endpoint returned no body.");
    }
}
