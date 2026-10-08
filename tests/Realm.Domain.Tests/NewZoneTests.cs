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
    [InlineData(24)]
    [InlineData(2001)]
    [InlineData(double.NaN)]
    public void ARadiusOutsideTheSlider_IsAProblem(double radius) => Assert.NotNull((Good with { RadiusM = radius }).Problem());

    [Theory]
    [InlineData(25)]
    [InlineData(2000)]
    public void TheEdgesOfTheSlider_AreAllowed(double radius) => Assert.Null((Good with { RadiusM = radius }).Problem());

    [Theory]
    [InlineData(0, 25)]
    [InlineData(100, 100)]
    [InlineData(5000, 2000)]
    [InlineData(double.NaN, 100)]
    public void ClampRadius_KeepsTheValueInRange(double given, double expected) => Assert.Equal(expected, NewZone.ClampRadius(given));

    [Fact]
    public void TheSlider_Is25mTo2kmIn25mSteps_From100m()
    {
        Assert.Equal(25, NewZone.MinRadiusM);
        Assert.Equal(2000, NewZone.MaxRadiusM);
        Assert.Equal(25, NewZone.RadiusStepM);
        Assert.Equal(100, NewZone.DefaultRadiusM);
    }

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
