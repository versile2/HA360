namespace Realm.Domain;

/// <summary>The two names a <see cref="ConnectionVm"/> can carry (Home Assistant and Life360 are the only sources); use these instead of string literals.</summary>
public static class ConnectionNames
{
    public const string HomeAssistant = "HomeAssistant";
    public const string Life360Trackers = "Life360Trackers";
}
