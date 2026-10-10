namespace Realm.Web.Map;

// The payload records that C# sends to realmMap.js, one per type of 03 section 4.5. Property names become camelCase on the wire
// (System.Text.Json web defaults, the same options Blazor's JS interop uses), so a rename here is a contract change that
// tests/contract/payloadShape.mjs catches in CI. Coordinates are degrees (lat, lon), lengths metres or CSS pixels, and no number or
// time travels formatted except the display strings, which C# builds (03 section 4.2).

/// <summary>Insets in CSS pixels (03 section 4.5 <c>Padding</c>).</summary>
public sealed record Padding(double Top, double Right, double Bottom, double Left);

/// <summary>A pin's ring (01 sections 4.2 and 4.7). <c>WidthPx</c> is 4 for members and 3 for vehicles; JavaScript adds 1 when selected.</summary>
public sealed record Ring(string Color, bool Dashed, double WidthPx);

/// <summary>What the layout contributes to the map padding (01 section 3.4.3).</summary>
/// <param name="PanelLeftPx">Expanded: 16.</param>
/// <param name="PanelWidthPx">Expanded: 400, 0 when the panel is hidden; 0 in Compact.</param>
/// <param name="StackVisible">False when the sheet is Tall (D42).</param>
/// <param name="Safe">Safe-area insets in CSS pixels.</param>
/// <param name="NavHeightPx">64, without the bottom safe inset.</param>
public sealed record LayoutPayload(
    MapLayoutMode Mode,
    double PanelLeftPx,
    double PanelWidthPx,
    bool PanelHidden,
    bool StackVisible,
    Padding Safe,
    double NavHeightPx);

/// <summary>One person's pin and edge bubble. Lat and Lon are both null when there is no fix: no pin and no bubble.</summary>
/// <param name="Initial">The first letter of the display name, shown on the member colour when there is no photo.</param>
/// <param name="PoorAccuracy">Draws the accuracy halo (01 section 9 item 12).</param>
/// <param name="ZClass">Draw order: stale 0, at a place 1, out 2, driving 3; JavaScript raises the selected pin to 4.</param>
/// <param name="AvatarUrl">The relative <c>avatars/{id}</c>, or null for the initials (or the glyph).</param>
/// <param name="Glyph">The glyph shown instead of the initials when the owner chose one (0.2.1); null otherwise.</param>
/// <param name="Chip">The chip text, decided here (01 section 4.4); null means no chip.</param>
/// <param name="ChipMinute">The epoch minute the chip was computed for; JavaScript never recomputes it.</param>
public sealed record MemberPayloadItem(
    string Id,
    string Name,
    string Initial,
    string Color,
    double? Lat,
    double? Lon,
    double? AccuracyM,
    bool PoorAccuracy,
    MemberStatus Status,
    Ring Ring,
    PinBadge? Badge,
    bool LowBattery,
    bool DrivingFresh,
    bool Far,
    bool IsStatic,
    bool IsMe,
    int ZClass,
    string? AvatarUrl,
    string? Chip,
    long ChipMinute,
    string AriaLabel,
    string Tooltip,
    string BubbleLabel,
    string BubbleTooltip,
    MapGlyph? Glyph = null);

/// <summary>The full member list, versioned: JavaScript ignores a payload whose version is lower than the one it holds.</summary>
/// <param name="MeId">The viewer's member id; empty when there is no member at all.</param>
public sealed record MembersPayload(int Version, string MeId, IReadOnlyList<MemberPayloadItem> Members);

/// <summary>One tracker's pin. Lat and Lon are null for a tracker without a position: no pin.</summary>
/// <param name="Initial">The first letter of the name, drawn when <paramref name="ShowInitial"/> is set.</param>
/// <param name="Color">The roster colour: the face is drawn on it (0.2.1).</param>
/// <param name="AvatarUrl">The relative <c>avatars/{id}</c> when the face is the photo; null otherwise.</param>
/// <param name="ShowInitial">The owner chose the initial: it replaces the glyph.</param>
public sealed record VehiclePayloadItem(
    string Id,
    string Name,
    MapGlyph Glyph,
    double? Lat,
    double? Lon,
    Ring Ring,
    bool Stale,
    string? Chip,
    string AriaLabel,
    string Tooltip,
    string Initial = "",
    string Color = "#E8BC4E",
    string? AvatarUrl = null,
    bool ShowInitial = false);

/// <summary>The full vehicle list, versioned.</summary>
public sealed record VehiclesPayload(int Version, IReadOnlyList<VehiclePayloadItem> Vehicles);

/// <summary>One drawn zone. Every zone is in the payload whatever its radius (0.2.1), except one with no radius.</summary>
public sealed record ZoneItem(string Id, string Name, double Lat, double Lon, double RadiusM, bool Occupied);

/// <summary>The zone colours for one kind of map style (01 section 4.6).</summary>
public sealed record ZoneAppearance(string LineColor, double FillAlpha, double FillAlphaOccupied, bool Casing);

/// <summary>The three appearances; JavaScript picks one by the active style.</summary>
public sealed record ZoneAppearances(ZoneAppearance Dark, ZoneAppearance Light, ZoneAppearance Imagery);

/// <summary>The zones and whether to draw them at all (the "Show places" switch).</summary>
public sealed record ZonesPayload(int Version, bool Show, IReadOnlyList<ZoneItem> Zones, ZoneAppearances Appearances);

/// <summary>The default view of 01 section 4.9: bounds in <c>[[west, south], [east, north]]</c> order and a maximum zoom of 16.</summary>
public sealed record DefaultView(IReadOnlyList<IReadOnlyList<double>> Bounds, int MaxZoom);

/// <summary>The "me alone" view: <c>[lon, lat]</c> and zoom 16 (01 section 4.11).</summary>
public sealed record MeTarget(IReadOnlyList<double> Center, int Zoom);

/// <summary>The camera targets C# computes; JavaScript only performs them. <c>Me</c> is null when no position stands for me.</summary>
public sealed record DefaultTargets(int Version, DefaultView Default, MeTarget? Me);
