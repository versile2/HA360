using MudBlazor;
using MudBlazor.Utilities;
using Realm.Web.Theme;
using Xunit;

namespace Realm.Web.Tests;

/// <summary>
/// The palette of 01 section 7.2 has one source, <see cref="RealmPalette"/>, and two consumers that this class checks against the table typed out
/// below (not against the constants, so a wrong constant fails here): <see cref="RealmTokens"/> must emit every <c>--realm-*</c> variable with the
/// table value, and <see cref="RealmTheme.Create"/> must map the table onto the MudBlazor dark palette, the display and body typefaces, the button
/// text transform and the 16 px default radius (03 section 3.8).
/// </summary>
public sealed class PaletteTests
{
    // 01 section 7.2, column "Token (CSS)" and column "Dark value".
    private static readonly (string Token, string Value)[] Spec =
    [
        ("bg", "#0B0E1F"),
        ("surface", "#141A33"),
        ("surface-2", "#1C2347"),
        ("plum", "#2B1D45"),
        ("line", "#323B66"),
        ("line-strong", "#8089B8"),
        ("text", "#F3F0FA"),
        ("text-2", "#B4B9D6"),
        ("text-disabled", "#6E7494"),
        ("primary", "#E8BC4E"),
        ("on-primary", "#1A1405"),
        ("secondary", "#B794F6"),
        ("on-secondary", "#1A0F2E"),
        ("tertiary", "#E8BC4E"),
        ("on-tertiary", "#1A1405"),
        ("success", "#4ADE80"),
        ("warning", "#FFB454"),
        ("error", "#FF7A85"),
        ("info", "#5CC8FF"),
        ("stale", "#9AA0BD"),
        ("bar-track", "#232B54"),
        ("bar-fill", "#8E6BD6"),
        ("control-bg", "rgba(20,26,51,0.94)"),
        ("control-icon", "#E8BC4E"),
        ("sheet-bg-peek", "rgba(20,26,51,0.86)"),
        ("scrim", "rgba(0,0,0,0.56)"),
        ("focus", "#E8BC4E"),
        ("pin-outline", "#0B0E1F"),
    ];

    [Fact]
    public void Tokens_EmitEveryVariableOfSection72WithTheTableValue()
    {
        var emitted = EmittedTokens();
        var problems = new List<string>();

        foreach (var (token, expected) in Spec)
        {
            if (!emitted.TryGetValue(token, out var actual))
            {
                problems.Add($"--realm-{token} is not emitted by RealmTokens.Css()");
            }
            else if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
            {
                problems.Add($"--realm-{token}: the table says {expected}, RealmTokens.Css() emits {actual}");
            }
        }

        Assert.Empty(problems);
    }

    [Fact]
    public void Tokens_AreDeclaredOnRootForTheDarkScheme()
    {
        var css = RealmTokens.Css();

        Assert.StartsWith(":root {", css, StringComparison.Ordinal);
        Assert.Contains("color-scheme: dark;", css, StringComparison.Ordinal);
    }

