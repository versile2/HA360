using Realm.Domain;

namespace Realm.Demo;

/// <summary>
/// Creates one Demo session per circuit, each with its own <see cref="DemoTimeProvider"/> (03 section 2.2). Creating a
/// session is cheap: the fixture is built when the session's snapshot is first read (03 section 3.1).
/// </summary>
public sealed class DemoRealmSessionFactory : IRealmSessionFactory
{
    // The only variant that changes the clock; the others are pure transforms of the snapshot (S4b).
    private const string HaDownVariant = "ha-down";

    /// <summary>
    /// A clock frozen at the anchor instant, or at <see cref="DemoUrlParams.Now"/>; under the ha-down variant it
    /// advances in real time from there. Null means the mode's default, which in a Demo run is the plain fixture.
    /// </summary>
    public IRealmSession Create(DemoUrlParams? demo)
    {
        var start = demo?.Now ?? DemoDataSource.Anchor;
        var advancing = demo is not null && demo.Variants.Contains(HaDownVariant, StringComparer.Ordinal);
        return new DemoRealmSession(new DemoDataSource(new DemoTimeProvider(start, advancing)));
    }
}
