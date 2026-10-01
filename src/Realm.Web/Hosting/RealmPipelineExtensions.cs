namespace Realm.Web.Hosting;

public static class RealmPipelineExtensions
{
    /// <summary>
    /// The middleware of 03 section 5.2, in contract order (D41: no ingress gate and no security headers). The endpoints, the static
    /// assets and the Razor components are mapped by the caller, because <c>MapStaticAssets</c> needs the static-web-assets manifest.
    /// </summary>
    public static IApplicationBuilder UseRealmPipeline(this IApplicationBuilder app)
    {
        app.UseExceptionHandler("/error-plain");   // generic 500 text, details only in the log
        app.UseForwardedHeaders();                 // XForwardedProto and XForwardedHost, configured by AddRealmWeb
        app.UseMiddleware<IngressPathBase>();      // PathBase from X-Ingress-Path, Path untouched
        app.UseAntiforgery();                      // required by Razor components endpoints
        return app;
    }
}
