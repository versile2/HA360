namespace Realm.Web.Hosting;

/// <summary>
/// The <c>healthcheck</c> CLI mode (03 section 6.3): one loopback <c>GET healthz</c> with a 5 s timeout, exit code 0 or 1. It runs
/// before the host is built, so it uses no dependency injection. Used only by <c>image-smoke.sh</c>.
/// </summary>
public static class HealthProbe
{
    private const int DefaultPort = 8099;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    /// <summary>Probes the port Kestrel listens on (<c>ASPNETCORE_HTTP_PORTS</c>, 8099 in the image).</summary>
    public static Task<int> RunAsync() => RunAsync(new Uri($"http://127.0.0.1:{ResolvePort()}/"));

    /// <summary>Returns 0 when <paramref name="baseAddress"/> answers <c>healthz</c> with a success status, otherwise 1.</summary>
    public static async Task<int> RunAsync(Uri baseAddress)
    {
        try
        {
            using var client = new HttpClient { Timeout = Timeout };
            using var response = await client.GetAsync(new Uri(baseAddress, "healthz"));
            return response.IsSuccessStatusCode ? 0 : 1;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            return 1;
        }
    }

    private static int ResolvePort()
    {
        var configured = Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS")?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return configured is { Length: > 0 } && int.TryParse(configured[0], out var port) ? port : DefaultPort;
    }
}
