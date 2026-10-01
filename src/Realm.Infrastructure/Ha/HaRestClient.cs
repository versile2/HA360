using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Realm.Domain;

namespace Realm.Infrastructure.Ha;

/// <summary>
/// The REST side of Home Assistant (03 section 2.6): <c>config</c>, <c>states</c>, <c>template</c> (a read-only POST), <c>history/period</c> and
/// <c>image/serve</c>. That is the complete list of HA calls of the data layer (02 section 10.3): it never calls a service, writes a state or issues an
/// admin command. Every request URI is relative to <see cref="HaRestOptions.BaseAddress"/> and has no leading slash. A 502, 503 or 504 (Core is
/// restarting), a timeout or a refused connection is retried after 1, 2, 5, 10 and 30 s; history requests are sequential, 250 ms apart. The bearer
/// token goes only into the request header and is never logged.
/// </summary>
public sealed partial class HaRestClient
{
    // 02 section 1.2 step 2 and step 7, word for word.
    private const string IntegrationTemplate =
        "{{ {'life360': integration_entities('life360'), 'mobile_app': integration_entities('mobile_app'), 'fordpass': integration_entities('fordpass')} | tojson }}";

    private const string ZonesTemplate =
        "{% set o = namespace(l=[]) %}{% for z in states.zone %}"
        + "{% set o.l = o.l + [{'id': z.entity_id, 'name': z.name, 'lat': z.attributes.latitude, 'lon': z.attributes.longitude, 'r': z.attributes.radius, 'passive': z.attributes.passive}] %}"
        + "{% endfor %}{{ o.l | tojson }}";

    private static readonly IReadOnlyDictionary<string, JsonElement> NoAttributes = new Dictionary<string, JsonElement>(0);

    private readonly HttpClient _http;
    private readonly HaRestOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _historyGate = new(1, 1);
    private DateTimeOffset? _lastHistoryEnd;

    /// <param name="http">The client; its <c>BaseAddress</c> is the one of <paramref name="options"/>, and a request that is not allowed to follow redirects must come from a handler that does not.</param>
    /// <param name="options">Token, base address and timings.</param>
    /// <param name="time">The clock of the timeouts, the retry waits and the history spacing.</param>
    public HaRestClient(HttpClient http, HaRestOptions options, TimeProvider time, ILogger<HaRestClient> logger)
    {
        _http = http;
        _options = options;
        _time = time;
        _logger = logger;
    }

    /// <summary><c>GET config</c>: the time zone and the version.</summary>
    public Task<HaConfig> GetConfigAsync(CancellationToken cancellationToken)
    {
        return CallAsync(
            "config",
            () => Get("config"),
            allowRetry: true,
            async (response, token) =>
            {
                response.EnsureSuccessStatusCode();
                using var document = await ReadJsonAsync(response, token);
                var root = document.RootElement;
                var zone = Text(root, "time_zone") ?? throw new InvalidDataException("Home Assistant's configuration has no time_zone");
                return new HaConfig(zone, Text(root, "version"));
            },
            cancellationToken);
    }

