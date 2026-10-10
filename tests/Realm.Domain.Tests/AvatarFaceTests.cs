using Xunit;

namespace Realm.Domain.Tests;

// The one rule for what an avatar shows (0.2.1, D117): the map pin, the lists and Settings all draw the answer of AvatarFace.Resolve.
public class AvatarFaceTests
{
    [Fact]
    public void A_person_is_the_photo_when_the_source_has_one_else_the_initial()
    {
        Assert.Equal(FaceMode.Photo, AvatarFace.Resolve(null, asTracker: false, hasPhoto: true).Mode);
        Assert.Equal(FaceMode.Initial, AvatarFace.Resolve(null, asTracker: false, hasPhoto: false).Mode);
    }

    [Fact]
    public void A_tracker_is_the_photo_when_the_source_has_one_else_the_car()
    {
        Assert.Equal(FaceMode.Photo, AvatarFace.Resolve(null, asTracker: true, hasPhoto: true).Mode);
        var plain = AvatarFace.Resolve(null, asTracker: true, hasPhoto: false);
        Assert.Equal(FaceMode.Glyph, plain.Mode);
        Assert.Equal(VehicleGlyph.Car, plain.Glyph);
    }

    [Theory]
    [InlineData("glyph:car", VehicleGlyph.Car)]
    [InlineData("glyph:truck", VehicleGlyph.Pickup)]
    [InlineData("glyph:person", VehicleGlyph.Person)]
    [InlineData("glyph:pet", VehicleGlyph.Pet)]
    [InlineData("glyph:phone", VehicleGlyph.Phone)]
    [InlineData("glyph:tag", VehicleGlyph.Tag)]
    public void A_glyph_the_owner_chose_wins_over_the_photo(string token, VehicleGlyph glyph)
    {
        foreach (var asTracker in new[] { false, true })
        {
            var face = AvatarFace.Resolve(token, asTracker, hasPhoto: true);

            Assert.Equal(FaceMode.Glyph, face.Mode);
            Assert.Equal(glyph, face.Glyph);
            Assert.Equal(token, RosterIcons.TokenOf(glyph));
            Assert.Equal(glyph, RosterIcons.GlyphOf(token));
        }
    }

    [Fact]
    public void The_initial_the_owner_chose_wins_over_the_photo()
    {
        Assert.Equal(FaceMode.Initial, AvatarFace.Resolve(RosterIcons.Initial, asTracker: true, hasPhoto: true).Mode);
        Assert.Equal(FaceMode.Initial, AvatarFace.Resolve(RosterIcons.Initial, asTracker: false, hasPhoto: true).Mode);
    }

    [Fact]
    public void The_photo_the_owner_chose_falls_back_to_automatic_when_the_source_has_none()
    {
        Assert.Equal(FaceMode.Initial, AvatarFace.Resolve(RosterIcons.Photo, asTracker: false, hasPhoto: false).Mode);
        Assert.Equal(FaceMode.Glyph, AvatarFace.Resolve(RosterIcons.Photo, asTracker: true, hasPhoto: false).Mode);
    }

    [Fact]
    public void Only_the_known_tokens_are_valid()
    {
        Assert.All(RosterIcons.Glyphs.Append(RosterIcons.Photo).Append(RosterIcons.Initial), token => Assert.True(RosterIcons.IsValid(token)));
        Assert.False(RosterIcons.IsValid(null));
        Assert.False(RosterIcons.IsValid("glyph:rocket"));
        Assert.False(RosterIcons.IsValid(""));
    }

    [Fact]
    public void Every_glyph_has_paths_and_a_script_name()
    {
        foreach (var glyph in Enum.GetValues<VehicleGlyph>())
        {
            Assert.NotEmpty(GlyphPaths.Of(glyph));
            Assert.StartsWith("<path d=\"", GlyphPaths.Markup(glyph), StringComparison.Ordinal);
            Assert.NotEmpty(GlyphPaths.JsName(glyph));
        }
    }
}
