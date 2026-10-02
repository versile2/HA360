using Xunit;

namespace Realm.Domain.Tests;

// The names that other slices share as constants (D52), and the MemberVm field added by D51.
public class ConstantsTests
{
    // The four connection names of 02 section 1.8.
    [Fact]
    public void Connection_names_are_the_four_of_the_spec()
    {
        Assert.Equal("HomeAssistant", ConnectionNames.HomeAssistant);
        Assert.Equal("Life360Trackers", ConnectionNames.Life360Trackers);
        Assert.Equal("FordPass", ConnectionNames.FordPass);
        Assert.Equal("VehiclePlaceholder", ConnectionNames.VehiclePlaceholder);
    }

    // The keys of the four event kinds of 02 section 6.6.
    [Fact]
    public void Event_keys_are_the_four_of_the_spec()
    {
        Assert.Equal("speeding", EventKeys.Speeding);
        Assert.Equal("phone", EventKeys.Phone);
        Assert.Equal("accel", EventKeys.Accel);
        Assert.Equal("braking", EventKeys.Braking);
    }

    private static MemberVm Member(string? staticLabel) => new(
        Id: "prince",
        DisplayName: "Elio",
        LoreTitle: "Prince of the Peaks",
        AvatarUrl: null,
        Color: "#7EE0A5",
        Kind: MemberKind.Static,
        Lat: 38.8339,
        Lon: -92.8214,
        AccuracyM: null,
        BatteryPct: null,
        Charging: null,
        BatteryAsOfUtc: null,
        IsDriving: false,
        SpeedMps: null,
        Street: null,
        City: null,
        Region: null,
        FullAddress: null,
        PlaceId: null,
        SinceUtc: null,
        LastUpdateUtc: null,
        SortOrder: 4,
        Freshness: Freshness.Static,
        StaticLabel: staticLabel);

    // D51: the label of a static pin is the last positional parameter, and a live member simply leaves it null.
    [Fact]
    public void Static_label_is_the_last_parameter_of_member_vm()
    {
        var parameters = typeof(MemberVm).GetConstructors().Single().GetParameters();

        Assert.Equal("StaticLabel", parameters[^1].Name);
        Assert.Equal(typeof(string), parameters[^1].ParameterType);
        Assert.Equal("Home · Highmeadow", Member("Home · Highmeadow").StaticLabel);
        Assert.Null(Member(null).StaticLabel);
    }
}
