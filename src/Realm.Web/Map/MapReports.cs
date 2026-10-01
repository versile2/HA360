namespace Realm.Web.Map;

// What realmMap.js reports back (03 sections 4.3 and 4.7). Names are camelCase on the wire; JavaScript is the writer, so the
// positional constructors only have to match those names.

/// <summary>The result of <c>init</c> and the argument of <c>OnReady</c>.</summary>
/// <param name="PayloadSchema">Must equal <c>MapInterop.PayloadSchema</c>.</param>
public sealed record ReadyInfo(string JsVersion, int PayloadSchema, string Maplibre);

/// <summary>The camera after a move settled. <c>Center</c> is <c>[lon, lat]</c>; <c>Bounds</c> is <c>[[west, south], [east, north]]</c>.</summary>
/// <param name="Animated">The last move used a duration above zero.</param>
/// <param name="LastDurationMs">The duration of the last move; 0 for a jump, a gesture or reduced motion.</param>
/// <param name="Recenter">Computed in JavaScript against the default targets (01 section 4.11).</param>
/// <param name="UserInitiated">The move was a user gesture.</param>
public sealed record CameraState(
    IReadOnlyList<double> Center,
    double Zoom,
    IReadOnlyList<IReadOnlyList<double>> Bounds,
    bool Animated,
    double LastDurationMs,
    RecenterState Recenter,
    bool UserInitiated);

/// <summary>The outcome of a style switch. <c>Error</c> is null when it worked.</summary>
public sealed record StyleResult(string StyleId, bool Ok, string? Error);
