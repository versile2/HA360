using Realm.Domain;
using Xunit;

namespace Realm.Web.Tests;

/// <summary>
/// A pin and its Settings avatar are the same picture (0.2.1, D117): every path of <see cref="GlyphPaths"/> is also in the ICONS table of realmMap.js, under the glyph's name, in the same order.
/// </summary>
public sealed class GlyphPathsParityTests
{
    [Theory]
    [InlineData(VehicleGlyph.Car)]
    [InlineData(VehicleGlyph.Pickup)]
    [InlineData(VehicleGlyph.Person)]
    [InlineData(VehicleGlyph.Pet)]
    [InlineData(VehicleGlyph.Phone)]
    [InlineData(VehicleGlyph.Tag)]
    public void EveryGlyph_HasTheSamePathsInTheMapScript(VehicleGlyph glyph)
    {
        var script = File.ReadAllText(Path.Combine(PayloadContractTests.FindRepositoryRoot(), "src", "Realm.Web", "wwwroot", "js", "realmMap.js"));
        var name = GlyphPaths.JsName(glyph);
        var start = script.IndexOf($"\n  {name}: [", StringComparison.Ordinal);
        Assert.True(start >= 0, $"ICONS has no '{name}'");
        var end = script.IndexOf('\n', start + 1);
        var entry = script[start..end];

        var expected = string.Join(", ", GlyphPaths.Of(glyph).Select(d => "'" + d + "'"));
        Assert.Contains(expected, entry, StringComparison.Ordinal);
    }
}
