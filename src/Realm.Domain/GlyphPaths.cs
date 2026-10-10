namespace Realm.Domain;

/// <summary>
/// The SVG paths (24 x 24 box) of the glyphs a pin can show: the one source for Settings, the sheet rows and the detail. <c>wwwroot/js/realmMap.js</c> draws the map pins
/// from its own copy of the same strings; a test keeps the two equal, so a pin and its Settings avatar are the same picture.
/// </summary>
public static class GlyphPaths
{
    private static readonly Dictionary<VehicleGlyph, string[]> Paths = new()
    {
        [VehicleGlyph.Car] = ["M18.92 6.01C18.72 5.42 18.16 5 17.5 5h-11c-.66 0-1.21.42-1.42 1.01L3 12v8c0 .55.45 1 1 1h1c.55 0 1-.45 1-1v-1h12v1c0 .55.45 1 1 1h1c.55 0 1-.45 1-1v-8l-2.08-5.99zM6.5 16c-.83 0-1.5-.67-1.5-1.5S5.67 13 6.5 13s1.5.67 1.5 1.5S7.33 16 6.5 16zm11 0c-.83 0-1.5-.67-1.5-1.5s.67-1.5 1.5-1.5 1.5.67 1.5 1.5-.67 1.5-1.5 1.5zM5 11l1.5-4.5h11L19 11H5z"],
        [VehicleGlyph.Pickup] =
        [
            "M2 9.5h9.5v-3H16l3.3 4H21.2c.44 0 .8.36.8.8V16H2z",
            "M13 8v2.2h4.6L15.8 8z",
            "M6.2 14.2a2.4 2.4 0 0 1 0 4.8 2.4 2.4 0 0 1 0-4.8z",
            "M17.6 14.2a2.4 2.4 0 0 1 0 4.8 2.4 2.4 0 0 1 0-4.8z",
        ],
        [VehicleGlyph.Person] = ["M12 12c2.21 0 4-1.79 4-4s-1.79-4-4-4-4 1.79-4 4 1.79 4 4 4zm0 2c-2.67 0-8 1.34-8 4v2h16v-2c0-2.66-5.33-4-8-4z"],
        [VehicleGlyph.Pet] =
        [
            "M12 12.5c-2.2 0-5 2.1-5 4.4 0 1.6 1.2 2.6 2.6 2.6.9 0 1.6-.3 2.4-.3s1.5.3 2.4.3c1.4 0 2.6-1 2.6-2.6 0-2.3-2.8-4.4-5-4.4z",
            "M6.3 8.3a1.9 1.9 0 1 0 0 3.8 1.9 1.9 0 0 0 0-3.8z",
            "M9.6 4.2a2 2 0 1 0 0 4 2 2 0 0 0 0-4z",
            "M14.4 4.2a2 2 0 1 0 0 4 2 2 0 0 0 0-4z",
            "M17.7 8.3a1.9 1.9 0 1 0 0 3.8 1.9 1.9 0 0 0 0-3.8z",
        ],
        [VehicleGlyph.Phone] = ["M17 1.01L7 1c-1.1 0-2 .9-2 2v18c0 1.1.9 2 2 2h10c1.1 0 2-.9 2-2V3c0-1.1-.9-1.99-2-1.99zM17 19H7V5h10v14z"],
        [VehicleGlyph.Tag] = ["M21.41 11.41l-8.83-8.83c-.37-.37-.88-.58-1.41-.58H4c-1.1 0-2 .9-2 2v7.17c0 .53.21 1.04.59 1.41l8.83 8.83c.78.78 2.05.78 2.83 0l7.17-7.17c.78-.78.78-2.04-.01-2.83zM6.5 8C5.67 8 5 7.33 5 6.5S5.67 5 6.5 5 8 5.67 8 6.5 7.33 8 6.5 8z"],
    };

    /// <summary>The path data (<c>d</c> attributes) of a glyph.</summary>
    public static IReadOnlyList<string> Of(VehicleGlyph glyph) => Paths[glyph];

    /// <summary>The glyph as SVG inner markup (<c>&lt;path d="…"/&gt;</c> elements), which is what a MudIcon takes as its icon.</summary>
    public static string Markup(VehicleGlyph glyph) => string.Concat(Paths[glyph].Select(d => "<path d=\"" + d + "\"/>"));

    /// <summary>The name <c>realmMap.js</c> knows the glyph by.</summary>
    public static string JsName(VehicleGlyph glyph) => glyph switch
    {
        VehicleGlyph.Pickup => "pickup",
        VehicleGlyph.Person => "person",
        VehicleGlyph.Pet => "pet",
        VehicleGlyph.Phone => "phone",
        VehicleGlyph.Tag => "tag",
        _ => "car",
    };
}
