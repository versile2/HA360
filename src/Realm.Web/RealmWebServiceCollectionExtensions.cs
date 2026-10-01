using Microsoft.AspNetCore.HttpOverrides;
using MudBlazor.Services;

namespace Realm.Web;

public static class RealmWebServiceCollectionExtensions
{
    /// <summary>Razor components with the Interactive Server circuit settings of 03 section 5.6, the forwarded-headers options of 5.2 and the MudBlazor services.</summary>
    public static IServiceCollection AddRealmWeb(this IServiceCollection services, RuntimeOptions runtime)
    {
        services.AddRazorComponents().AddInteractiveServerComponents(options =>
        {
            options.DisconnectedCircuitRetentionPeriod = TimeSpan.FromMinutes(45);   // default 3 min: a phone in a pocket
            options.DisconnectedCircuitMaxRetained = 20;                              // default 100; household scale
            options.JSInteropDefaultCallTimeout = TimeSpan.FromSeconds(30);
            options.DetailedErrors = runtime.DetailedErrors;
        });

        // Ingress sits behind HA's proxy chain: the scheme and host the app sees are the external ones. Nothing but Ingress is
        // meant to reach the port (D41), so no proxy list is kept. XForwardedFor stays off. KnownNetworks is obsolete (ASPDEPR005).
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();
        });

        // The MudBlazor services (dialog, snackbar, popover, resize and the rest) that MainLayout's providers and every Mud component need.
        services.AddMudServices();

        return services;
    }
}
