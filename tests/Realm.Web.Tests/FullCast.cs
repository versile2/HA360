using Realm.Demo;
using Realm.Domain;

namespace Realm.Web.Tests;

/// <summary>
/// The Demo with all seven roles of the cast on the map (the prince under People and the hatchback under Vehicles), which is the fixture that most
/// of these tests describe (02 section 9.3). The Demo itself starts with those two under Not tracked (D113); <see cref="Session"/> bypasses that.
/// </summary>
internal static class FullCast
{
    public static IRealmSession Session(DemoUrlParams? demo = null)
    {
        var variants = DemoVariants.Parse(demo?.Variants);
        var start = demo?.Now ?? DemoDataSource.Anchor;
        return new DemoRealmSession(new DemoDataSource(new DemoTimeProvider(start, variants.HaDown), variants, DemoRoster.EveryoneOnTheMap()));
    }

    public static RealmSnapshot Snapshot() => Session().Current;
}
