using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Net.Http.Headers;
using Realm.Domain;
using Realm.Web.Theme;

namespace Realm.Web.Hosting;

public static class RealmEndpointRouteBuilderExtensions
{
    // camelCase names and string enums, the field names of 03 section 2.11 (`connections[].state` is a word, not a number).
    private static readonly JsonSerializerOptions DiagnosticsJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>The plain endpoints of 03 section 3.3 that exist so far. Route templates carry no leading slash.</summary>
    public static IEndpointRouteBuilder MapRealmEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Liveness only (03 section 2.14): it must never depend on HA, or the Supervisor watchdog would restart the add-on whenever HA restarts.
        endpoints.MapGet("healthz", () => Results.Text("ok"));

        // The exception handler re-runs the request here; the 500 status is already set, so this writes only the generic text.
        endpoints.MapGet("error-plain", () => Results.Text("Something went wrong in the Realm."));

        // The theme tokens are an endpoint, not a file (03 sections 3.8 and 5.3): no-cache plus a strong ETag, so a revalidation costs a 304.
        endpoints.MapGet("css/tokens.css", TokensCss);

        // The avatar proxy (03 section 10.4): the path carries a member id and nothing else, and the picture is the one the member's own option chose.
        endpoints.MapGet("avatars/{memberId}", AvatarAsync);

        // The only diagnostics surface of v1 (03 section 2.11): states, counts, versions and fixed warning codes, open to every panel user (D43).
        endpoints.MapGet("diagnostics.json", DiagnosticsJson);

        return endpoints;
    }

    // Demo registers no IAvatarSource (its members have no picture, R2-016), so it answers 404 like a member without one. A refusal or a failed
    // upstream is a 404 with an empty body too; the service has logged why, and the UI draws initials.
    private static async Task<IResult> AvatarAsync(string memberId, HttpContext context, CancellationToken cancellationToken)
    {
        var source = context.RequestServices.GetService<IAvatarSource>();
        var image = source is null ? null : await source.GetAsync(memberId, cancellationToken);
        if (image is null)
        {
            return Results.NotFound();
        }

        // Revalidation costs a 304 (the framework compares If-None-Match with the entity tag); the browser keeps the picture for 24 hours.
        context.Response.Headers.CacheControl = "private, max-age=86400";
        return Results.Bytes(image.Bytes, image.ContentType, entityTag: image.ETag is null ? null : new EntityTagHeaderValue(image.ETag));
    }

    // Demo and Live both register an IDiagnostics (Live since S13c); a host with none answers 404. The circuit counts are the web
    // host's own, so they are filled in here rather than by the port.
    private static IResult DiagnosticsJson(HttpContext context)
    {
        var source = context.RequestServices.GetService<IDiagnostics>();
        if (source is null)
        {
            return Results.NotFound();
        }

        var circuits = context.RequestServices.GetService<RealmCircuitHandler>();
        var snapshot = source.GetSnapshot() with { Circuits = new DiagnosticsSnapshot.CircuitCounts(circuits?.Open ?? 0, circuits?.Disconnected ?? 0) };
        context.Response.Headers.CacheControl = "no-store";
        return Results.Json(snapshot, DiagnosticsJsonOptions);
    }

    private static IResult TokensCss(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-cache";
        return Results.Bytes(
            Encoding.UTF8.GetBytes(RealmTokens.Css()),
            "text/css; charset=utf-8",
            entityTag: new EntityTagHeaderValue(RealmTokens.ETag()));
    }
}
