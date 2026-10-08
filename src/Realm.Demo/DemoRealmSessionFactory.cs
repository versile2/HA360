using Realm.Domain;

namespace Realm.Demo;

/// <summary>
/// Creates one Demo session per circuit, each with its own <see cref="DemoTimeProvider"/> (03 section 2.2). Creating a
/// session is cheap: the fixture is built when the session's snapshot is first read (03 section 3.1).
/// </summary>
public sealed class DemoRealmSessionFactory : IRealmSessionFactory
{
    /// <summary>
    /// A clock frozen at the anchor instant, or at <see cref="DemoUrlParams.Now"/>; under the ha-down variant it
    /// advances in real time from there. The variants are parsed by <see cref="DemoVariants.Parse"/>. Null means the
    /// mode's default, which in a Demo run is the plain fixture.
    /// </summary>
    public IRealmSession Create(DemoUrlParams? demo)
    {
        var variants = DemoVariants.Parse(demo?.Variants);
        var start = demo?.Now ?? DemoDataSource.Anchor;
        return new DemoRealmSession(new DemoDataSource(new DemoTimeProvider(start, variants.HaDown), variants, variants.FullCast ? DemoRoster.EveryoneOnTheMap() : null));
    }
}
