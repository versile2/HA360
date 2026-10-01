using Realm.Web.Map;

namespace Realm.Web.Components.Map;

/// <summary>The options of <c>realmMap.js</c> <c>init</c> (03 section 4.4). Names become camelCase on the wire.</summary>
/// <param name="ContainerId">The id of the map's <c>div</c>.</param>
/// <param name="Center"><c>[lon, lat]</c>, used only until the first <c>fitDefault</c>.</param>
/// <param name="RestoreCamera">The camera from <c>sessionStorage["realm.camera"]</c>; S7's shell interop reads it, so it is null until then.</param>
public sealed record MapInitOptions(
    string ContainerId,
    string StyleId,
    IReadOnlyList<double> Center,
    double Zoom,
    bool ReducedMotion,
    bool TestHooks,
    MapStrings Strings,
    MapFeatures Features,
    CameraState? RestoreCamera = null,
    double MinZoom = 3,
    double MaxZoom = 19);

/// <summary>Switches that exist for E2E isolation; both are always true in the product.</summary>
public sealed record MapFeatures(bool Fanout, bool Bubbles)
{
    /// <summary>Both features on.</summary>
    public static MapFeatures All { get; } = new(Fanout: true, Bubbles: true);
}

/// <summary>Every user-visible string the script needs, formatted here so JavaScript never builds one (03 section 4.2).</summary>
/// <param name="ClusterName">"{n} people off screen: {names}. Double tap to include them on the map." (01 section 10.3); JavaScript fills the two placeholders.</param>
public sealed record MapStrings(string MapLabel, string AttributionLabel, string ClusterName, string ClusterTooltip, string DemoAttribution)
{
    /// <summary>The English strings of 01 sections 4.10 and 10.3 (the cluster tooltip and the demo attribution are not written in 01).</summary>
    public static MapStrings Default { get; } = new(
        MapLabel: "Map of the Realm. Use the list for details.",
        AttributionLabel: "Map data attribution",
        ClusterName: "{n} people off screen: {names}. Double tap to include them on the map.",
        ClusterTooltip: "{names} · tap to include them on the map",
        DemoAttribution: "Demo map");
}
