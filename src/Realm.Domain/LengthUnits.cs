using System.Globalization;

namespace Realm.Domain;

/// <summary>The length units of Home Assistant's unit system (0.2.3, D122): metric shows metres and kilometres, imperial / US customary shows feet and miles.</summary>
public enum LengthUnits
{
    /// <summary>Metres and kilometres (Home Assistant's <c>unit_system.length</c> is <c>km</c>); also the fallback when Home Assistant does not say.</summary>
    Metric,

    /// <summary>Feet and miles (<c>unit_system.length</c> is <c>mi</c>).</summary>
    Imperial,
}

/// <summary>
/// The radius of a new place in the units of the owner's Home Assistant (0.2.3, D122). Home Assistant's own zone editor (frontend <c>dialog-zone-detail.ts</c> with the
/// location selector) starts at 100 m and takes whole numbers from 0 up; this page keeps the same start and step, shows them in <see cref="LengthUnits"/>, and caps the slider
/// (the editor's number box has no upper limit). The value that is sent to Home Assistant is always metres.
/// </summary>
public static class RadiusUnits
{
    private const double FeetPerMeter = 3.280839895;
    private const double FeetPerMile = 5280;

    /// <summary>The slider's step in the displayed unit (HA's number box: <c>step: 1</c>).</summary>
    public const double Step = 1;

    /// <summary>The slider's minimum in the displayed unit (HA's number box: <c>min: 0</c>).</summary>
    public const double Min = 0;

    /// <summary>The slider's maximum in the displayed unit: 2000 m, or 6500 ft (about 1981 m).</summary>
    public static double Max(LengthUnits units) => units == LengthUnits.Imperial ? 6500 : NewZone.MaxRadiusM;

    /// <summary>The unit of the slider: <c>m</c> or <c>ft</c>.</summary>
    public static string Unit(LengthUnits units) => units == LengthUnits.Imperial ? "ft" : "m";

    /// <summary>The units for the <c>unit_system.length</c> of Home Assistant's configuration: <c>mi</c> and <c>ft</c> are imperial, anything else (<c>km</c>, <c>m</c>, absent) is metric.</summary>
    public static LengthUnits FromHa(string? length) => length is "mi" or "ft" ? LengthUnits.Imperial : LengthUnits.Metric;

    /// <summary>Metres to the slider's value (whole metres or whole feet).</summary>
    public static double ToSlider(LengthUnits units, double meters) => Math.Round(units == LengthUnits.Imperial ? meters * FeetPerMeter : meters);

    /// <summary>The slider's value to metres, kept inside the range of <see cref="NewZone.ClampRadius"/>.</summary>
    public static double ToMeters(LengthUnits units, double sliderValue) => NewZone.ClampRadius(units == LengthUnits.Imperial ? sliderValue / FeetPerMeter : sliderValue);

    /// <summary>The radius as the panel writes it: "100 m" and "1.5 km" (metric), "328 ft" and "1.23 mi" (imperial); the larger unit from 1000 m, or from a mile.</summary>
    public static string Text(LengthUnits units, double meters)
    {
        var culture = CultureInfo.InvariantCulture;
        if (units == LengthUnits.Imperial)
        {
            var feet = Math.Round(meters * FeetPerMeter);
            return feet < FeetPerMile
                ? feet.ToString("0", culture) + " ft"
                : (feet / FeetPerMile).ToString("0.0#", culture) + " mi";
        }

        var metres = Math.Round(meters);
        return metres < 1000 ? metres.ToString("0", culture) + " m" : (meters / 1000).ToString("0.0#", culture) + " km";
    }
}
