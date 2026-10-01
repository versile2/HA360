using System.Text;
using Microsoft.Net.Http.Headers;
using Realm.Web.Theme;

namespace Realm.Web.Hosting;

public static class RealmEndpointRouteBuilderExtensions
{
    /// <summary>The plain endpoints of 03 section 3.3 that exist so far. Route templates carry no leading slash.</summary>
    public static IEndpointRouteBuilder MapRealmEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Liveness only (03 section 2.14): it must never depend on HA, or the Supervisor watchdog would restart the add-on whenever HA restarts.
        endpoints.MapGet("healthz", () => Results.Text("ok"));

        // The exception handler re-runs the request here; the 500 status is already set, so this writes only the generic text.
        endpoints.MapGet("error-plain", () => Results.Text("Something went wrong in the Realm."));

        // The theme tokens are an endpoint, not a file (03 sections 3.8 and 5.3): no-cache plus a strong ETag, so a revalidation costs a 304.
        endpoints.MapGet("css/tokens.css", TokensCss);

        return endpoints;
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
