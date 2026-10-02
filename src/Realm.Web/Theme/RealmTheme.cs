using MudBlazor;

namespace Realm.Web.Theme;

/// <summary>
/// The one MudBlazor theme of v1 (03 section 3.8, 01 sections 7.2 and 7.4): dark only (D35), built from <see cref="RealmPalette"/> so the
/// palette has a single source next to <see cref="RealmTokens"/>. Cinzel is the display face of H1 to H3, Atkinson Hyperlegible is everything
/// else, and Roboto never comes first (the stacks match <c>--realm-font-display</c> and <c>--realm-font-body</c> in <c>css/app.css</c>).
/// <c>MainLayout</c> hands the result to <c>MudThemeProvider</c> with <c>IsDarkMode="true"</c>; the light palette of the MudTheme is left at
/// MudBlazor's default because nothing ever selects it.
/// </summary>
public static class RealmTheme
{
    /// <summary>Builds the theme; every call returns a new instance, so a caller may keep or change its copy.</summary>
    public static MudTheme Create() => new()
    {
        PaletteDark = new PaletteDark
        {
            Background = RealmPalette.Bg,
            Surface = RealmPalette.Surface,
            DrawerBackground = RealmPalette.Surface,
            AppbarBackground = RealmPalette.Surface,
            BackgroundGray = RealmPalette.Surface2,
            LinesDefault = RealmPalette.Line,
            Divider = RealmPalette.Line,
            TableLines = RealmPalette.Line,
            TextPrimary = RealmPalette.Text,
            AppbarText = RealmPalette.Text,
            DrawerText = RealmPalette.Text,
            TextSecondary = RealmPalette.Text2,
            TextDisabled = RealmPalette.TextDisabled,
            ActionDisabled = RealmPalette.TextDisabled,
            Primary = RealmPalette.Primary,
            PrimaryContrastText = RealmPalette.OnPrimary,
            Secondary = RealmPalette.Secondary,
            SecondaryContrastText = RealmPalette.OnSecondary,
            Tertiary = RealmPalette.Tertiary,
            TertiaryContrastText = RealmPalette.OnTertiary,
            Success = RealmPalette.Success,
            Warning = RealmPalette.Warning,
            Error = RealmPalette.Error,
            Info = RealmPalette.Info,
            OverlayDark = RealmPalette.Scrim,
        },
        Typography = new Typography
        {
            Default = new DefaultTypography { FontFamily = BodyFamily(), FontSize = "1rem", FontWeight = "400", LineHeight = "1.5" },
            H1 = new H1Typography { FontFamily = DisplayFamily(), FontSize = "clamp(24px, 6vw, 32px)", FontWeight = "700", LineHeight = "1.25" },
            H2 = new H2Typography { FontFamily = DisplayFamily(), FontSize = "1.375rem", FontWeight = "700", LineHeight = "1.273" },
            H3 = new H3Typography { FontFamily = DisplayFamily(), FontSize = "1.125rem", FontWeight = "600", LineHeight = "1.333" },
            H4 = new H4Typography { FontFamily = BodyFamily() },
            H5 = new H5Typography { FontFamily = BodyFamily() },
            H6 = new H6Typography { FontFamily = BodyFamily() },
            Subtitle1 = new Subtitle1Typography { FontFamily = BodyFamily() },
            Subtitle2 = new Subtitle2Typography { FontFamily = BodyFamily() },
            Body1 = new Body1Typography { FontFamily = BodyFamily() },
            Body2 = new Body2Typography { FontFamily = BodyFamily() },
            Button = new ButtonTypography { FontFamily = BodyFamily(), FontSize = ".9375rem", FontWeight = "700", LineHeight = "1.333", TextTransform = "none" },
            Caption = new CaptionTypography { FontFamily = BodyFamily() },
            Overline = new OverlineTypography { FontFamily = BodyFamily() },
        },
        LayoutProperties = new LayoutProperties { DefaultBorderRadius = "16px" },
    };

    // 01 section 7.4: display Cinzel, "Trajan Pro", Georgia, "Times New Roman", serif.
    private static string[] DisplayFamily() => ["Cinzel", "Trajan Pro", "Georgia", "Times New Roman", "serif"];

    // 01 section 7.4: body "Atkinson Hyperlegible", system-ui, Roboto, "Segoe UI", sans-serif.
    private static string[] BodyFamily() => ["Atkinson Hyperlegible", "system-ui", "Roboto", "Segoe UI", "sans-serif"];
}
