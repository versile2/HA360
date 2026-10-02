using Microsoft.AspNetCore.Components.Server.Circuits;
using Realm.Demo;
using Realm.Infrastructure.Hosting;
using Realm.Web.Hosting;
using Realm.Web.State;

namespace Realm.Web;

/// <summary>
/// The one place that composes the application's services. <c>Program.cs</c> calls it, and so does the Kestrel test host
/// (<c>RealmTestHost</c>), so a service added here cannot be missing from the tests that render the real pages.
/// </summary>
public static class RealmAppServiceCollectionExtensions
{
    /// <param name="configuration">
    /// The host's configuration, which the Live services read (<c>Realm:Ha:*</c>, <c>Realm:Db</c>, the token and the mapped options) and the host settings
    /// read for the Data Protection directory. Demo mode reads nothing else from it; Live mode cannot be composed without it.
    /// </param>
    public static IServiceCollection AddRealmApp(this IServiceCollection services, RuntimeOptions runtime, IConfiguration? configuration = null)
    {
        services.AddSingleton(runtime);   // RealmShell reads the mode to decide whether the Demo-only URL parameters apply (03 section 2.1)
        services.AddRealmWeb(runtime);
        services.AddSingleton<RealmCircuitHandler>();   // counts circuits for diagnostics.json (03 section 5.6)
        services.AddSingleton<CircuitHandler>(provider => provider.GetRequiredService<RealmCircuitHandler>());
        services.AddScoped<DevicePrefs>();   // the four device preferences of 01 section 7.9, one set per circuit (03 section 3.6)
        if (runtime.Mode == RealmMode.Live)
        {
            ArgumentNullException.ThrowIfNull(configuration);
            services.AddRealmLive(configuration, new DemoRealmSessionFactory());   // Demo sessions only for a circuit that asked (?demo=1 with allow_demo_param)
        }
        else
        {
            services.AddRealmDemo();
        }

        // After the mode's services: AddRealmData must come before any other hosted service (03 section 2.4), and this registers one (the Data Protection report).
        services.AddRealmHosting(runtime, configuration);   // shutdown budget, HTTP/1.1, Data Protection keys (03 sections 2.13, 5.1, 5.7)
        return services;
    }
}
