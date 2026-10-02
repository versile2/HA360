namespace Realm.Web.Map;

/// <summary>
/// The layout facts the map needs, in the shape <see cref="MapPayloadFactory.Layout"/> turns into a <see cref="LayoutPayload"/>
/// (01 sections 3.1 and 3.4.3). S7 owns the viewport and the sheet; until it passes a real value the page uses <see cref="Compact"/>.
/// </summary>
/// <param name="PanelHidden">Expanded only: the side panel is folded away.</param>
/// <param name="StackVisible">False while the sheet is Tall (D42).</param>
/// <param name="Safe">Safe-area insets in CSS pixels.</param>
public sealed record MapLayout(MapLayoutMode Mode, bool PanelHidden, bool StackVisible, Padding Safe)
{
    /// <summary>The panel's left gutter in Expanded (03 section 4.5).</summary>
    public const double PanelLeftPx = 16;

    /// <summary>The panel's width in Expanded (03 section 4.5).</summary>
    public const double PanelWidthPx = 400;

    /// <summary>The bottom navigation without the safe inset (01 section 3.2).</summary>
    public const double NavHeightPx = 64;

    /// <summary>Phone portrait: bottom sheet, right stack visible, no inset.</summary>
    public static MapLayout Compact { get; } = new(MapLayoutMode.Compact, PanelHidden: false, StackVisible: true, new Padding(0, 0, 0, 0));

    /// <summary>The side-panel layout with the panel shown.</summary>
    public static MapLayout Expanded { get; } = new(MapLayoutMode.Expanded, PanelHidden: false, StackVisible: true, new Padding(0, 0, 0, 0));
}
