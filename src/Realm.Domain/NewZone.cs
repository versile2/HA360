namespace Realm.Domain;

/// <summary>
/// A place the owner is adding (0.2.2, D119): what the Home Assistant command <c>zone/create</c> is given. The radius is kept between <see cref="MinRadiusM"/> and
/// <see cref="MaxRadiusM"/> by the placement panel; <see cref="Problem"/> says what is wrong with a value that came from somewhere else.
/// </summary>
/// <param name="Name">The zone's name (trimmed, 1 to <see cref="MaxNameLength"/> characters).</param>
/// <param name="Latitude">Degrees north, -90 to 90.</param>
/// <param name="Longitude">Degrees east, -180 to 180.</param>
/// <param name="RadiusM">Metres, <see cref="MinRadiusM"/> to <see cref="MaxRadiusM"/>.</param>
/// <param name="Icon">A Home Assistant icon name such as <c>mdi:home</c> (see <see cref="PlaceKindIcons.HaIcon"/>).</param>
public sealed record NewZone(string Name, double Latitude, double Longitude, double RadiusM, string Icon)
{
    /// <summary>The smallest radius, in metres: Home Assistant's zone editor allows 0 (0.2.3, D122).</summary>
    public const double MinRadiusM = 0;

    /// <summary>The largest radius the slider offers, in metres (2 km; Home Assistant's editor has no upper limit).</summary>
    public const double MaxRadiusM = 2000;

    /// <summary>The radius the placement starts with, in metres: Home Assistant's own default (<c>DEFAULT_RADIUS = 100</c>, and the editor's new zone).</summary>
    public const double DefaultRadiusM = 100;

    /// <summary>The slider's step in the displayed unit (whole metres or whole feet); see <see cref="RadiusUnits.Step"/>.</summary>
    public const double RadiusStep = RadiusUnits.Step;

    /// <summary>The longest name accepted.</summary>
    public const int MaxNameLength = 64;

    /// <summary>Keeps a radius inside the range the slider offers.</summary>
    public static double ClampRadius(double radiusM) => double.IsNaN(radiusM) ? DefaultRadiusM : Math.Clamp(radiusM, MinRadiusM, MaxRadiusM);

    /// <summary>Null when the values can be sent to Home Assistant, otherwise one sentence that says what is wrong.</summary>
    public string? Problem()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            return "Give the place a name.";
        }

        if (Name.Trim().Length > MaxNameLength)
        {
            return $"The name can have at most {MaxNameLength} characters.";
        }

        if (double.IsNaN(Latitude) || double.IsNaN(Longitude) || Latitude is < -90 or > 90 || Longitude is < -180 or > 180)
        {
            return "The position is not on the map.";
        }

        if (double.IsNaN(RadiusM) || RadiusM is < MinRadiusM or > MaxRadiusM)
        {
            return $"The radius must be between {MinRadiusM:0} m and {MaxRadiusM:0} m.";
        }

        return string.IsNullOrWhiteSpace(Icon) ? "Choose an icon." : null;
    }
}
