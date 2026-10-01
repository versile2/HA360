using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Realm.TestKit;

/// <summary>
/// A real Kestrel server on 127.0.0.1 and an ephemeral port, for middleware and endpoint tests (03 section 8.1 rule 5). The caller
/// composes the app with the same extension methods <c>Program.cs</c> calls, so no <c>WebApplicationFactory</c> package is needed.
/// All logging providers are removed; a test that wants the log adds an <see cref="InMemoryLogSink"/>.
/// </summary>
public sealed class KestrelHost : IAsyncDisposable
{
    private readonly WebApplication _app;

    private KestrelHost(WebApplication app, Uri baseAddress)
    {
        _app = app;
        BaseAddress = baseAddress;
    }

    /// <summary>The address the server is listening on, such as <c>http://127.0.0.1:41234/</c>.</summary>
    public Uri BaseAddress { get; }

    public IServiceProvider Services => _app.Services;

    /// <param name="configureBuilder">Adds configuration, logging and services.</param>
    /// <param name="configureApp">Adds middleware and endpoints.</param>
    public static async Task<KestrelHost> StartAsync(
        Action<WebApplicationBuilder> configureBuilder,
        Action<WebApplication> configureApp,
        CancellationToken cancellationToken = default)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        configureBuilder(builder);

        var app = builder.Build();
        configureApp(app);
        await app.StartAsync(cancellationToken);

        // With port 0 Kestrel replaces the requested address by the one it actually bound.
        return new KestrelHost(app, new Uri(app.Urls.First()));
    }

    /// <summary>A new client for <see cref="BaseAddress"/>; the caller disposes it.</summary>
    public HttpClient CreateClient() => new() { BaseAddress = BaseAddress };

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
