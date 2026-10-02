namespace Realm.Web.Layout;

/// <summary>
/// The layout the viewport calls for (01 section 3.1, 03 section 3.1). <see cref="Unknown"/> until <c>ShellInterop</c> has reported the
/// viewport: the page then renders no sheet, only the CSS placeholder that reserves the Peek band.
/// </summary>
public enum LayoutMode
{
    Unknown,

    /// <summary>The bottom sheet: narrower than 840 px, or lower than 560 px.</summary>
    Compact,

    /// <summary>The left panel: at least 840 px wide and at least 560 px high.</summary>
    Expanded,
}

/// <summary>The "Layout" choice of 01 section 3.1 (<c>realm.layout</c>, D23) and the <c>?layout=</c> parameter of Demo: Auto, Bottom sheet, Side panel.</summary>
public enum LayoutOverride
{
    Auto,
    Sheet,
    Panel,
}

/// <summary>The two states of the Compact sheet (D42): Peek (19 %, at least 168 px) and Tall (80 % of the viewport height). Hooks and URLs spell them <c>peek</c> and <c>80</c>.</summary>
public enum SheetSize
{
    Peek,
    Tall,
}

/// <summary>
/// The layout mode of a viewport, as a pure function (03 section 3.6; 01 section 3.1): Compact below 840 px wide <b>or</b> below 560 px high
/// (a landscape phone is a bottom sheet, not a panel), Expanded from 840 x 560 up. The user override beats both rules.
/// </summary>
public static class LayoutResolver
{
    /// <summary>Narrower than this is Compact.</summary>
    public const double ExpandedMinWidthPx = 840;

    /// <summary>Lower than this is Compact, however wide.</summary>
    public const double ExpandedMinHeightPx = 560;

    /// <summary>The side panel is this wide at every Expanded viewport width (the 420 px Desktop panel is v1.1).</summary>
    public const double PanelWidthPx = 400;

    /// <summary>The valid range of <c>MudXSheet.CurrentSize</c>, in percent.</summary>
    public const int MinSizePercent = 10;

    /// <inheritdoc cref="MinSizePercent"/>
    public const int MaxSizePercent = 100;

    /// <summary>Never <see cref="LayoutMode.Unknown"/>: a viewport that is not a usable number (NaN, zero) is Compact, the phone layout.</summary>
    public static LayoutMode Resolve(double width, double height, LayoutOverride layoutOverride = LayoutOverride.Auto) =>
        layoutOverride switch
        {
            LayoutOverride.Sheet => LayoutMode.Compact,
            LayoutOverride.Panel => LayoutMode.Expanded,
            _ => width >= ExpandedMinWidthPx && height >= ExpandedMinHeightPx ? LayoutMode.Expanded : LayoutMode.Compact,
        };

    /// <summary>The override for a stored or URL value (<c>auto</c>, <c>sheet</c>, <c>panel</c>); anything else, and null, is Auto.</summary>
    public static LayoutOverride ParseOverride(string? value) =>
        value switch
        {
            "sheet" => LayoutOverride.Sheet,
            "panel" => LayoutOverride.Panel,
            _ => LayoutOverride.Auto,
        };

    /// <summary>
    /// <c>MudXSheet.CurrentSize</c> of the left panel: a percentage of the viewport <b>width</b>, <c>round(100 x 400 / width)</c> clamped to 10..100 (01 section 3.6):
    /// 45 at 884, 48 at 840, 28 at 1440. A width that is not a usable number gives the largest size, which the panel CSS overrides anyway.
    /// </summary>
    public static int PanelSizePercent(double viewportWidth)
    {
        if (!(viewportWidth > 0) || double.IsInfinity(viewportWidth))
        {
            return MaxSizePercent;
        }

        var percent = Math.Round(100 * PanelWidthPx / viewportWidth, MidpointRounding.AwayFromZero);
        return (int)Math.Clamp(percent, MinSizePercent, MaxSizePercent);
    }
}
