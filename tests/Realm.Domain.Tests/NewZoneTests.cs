using Xunit;

namespace Realm.Domain.Tests;

// Adding a place (0.2.2, D119): what a new zone may hold, the icons the picker offers and the sentences for Home Assistant's refusals.
public class NewZoneTests
{
    private static readonly NewZone Good = new("Grandma's", 33.1, -84.5, 100, "mdi:home-heart");

    [Fact]
    public void AGoodZone_HasNoProblem() => Assert.Null(Good.Problem());

    [Theory]
    [InlineData("", "Give the place a name.")]
    [InlineData("   ", "Give the place a name.")]
    public void ABlankName_IsAProblem(string name, string sentence) => Assert.Equal(sentence, (Good with { Name = name }).Problem());

    [Fact]
    public void ATooLongName_IsAProblem() => Assert.Contains("at most 64", (Good with { Name = new string('x', 65) }).Problem());

    [Theory]
    [InlineData(91, 0)]
    [InlineData(-91, 0)]
    [InlineData(0, 181)]
    [InlineData(0, -181)]
    [InlineData(double.NaN, 0)]
    public void APositionOffTheGlobe_IsAProblem(double lat, double lon) => Assert.Equal("The position is not on the map.", (Good with { Latitude = lat, Longitude = lon }).Problem());

    [Theory]
    [InlineData(-1)]
    [InlineData(2001)]
    [InlineData(double.NaN)]
    public void ARadiusOutsideTheSlider_IsAProblem(double radius) => Assert.NotNull((Good with { RadiusM = radius }).Problem());

    [Theory]
    [InlineData(0)]
    [InlineData(2000)]
    public void TheEdgesOfTheSlider_AreAllowed(double radius) => Assert.Null((Good with { RadiusM = radius }).Problem());

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(100, 100)]
    [InlineData(5000, 2000)]
    [InlineData(double.NaN, 100)]
    public void ClampRadius_KeepsTheValueInRange(double given, double expected) => Assert.Equal(expected, NewZone.ClampRadius(given));

    [Fact]
    public void TheSlider_MatchesHomeAssistantsEditor_0To2kmInWholeUnits_From100m()
    {
        // Home Assistant: DEFAULT_RADIUS = 100 (core const.py); location selector number box min 0, step 1 (frontend ha-selector-location.ts); 2000 m is this app's cap.
        Assert.Equal(0, NewZone.MinRadiusM);
        Assert.Equal(2000, NewZone.MaxRadiusM);
        Assert.Equal(1, NewZone.RadiusStep);
        Assert.Equal(100, NewZone.DefaultRadiusM);
    }

    [Theory]
    [InlineData("km", LengthUnits.Metric)]
    [InlineData("m", LengthUnits.Metric)]
    [InlineData(null, LengthUnits.Metric)]
    [InlineData("", LengthUnits.Metric)]
    [InlineData("mi", LengthUnits.Imperial)]
    [InlineData("ft", LengthUnits.Imperial)]
    public void TheHomeAssistantLengthUnit_PicksTheUnitSystem(string? length, LengthUnits expected) => Assert.Equal(expected, RadiusUnits.FromHa(length));

    [Fact]
    public void TheSliderLimits_FollowTheUnitSystem()
    {
        Assert.Equal(0, RadiusUnits.Min);
        Assert.Equal(1, RadiusUnits.Step);
        Assert.Equal(2000, RadiusUnits.Max(LengthUnits.Metric));
        Assert.Equal(6500, RadiusUnits.Max(LengthUnits.Imperial));
        Assert.True(RadiusUnits.ToMeters(LengthUnits.Imperial, 6500) <= NewZone.MaxRadiusM);
        Assert.Equal("m", RadiusUnits.Unit(LengthUnits.Metric));
        Assert.Equal("ft", RadiusUnits.Unit(LengthUnits.Imperial));
    }

    [Theory]
    [InlineData(LengthUnits.Metric, 100, 100)]
    [InlineData(LengthUnits.Imperial, 100, 328)]
    [InlineData(LengthUnits.Imperial, 0, 0)]
    [InlineData(LengthUnits.Imperial, 1980, 6496)]
    public void MetresToTheSlider_AreWholeUnits(LengthUnits units, double meters, double slider) => Assert.Equal(slider, RadiusUnits.ToSlider(units, meters));

    [Theory]
    [InlineData(LengthUnits.Metric, 450, 450)]
    [InlineData(LengthUnits.Metric, 99999, 2000)]
    [InlineData(LengthUnits.Metric, -3, 0)]
    [InlineData(LengthUnits.Imperial, 328.084, 100)]
    [InlineData(LengthUnits.Imperial, 99999, 2000)]
    public void TheSlider_BecomesMetresClampedToTheRange(LengthUnits units, double slider, double meters) => Assert.Equal(meters, RadiusUnits.ToMeters(units, slider), 3);

    [Theory]
    [InlineData(LengthUnits.Metric, 0, "0 m")]
    [InlineData(LengthUnits.Metric, 100, "100 m")]
    [InlineData(LengthUnits.Metric, 999, "999 m")]
    [InlineData(LengthUnits.Metric, 1000, "1.0 km")]
    [InlineData(LengthUnits.Metric, 1500, "1.5 km")]
    [InlineData(LengthUnits.Metric, 2000, "2.0 km")]
    [InlineData(LengthUnits.Imperial, 25, "82 ft")]
    [InlineData(LengthUnits.Imperial, 100, "328 ft")]
    [InlineData(LengthUnits.Imperial, 1000, "3281 ft")]
    [InlineData(LengthUnits.Imperial, 1609.344, "1.0 mi")]
    [InlineData(LengthUnits.Imperial, 2000, "1.24 mi")]
    public void TheRadiusText_IsOneUnit_InTheUnitSystem(LengthUnits units, double meters, string expected) => Assert.Equal(expected, RadiusUnits.Text(units, meters));

    [Fact]
    public void EveryPickableKind_HasAnMdiIconThatMapsBack()
    {
        foreach (var kind in PlaceKindIcons.Pickable)
        {
            var icon = PlaceKindIcons.HaIcon(kind);
            Assert.StartsWith("mdi:", icon, StringComparison.Ordinal);
            Assert.Equal(kind, PlaceKindIcons.KindOf(icon));
            Assert.False(string.IsNullOrWhiteSpace(PlaceKindIcons.Label(kind)));
        }

        Assert.Equal(PlaceKind.Other, PlaceKindIcons.KindOf("mdi:castle"));
        Assert.Equal(PlaceKind.Other, PlaceKindIcons.KindOf(null));
        Assert.Equal(PlaceKindIcons.Pickable.Count, PlaceKindIcons.Pickable.Distinct().Count());
    }

    [Theory]
    [InlineData("unauthorized", "administrator")]
    [InlineData("unknown_command", "cannot add a place")]
    [InlineData("invalid_format", "did not accept")]
    [InlineData("not_connected", "not connected")]
    [InlineData("connection_lost", "not connected")]
    [InlineData("timeout", "not connected")]
    [InlineData("weird", "(weird)")]
    [InlineData(null, "refused")]
    public void EachHomeAssistantCode_HasASentence(string? code, string part) => Assert.Contains(part, PlaceCreateResult.Describe(code));

    [Fact]
    public async Task TheUnavailableEditor_AlwaysFails()
    {
        var result = await UnavailablePlaceEditor.Instance.CreateAsync(Good);

        Assert.False(result.Ok);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
        Assert.True(PlaceCreateResult.Success.Ok);
        Assert.Null(PlaceCreateResult.Success.Message);
    }
}
