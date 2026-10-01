using System.Reflection;
using Xunit;

namespace Realm.Demo.Tests;

// The variants of 02 section 9.5, parsed in one place: nine names, each switching one aspect, combined with commas and
// applied left to right; a name that is not one of the nine is ignored. What each variant does to the data is asserted
// through the session in DemoDataTests.
public class DemoVariantsTests
{
    private static readonly string[] NineNames =
    [
        "all-sources",
        "phone-unavailable",
        "life360-down",
        "ha-down",
        "poor-accuracy",
        "no-fix",
        "all-near",
        "empty-week",
        "fresh-install",
    ];

    // The flags of a parsed result that are on.
    private static string[] FlagsOn(DemoVariants variants) =>
        [.. typeof(DemoVariants).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.PropertyType == typeof(bool) && (bool)property.GetValue(variants)!)
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)];

    [Fact]
    public void The_variant_names_are_exactly_the_nine_of_the_spec()
    {
        Assert.Equal(9, DemoVariants.Names.Count);
        Assert.Equal(NineNames.Order(StringComparer.Ordinal), DemoVariants.Names.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void No_variant_is_the_default_fixture()
    {
        Assert.Empty(FlagsOn(DemoVariants.None));
        Assert.Equal(DemoVariants.None, DemoVariants.Parse(null));
        Assert.Equal(DemoVariants.None, DemoVariants.Parse([]));
    }

    [Theory]
    [InlineData("all-sources", "AllSources")]
    [InlineData("phone-unavailable", "PhoneUnavailable")]
    [InlineData("life360-down", "Life360Down")]
    [InlineData("ha-down", "HaDown")]
    [InlineData("poor-accuracy", "PoorAccuracy")]
    [InlineData("no-fix", "NoFix")]
    [InlineData("all-near", "AllNear")]
    [InlineData("empty-week", "EmptyWeek")]
    [InlineData("fresh-install", "FreshInstall")]
    public void Each_name_switches_exactly_its_own_aspect(string name, string flag)
    {
        Assert.Equal(flag, Assert.Single(FlagsOn(DemoVariants.Parse([name]))));
    }

    // Unknown names, empty entries and names of another case are ignored; the known ones around them still apply.
    [Fact]
    public void Unknown_names_are_ignored()
    {
        Assert.Equal(DemoVariants.None, DemoVariants.Parse(["bogus"]));
        Assert.Equal(DemoVariants.None, DemoVariants.Parse(["", "ALL-SOURCES", "all_sources"]));
        Assert.Equal(DemoVariants.Parse(["all-sources"]), DemoVariants.Parse(["bogus", "all-sources", "", "nope"]));
    }

    // A caller may hand over the names split or as the one comma-separated value of ?variant=.
    [Theory]
    [InlineData("all-sources,phone-unavailable")]
    [InlineData("all-sources, phone-unavailable")]
    [InlineData(" all-sources ,phone-unavailable,")]
    public void Names_compose_with_commas_inside_one_value(string value)
    {
        var composed = DemoVariants.Parse([value]);

        Assert.Equal(DemoVariants.Parse(["all-sources", "phone-unavailable"]), composed);
        Assert.Equal(new[] { "AllSources", "PhoneUnavailable" }, FlagsOn(composed));
    }

    // No variant undoes another, so applying left to right gives the same result in either order.
    [Fact]
    public void Any_two_variants_compose_in_either_order()
    {
        foreach (var first in NineNames)
        {
            foreach (var second in NineNames)
            {
                Assert.Equal(DemoVariants.Parse([first, second]), DemoVariants.Parse([second, first]));
                Assert.Equal(first == second ? 1 : 2, FlagsOn(DemoVariants.Parse([first, second])).Length);
            }
        }
    }

    [Fact]
    public void All_nine_names_together_switch_all_nine_aspects()
    {
        Assert.Equal(9, FlagsOn(DemoVariants.Parse(NineNames)).Length);
        Assert.Equal(DemoVariants.Parse(NineNames), DemoVariants.Parse([string.Join(",", NineNames)]));
    }
}
