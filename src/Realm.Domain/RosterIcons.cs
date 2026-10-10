namespace Realm.Domain;

/// <summary>
/// The pictures the owner can give a roster entry (0.2.1, D117): <c>photo</c> (the picture the source has), <c>initial</c> (the first letter of the name) or one of
/// the glyphs below. The same tokens are stored in the <c>roster.icon</c> column. No token (null) means automatic.
/// </summary>
public static class RosterIcons
{
    /// <summary>The photo of Home Assistant or Life360.</summary>
    public const string Photo = "photo";

    /// <summary>The first letter of the name.</summary>
    public const string Initial = "initial";

    /// <summary>The prefix of a glyph token (<c>glyph:truck</c>).</summary>
    public const string GlyphPrefix = "glyph:";

    /// <summary>The glyph tokens in the order Settings offers them.</summary>
    public static readonly IReadOnlyList<string> Glyphs =
    [
        GlyphPrefix + "car", GlyphPrefix + "truck", GlyphPrefix + "person", GlyphPrefix + "pet", GlyphPrefix + "phone", GlyphPrefix + "tag",
    ];

    /// <summary>True for a token this version knows.</summary>
    public static bool IsValid(string? icon) => icon is Photo or Initial || (icon is not null && Glyphs.Contains(icon, StringComparer.Ordinal));

    /// <summary>The token of a glyph.</summary>
    public static string TokenOf(VehicleGlyph glyph) => GlyphPrefix + glyph switch
    {
        VehicleGlyph.Pickup => "truck",
        VehicleGlyph.Person => "person",
        VehicleGlyph.Pet => "pet",
        VehicleGlyph.Phone => "phone",
        VehicleGlyph.Tag => "tag",
        _ => "car",
    };

    /// <summary>The glyph of a <c>glyph:*</c> token; null for any other token.</summary>
    public static VehicleGlyph? GlyphOf(string? icon) => icon switch
    {
        GlyphPrefix + "car" => VehicleGlyph.Car,
        GlyphPrefix + "truck" => VehicleGlyph.Pickup,
        GlyphPrefix + "person" => VehicleGlyph.Person,
        GlyphPrefix + "pet" => VehicleGlyph.Pet,
        GlyphPrefix + "phone" => VehicleGlyph.Phone,
        GlyphPrefix + "tag" => VehicleGlyph.Tag,
        _ => null,
    };

    /// <summary>The words Settings and screen readers use for a token.</summary>
    public static string LabelOf(string icon) => icon switch
    {
        Photo => "Photo",
        Initial => "Initial",
        GlyphPrefix + "car" => "Car",
        GlyphPrefix + "truck" => "Truck",
        GlyphPrefix + "person" => "Person",
        GlyphPrefix + "pet" => "Pet",
        GlyphPrefix + "phone" => "Phone",
        GlyphPrefix + "tag" => "Tag",
        _ => icon,
    };
}

/// <summary>How an avatar is drawn.</summary>
public enum FaceMode
{
    /// <summary>The photo of the source (the initial or the glyph shows beneath it until it loads, or when it fails).</summary>
    Photo,

    /// <summary>The first letter of the name.</summary>
    Initial,

    /// <summary>A glyph.</summary>
    Glyph,
}

/// <summary>What one entry's avatar shows. The map pin, the lists and Settings all draw this one answer.</summary>
/// <param name="Mode">Photo, initial or glyph.</param>
/// <param name="Glyph">The glyph that shows (the one shown in <see cref="FaceMode.Glyph"/> mode, and under a photo of a tracker).</param>
public sealed record AvatarFace(FaceMode Mode, VehicleGlyph Glyph)
{
    /// <summary>Resolves the owner's token (null: automatic) for an entry that is drawn as a person's pin (<paramref name="asTracker"/> false) or a tracker's.</summary>
    /// <param name="icon">The roster entry's <see cref="RosterEntry.Icon"/>.</param>
    /// <param name="asTracker">The pin is a tracker's (the Trackers group) rather than a person's.</param>
    /// <param name="hasPhoto">The source has a picture.</param>
    public static AvatarFace Resolve(string? icon, bool asTracker, bool hasPhoto)
    {
        var fallbackGlyph = asTracker ? VehicleGlyph.Car : VehicleGlyph.Person;
        if (RosterIcons.GlyphOf(icon) is { } glyph)
        {
            return new AvatarFace(FaceMode.Glyph, glyph);
        }

        if (icon == RosterIcons.Initial)
        {
            return new AvatarFace(FaceMode.Initial, fallbackGlyph);
        }

        if (hasPhoto)
        {
            return new AvatarFace(FaceMode.Photo, fallbackGlyph);
        }

        return asTracker ? new AvatarFace(FaceMode.Glyph, VehicleGlyph.Car) : new AvatarFace(FaceMode.Initial, fallbackGlyph);
    }
}
