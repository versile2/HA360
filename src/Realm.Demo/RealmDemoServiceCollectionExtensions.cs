using Microsoft.Extensions.DependencyInjection;
using Realm.Domain;

namespace Realm.Demo;

public static class RealmDemoServiceCollectionExtensions
{
    /// <summary>Registers the Demo session factory (03 section 2.1). Until the Live side exists it is registered unconditionally (R3-027).</summary>
    public static IServiceCollection AddRealmDemo(this IServiceCollection services)
    {
        services.AddScoped<IRealmSessionFactory, DemoRealmSessionFactory>();
        return services;
    }
}