    /// <summary><c>GET states</c>: one snapshot per entity for which <paramref name="include"/> returns true (all when it is null); the rest are never copied.</summary>
    public Task<IReadOnlyList<HaEntitySnapshot>> GetStatesAsync(Func<string, bool>? include, CancellationToken cancellationToken)
    {
        return CallAsync<IReadOnlyList<HaEntitySnapshot>>(
            "states",
            () => Get("states"),
            allowRetry: true,
            async (response, token) =>
            {
                response.EnsureSuccessStatusCode();
                using var document = await ReadJsonAsync(response, token);
                var states = new List<HaEntitySnapshot>();
                if (document.RootElement.ValueKind != JsonValueKind.Array)
                {
                    throw new InvalidDataException("The states answer is not a list");
                }

                foreach (var item in document.RootElement.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Object
                        && Text(item, "entity_id") is { } id
                        && (include is null || include(id))
                        && ParseState(item, id) is { } snapshot)
                    {
                        states.Add(snapshot);
                    }
                }

                return states;
            },
            cancellationToken);
    }

    /// <summary><c>POST template</c>: renders a template and returns the text HA answers. Read-only: a template cannot change anything.</summary>
    public Task<string> RenderTemplateAsync(string template, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(template);
        return CallAsync(
            "template",
            () => new HttpRequestMessage(HttpMethod.Post, "template")
            {
                Content = new StringContent(JsonSerializer.Serialize(new Dictionary<string, string> { ["template"] = template }), Encoding.UTF8, "application/json"),
            },
            allowRetry: true,
            async (response, token) =>
            {
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync(token);
            },
            cancellationToken);
    }

    /// <summary>The discovery template of 02 section 1.2 step 2.</summary>
    public async Task<HaIntegrationEntities> GetIntegrationEntitiesAsync(CancellationToken cancellationToken)
    {
        var text = await RenderTemplateAsync(IntegrationTemplate, cancellationToken);
        using var document = ParseJson(text, "integration entities");
        var root = document.RootElement;
        return new HaIntegrationEntities(Strings(root, "life360"), Strings(root, "mobile_app"), Strings(root, "fordpass"));
    }

    /// <summary>The zone template of 02 section 1.2 step 7. A zone without a usable position or radius is left out.</summary>
    public async Task<IReadOnlyList<RawPlace>> GetZonesAsync(CancellationToken cancellationToken)
    {
        var text = await RenderTemplateAsync(ZonesTemplate, cancellationToken);
        using var document = ParseJson(text, "zones");
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("The zones answer is not a list");
        }

        var zones = new List<RawPlace>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object && ParseZone(item) is { } zone)
            {
                zones.Add(zone);
            }
        }

        return zones;
    }

    /// <summary>
    /// <c>GET history/period</c> of one entity (02 section 8.2): significant changes are never filtered out, sensors come as <c>minimal_response</c> and
    /// <c>no_attributes</c>, and the instants are UTC with their offset, URL-encoded. Chunking is the caller's job. Requests wait for the 250 ms spacing.
    /// </summary>
    public async Task<IReadOnlyList<HaEntitySnapshot>> GetHistoryAsync(string entityId, DateTimeOffset start, DateTimeOffset end, bool withAttributes, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityId);
        var path = $"history/period/{Instant(start)}?filter_entity_id={Uri.EscapeDataString(entityId)}&end_time={Instant(end)}&significant_changes_only=0"
            + (withAttributes ? string.Empty : "&minimal_response&no_attributes");

        await _historyGate.WaitAsync(cancellationToken);
        try
        {
            if (_lastHistoryEnd is { } last)
            {
                var wait = last + _options.HistorySpacing - _time.GetUtcNow();
                if (wait > TimeSpan.Zero)
                {
                    await Task.Delay(wait, _time, cancellationToken);
                }
            }

            try
            {
                return await CallAsync<IReadOnlyList<HaEntitySnapshot>>(
                    "history",
                    () => Get(path),
                    allowRetry: true,
                    async (response, token) =>
                    {
                        response.EnsureSuccessStatusCode();
                        using var document = await ReadJsonAsync(response, token);
                        return ParseHistory(document.RootElement, entityId);
                    },
                    cancellationToken);
            }
            finally
            {
                _lastHistoryEnd = _time.GetUtcNow();
            }
        }
        finally
        {
            _historyGate.Release();
        }
    }

    /// <summary>
    /// <c>GET</c> of an <c>image/serve/{id}/{size}</c> path (a leading <c>/api/</c>, as in an entity picture, is dropped). Nothing else is fetched: any other
    /// path is an <see cref="ArgumentException"/>. Returns null unless HA answers 200 (a redirect is a failure); a body above
    /// <paramref name="maxBytes"/> is an <see cref="InvalidDataException"/>. Not retried: a person is waiting for the picture.
    /// </summary>
    public Task<AvatarImage?> GetImageAsync(string pathAndQuery, int maxBytes, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxBytes, 1);
        var path = NormalizeImagePath(pathAndQuery) ?? throw new ArgumentException("Only relative image/serve/ paths are fetched", nameof(pathAndQuery));
        return CallAsync<AvatarImage?>(
            "image",
            () => Get(path),
            allowRetry: false,
            async (response, token) =>
            {
                if (response.StatusCode != HttpStatusCode.OK)
                {
                    return null;
                }

                if (response.Content.Headers.ContentLength > maxBytes)
                {
                    throw new InvalidDataException("The image is larger than the limit");
                }

                await using var stream = await response.Content.ReadAsStreamAsync(token);
                using var bytes = new MemoryStream();
                var buffer = new byte[8192];
                int read;
                while ((read = await stream.ReadAsync(buffer, token)) > 0)
                {
                    if (bytes.Length + read > maxBytes)
                    {
                        throw new InvalidDataException("The image is larger than the limit");
                    }

                    bytes.Write(buffer, 0, read);
                }

                return new AvatarImage(bytes.ToArray(), response.Content.Headers.ContentType?.MediaType ?? string.Empty);
            },
            cancellationToken);
    }

    // ---- the call loop ------------------------------------------------------------------------------------------

    private static HttpRequestMessage Get(string relativePath) => new(HttpMethod.Get, relativePath);

    private static bool IsTransient(HttpStatusCode status) =>
        status is HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout;

    // One attempt per pass. A transient failure waits and tries again until the delays are used up; everything else (a 4xx, a body that does not parse,
    // a cancellation by the caller) ends the call at once. The per-attempt timeout covers the headers and the body (read), on the injected clock.
    private async Task<T> CallAsync<T>(
        string call,
        Func<HttpRequestMessage> create,
        bool allowRetry,
        Func<HttpResponseMessage, CancellationToken, Task<T>> read,
        CancellationToken cancellationToken)
    {
        var retries = allowRetry ? _options.RetryDelays.Count : 0;
        for (var attempt = 0; ; attempt++)
        {
            string reason;
            using (var timeout = new CancellationTokenSource(_options.Timeout, _time))
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token))
            {
                try
                {
                    using var request = create();
                    if (!string.IsNullOrEmpty(_options.Token))
                    {
                        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.Token);
                    }

                    using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linked.Token);
                    if (!IsTransient(response.StatusCode) || attempt >= retries)
                    {
                        return await read(response, linked.Token);
                    }

                    reason = ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture);
                }
                catch (HttpRequestException ex) when (ex.StatusCode is null && attempt < retries && !cancellationToken.IsCancellationRequested)
                {
                    reason = "connection";
                }
                catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested && attempt < retries)
                {
                    reason = "timeout";
                }
            }

            var delay = _options.RetryDelays[attempt];
            _logger.LogWarning(
                "Home Assistant {Call} failed ({Reason}); retry {Attempt} of {Retries} in {DelaySeconds} s",
                call,
                reason,
                attempt + 1,
                retries,
                delay.TotalSeconds);
            await Task.Delay(delay, _time, cancellationToken);
        }
    }

    // ---- parsing ------------------------------------------------------------------------------------------------

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private static JsonDocument ParseJson(string text, string what)
    {
        try
        {
            return JsonDocument.Parse(text);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"The {what} answer is not JSON", ex);
        }
    }

    private static HaEntitySnapshot? ParseState(JsonElement item, string? fallbackEntityId)
    {
        var id = Text(item, "entity_id") ?? fallbackEntityId;
        if (id is null)
        {
            return null;
        }

        var attributes = NoAttributes;
        if (item.TryGetProperty("attributes", out var raw) && raw.ValueKind == JsonValueKind.Object)
        {
            var map = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var property in raw.EnumerateObject())
            {
                map[property.Name] = property.Value.Clone();
            }

            attributes = map;
        }

        var changed = Instant(item, "last_changed");
        return new HaEntitySnapshot(id, Text(item, "state") ?? string.Empty, attributes, changed, Instant(item, "last_updated") ?? changed);
    }

    // History is a list of lists, one per entity; with minimal_response only the first row of a list names its entity.
    private static List<HaEntitySnapshot> ParseHistory(JsonElement root, string requestedEntityId)
    {
        var rows = new List<HaEntitySnapshot>();
        if (root.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("The history answer is not a list");
        }

        foreach (var series in root.EnumerateArray())
        {
            if (series.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            string? seriesId = null;
            foreach (var item in series.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                seriesId = Text(item, "entity_id") ?? seriesId ?? requestedEntityId;
                if (ParseState(item, seriesId) is { } row)
                {
                    rows.Add(row);
                }
            }
        }

        return rows;
    }

    private static RawPlace? ParseZone(JsonElement item)
    {
        const string prefix = "zone.";
        var id = Text(item, "id");
        if (id is null || !id.StartsWith(prefix, StringComparison.Ordinal) || id.Length == prefix.Length)
        {
            return null;
        }

        if (Number(item, "lat") is not { } lat || Number(item, "lon") is not { } lon || Number(item, "r") is not { } radius)
        {
            return null;
        }

        if (lat is < -90 or > 90 || lon is < -180 or > 180 || radius < 0)
        {
            return null;
        }

        var passive = item.TryGetProperty("passive", out var flag) && flag.ValueKind == JsonValueKind.True;
        return new RawPlace(id[prefix.Length..], Text(item, "name")?.Trim() ?? id[prefix.Length..], lat, lon, radius, passive);
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static double? Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) && double.IsFinite(number)
            ? number
            : null;

    private static DateTimeOffset? Instant(JsonElement element, string name) =>
        Text(element, name) is { } text
        && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var instant)
            ? instant.ToUniversalTime()
            : null;

    private static List<string> Strings(JsonElement element, string name)
    {
        var values = new List<string>();
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in list.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } text)
                {
                    values.Add(text);
                }
            }
        }

        return values;
    }

    // An ISO instant in UTC with its offset, percent-encoded as a whole (research 4.2): "2026-10-01T00%3A00%3A00%2B00%3A00".
    private static string Instant(DateTimeOffset instant) =>
        Uri.EscapeDataString(instant.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'+00:00'", CultureInfo.InvariantCulture));

    // "image/serve/{id}/{size}", with or without a leading slash and "api/" (the form of an entity_picture). The two segments are plain tokens: no dots,
    // so no "..", no percent escapes, no query, no second slash.
    private static string? NormalizeImagePath(string? pathAndQuery)
    {
        var path = pathAndQuery?.Trim().TrimStart('/');
        if (path is not null && path.StartsWith("api/", StringComparison.Ordinal))
        {
            path = path["api/".Length..];
        }

        return path is not null && ImagePath().IsMatch(path) ? path : null;
    }

    [GeneratedRegex("^image/serve/[A-Za-z0-9_-]{1,64}/[A-Za-z0-9_-]{1,32}$", RegexOptions.CultureInvariant)]
    private static partial Regex ImagePath();
}
