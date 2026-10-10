namespace Realm.Web.Map;

/// <summary>
/// The thresholds the payloads need, named after the add-on options of 02 section 3.4 (<c>Ui:*</c>) with their defaults (01 section 4.1).
/// The options loader is S12's; until it binds them the defaults apply.
/// </summary>
/// <param name="PoorAccuracyMeters"><c>Ui:PoorAccuracyMeters</c>: an accuracy above this draws the halo.</param>
/// <param name="LowBatteryPercent"><c>Ui:LowBatteryPercent</c>: a battery below this is low.</param>
/// <param name="FarAwayKm"><c>Ui:FarAwayKm</c>: a member farther than this from me is far.</param>
/// <param name="DefaultViewRadiusKm"><c>Ui:DefaultViewRadiusKm</c> or the device setting; <see cref="double.PositiveInfinity"/> is "all".</param>
public sealed record MapPayloadOptions(
    double PoorAccuracyMeters = 500,
    int LowBatteryPercent = 15,
    double FarAwayKm = 80,
    double DefaultViewRadiusKm = 40)
{
    /// <summary>The defaults of 01 section 4.1.</summary>
    public static MapPayloadOptions Default { get; } = new();
}
