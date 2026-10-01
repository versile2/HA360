using Microsoft.Extensions.DependencyInjection;
using Realm.Demo;
using Realm.Domain;
using Xunit;

namespace Realm.Web.Tests;

/// <summary>
/// CR1-010: the session factory is a singleton and the session is per circuit, because RealmShell asks for one session per circuit
/// (03 sections 2.2 and 3.1). The factory must therefore be stateless: two asks give two independent sessions.
/// </summary>
public class RealmSessionFactoryLifetimeTests
{
    [Fact]
    public void AddRealmDemo_RegistersTheFactoryAsASingleton()
    {
        var services = new ServiceCollection().AddRealmDemo();

        var descriptor = Assert.Single(services, service => service.ServiceType == typeof(IRealmSessionFactory));

        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
        Assert.Equal(typeof(DemoRealmSessionFactory), descriptor.ImplementationType);
    }

    [Fact]
    public async Task EveryCircuitScope_SharesTheFactory_ButGetsItsOwnSession()
    {
        using var provider = new ServiceCollection().AddRealmDemo().BuildServiceProvider();
        using var firstCircuit = provider.CreateScope();
        using var secondCircuit = provider.CreateScope();

        var factory = firstCircuit.ServiceProvider.GetRequiredService<IRealmSessionFactory>();

        Assert.Same(factory, secondCircuit.ServiceProvider.GetRequiredService<IRealmSessionFactory>());
        await using var first = factory.Create(null);
        await using var second = factory.Create(null);
        Assert.NotSame(first, second);
        Assert.NotSame(first.Time, second.Time);   // each session owns its clock
    }
}
