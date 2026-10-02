using Microsoft.Extensions.DependencyInjection;
using Realm.Domain;

namespace Realm.Demo;

public static class RealmDemoServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Demo session factory (03 section 2.1). Until the Live side exists it is registered unconditionally (R3-027).
    /// </summary>
    /// <remarks>
    /// The factory is a singleton (CR1-010): it is stateless, so the lifetime decides nothing about sessions. "One session per circuit"
    /// (03 sections 2.2 and 3.1) is kept by <c>RealmShell</c>, which asks it for one session when the circuit's layout initialises and
    /// disposes that session with the circuit. The Live factory of S13b must be registered with the same lifetime.
    /// </remarks>
    public static IServiceCollection AddRealmDemo(this IServiceCollection services)
    {
        services.AddSingleton<IRealmSessionFactory, DemoRealmSessionFactory>();
        services.AddSingleton<IDiagnostics, DemoDiagnostics>();   // canned diagnostics.json (03 section 2.11); the Live side registers its own (S13c)
        return services;
    }
}