    [Fact]
    public void Theme_MapsTheDarkPaletteOfSection72OntoMudBlazor()
    {
        var dark = RealmTheme.Create().PaletteDark;
        var values = Spec.ToDictionary(row => row.Token, row => row.Value, StringComparer.Ordinal);

        // 01 section 7.2, column "MudBlazor Palette field": one row per field, with the token it takes its value from.
        (string Field, MudColor Actual, string Token)[] mapping =
        [
            ("Background", dark.Background, "bg"),
            ("Surface", dark.Surface, "surface"),
            ("DrawerBackground", dark.DrawerBackground, "surface"),
            ("AppbarBackground", dark.AppbarBackground, "surface"),
            ("BackgroundGray", dark.BackgroundGray, "surface-2"),
            ("LinesDefault", dark.LinesDefault, "line"),
            ("Divider", dark.Divider, "line"),
            ("TableLines", dark.TableLines, "line"),
            ("TextPrimary", dark.TextPrimary, "text"),
            ("AppbarText", dark.AppbarText, "text"),
            ("DrawerText", dark.DrawerText, "text"),
            ("TextSecondary", dark.TextSecondary, "text-2"),
            ("TextDisabled", dark.TextDisabled, "text-disabled"),
            ("ActionDisabled", dark.ActionDisabled, "text-disabled"),
            ("Primary", dark.Primary, "primary"),
            ("PrimaryContrastText", dark.PrimaryContrastText, "on-primary"),
            ("Secondary", dark.Secondary, "secondary"),
            ("SecondaryContrastText", dark.SecondaryContrastText, "on-secondary"),
            ("Tertiary", dark.Tertiary, "tertiary"),
            ("TertiaryContrastText", dark.TertiaryContrastText, "on-tertiary"),
            ("Success", dark.Success, "success"),
            ("Warning", dark.Warning, "warning"),
            ("Error", dark.Error, "error"),
            ("Info", dark.Info, "info"),
        ];

        var problems = new List<string>();
        foreach (var (field, actual, token) in mapping)
        {
            var expected = MudColor.Parse(values[token]);
            if (!expected.Equals(actual))
            {
                problems.Add($"{field}: the table says {values[token]} (--realm-{token}), the theme has {actual.ToString(MudColorOutputFormats.HexA)}");
            }
        }

        Assert.Empty(problems);

        // OverlayDark is a plain CSS string in MudBlazor, not a MudColor.
        Assert.Equal(values["scrim"], dark.OverlayDark);
    }

    [Fact]
    public void Theme_SetsCinzelOnH1ToH3AndAtkinsonHyperlegibleOnEverythingElse()
    {
        var typography = RealmTheme.Create().Typography;

        Assert.Equal("Cinzel", FirstFamily(typography.H1));
        Assert.Equal("Cinzel", FirstFamily(typography.H2));
        Assert.Equal("Cinzel", FirstFamily(typography.H3));

        BaseTypography[] others =
        [
            typography.Default,
            typography.H4,
            typography.H5,
            typography.H6,
            typography.Subtitle1,
            typography.Subtitle2,
            typography.Body1,
            typography.Body2,
            typography.Button,
            typography.Caption,
            typography.Overline,
        ];
        foreach (var other in others)
        {
            Assert.Equal("Atkinson Hyperlegible", FirstFamily(other));
        }
    }

    [Fact]
    public void Theme_NeverNamesRobotoFirst()
    {
        var typography = RealmTheme.Create().Typography;

        BaseTypography[] all =
        [
            typography.Default, typography.H1, typography.H2, typography.H3, typography.H4, typography.H5, typography.H6,
            typography.Subtitle1, typography.Subtitle2, typography.Body1, typography.Body2, typography.Button, typography.Caption, typography.Overline,
        ];
        foreach (var item in all)
        {
            Assert.NotEqual("Roboto", FirstFamily(item));
        }
    }

    [Fact]
    public void Theme_ButtonsKeepTheirCaseAndTheDefaultRadiusIsSixteenPixels()
    {
        var theme = RealmTheme.Create();

        Assert.Equal("none", theme.Typography.Button.TextTransform);
        Assert.Equal("16px", theme.LayoutProperties.DefaultBorderRadius);
    }

    // The emitted `--realm-<name>: <value>;` lines of the stylesheet, keyed by name.
    private static Dictionary<string, string> EmittedTokens()
    {
        const string prefix = "--realm-";
        var tokens = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in RealmTokens.Css().Split('\n'))
        {
            var text = line.Trim();
            if (!text.StartsWith(prefix, StringComparison.Ordinal) || !text.EndsWith(';'))
            {
                continue;
            }

            var colon = text.IndexOf(':');
            tokens[text[prefix.Length..colon]] = text[(colon + 1)..^1].Trim();
        }

        return tokens;
    }

    private static string FirstFamily(BaseTypography typography) =>
        typography.FontFamily is { Length: > 0 } families ? families[0] : string.Empty;
}
