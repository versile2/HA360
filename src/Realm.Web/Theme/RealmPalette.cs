namespace Realm.Web.Theme;

/// <summary>
/// The dark palette of 01 section 7.2 as constants. This file is the only place in the repository that holds colour literals:
/// <see cref="RealmTokens"/> emits them as CSS custom properties, and every stylesheet and Razor file uses <c>var(--realm-*)</c>
/// (the <c>no-colour-literals</c> guard). Dark is the only theme in v1 (D35). The status and member colours of 01 sections 4.3 and 7.5
/// join this table in the slice that first draws them.
/// </summary>
public static class RealmPalette
{
    public const string Bg = "#0B0E1F";
    public const string Surface = "#141A33";
    public const string Surface2 = "#1C2347";
    public const string Plum = "#2B1D45";
    public const string Line = "#323B66";
    public const string LineStrong = "#8089B8";

    public const string Text = "#F3F0FA";
    public const string Text2 = "#B4B9D6";
    public const string TextDisabled = "#6E7494";

    public const string Primary = "#E8BC4E";
    public const string OnPrimary = "#1A1405";
    public const string Secondary = "#B794F6";
    public const string OnSecondary = "#1A0F2E";
    public const string Tertiary = "#E8BC4E";
    public const string OnTertiary = "#1A1405";

    public const string Success = "#4ADE80";
    public const string Warning = "#FFB454";
    public const string Error = "#FF7A85";
    public const string Info = "#5CC8FF";
    public const string Stale = "#9AA0BD";

    public const string BarTrack = "#232B54";
    public const string BarFill = "#8E6BD6";

    public const string ControlBg = "rgba(20,26,51,0.94)";
    public const string ControlIcon = "#E8BC4E";
    public const string SheetBgPeek = "rgba(20,26,51,0.86)";
    public const string Scrim = "rgba(0,0,0,0.56)";
    public const string Focus = "#E8BC4E";

    /// <summary>The 2 px outline of pins and bubbles (01 section 4.2); a token so that <c>realm-map.css</c> needs no colour literal.</summary>
    public const string PinOutline = "#0B0E1F";
}
