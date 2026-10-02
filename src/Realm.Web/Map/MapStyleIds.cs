namespace Realm.Web.Map;

/// <summary>The map style ids of 01 section 4.12 (03 section 4.9). <c>demo-offline</c> is hidden: Demo and CI select it, never the Layers popover.</summary>
public static class MapStyleIds
{
    public const string Night = "night";
    public const string Day = "day";
    public const string Streets = "streets";
    public const string Satellite = "satellite";
    public const string DemoOffline = "demo-offline";

    /// <summary>Every id <c>realmMap.js</c> accepts.</summary>
    public static IReadOnlyList<string> All { get; } = [Night, Day, Streets, Satellite, DemoOffline];
}
