using Realm.Domain;

namespace Realm.Infrastructure.Ingestion;

/// <summary>
/// Creates the Live session of a circuit (03 section 2.2): a <see cref="LiveRealmSession"/> over the singleton <see cref="HaDataSource"/>, or, for a circuit
/// that asked for Demo, a session from the Demo factory. It is stateless and cheap, as prerendering creates and discards a session for every request.
/// </summary>
/// <remarks>
/// The shell decides that a circuit asked for Demo: only <c>?demo=1</c> with the option <c>allow_demo_param</c> gives a non-null parameter. This factory
/// therefore only routes a non-null parameter to the Demo factory, and it never creates a Demo session by itself.
/// </remarks>
public sealed class LiveRealmSessionFactory : IRealmSessionFactory
{
    private readonly HaDataSource _source;
    private readonly IRealmSessionFactory? _demo;

    /// <param name="source">The singleton every Live session adapts.</param>
    /// <param name="demo">The Demo factory for a circuit that asked for Demo; null when this host has none, and then every circuit is Live.</param>
    public LiveRealmSessionFactory(HaDataSource source, IRealmSessionFactory? demo = null)
    {
        _source = source;
        _demo = demo;
    }

    /// <inheritdoc />
    public IRealmSession Create(DemoUrlParams? demo) => demo is not null && _demo is not null ? _demo.Create(demo) : new LiveRealmSession(_source);
}
