using System.Globalization;
using Realm.Web.Map;
using Realm.Web.Theme;
using Xunit;

namespace Realm.Web.Tests;

/// <summary>
/// The contrast table of 01 section 7.3 (dark theme and map-side graphics), recomputed with the WCAG 2.1 relative-luminance formula from the
/// constants the app ships: <see cref="RealmPalette"/> for the surfaces, text and accents, and <see cref="MapPalette"/> for the pin rings. The few
/// colours that section 7.3 uses and no shipped constant holds yet (the low-battery badge text, the initials colour, the five member colours of
/// 01 section 7.5 and the mid-grey map of the Peek row) are typed below from the table; they join <see cref="RealmPalette"/> in the slice that first
/// draws them. 01 section 7.3 requires 4.5:1 for normal text and 3:1 for large text and for UI component and graphic boundaries.
/// </summary>
public sealed class ContrastTests
{
    private const double TextMinimum = 4.5;
    private const double GraphicMinimum = 3.0;

    // 01 section 7.3 states "all >= 7.6:1" for the initials on the member colours, which is stricter than the 4.5:1 of normal text.
    private const double InitialsMinimum = 7.6;

    // Not in RealmPalette yet; the values and the ratios are those of 01 sections 7.3 and 7.5.
    private const string LowBatteryBadgeText = "#2A0A0E";
    private const string Initials = "#10122A";
    private const string MidGreyMap = "#F2EFE9";
    private const string MemberKing = "#E8BC4E";
    private const string MemberQueen = "#C792EA";
    private const string MemberJester = "#5CC8FF";
    private const string MemberCryptid = "#FF8FB1";
    private const string MemberPrince = "#7EE0A5";

    private readonly record struct Rgb(int R, int G, int B);

    /// <summary>One row of 01 section 7.3: the pair, the minimum it must meet and the ratio the table prints (two decimals).</summary>
    private sealed record Pair(string Name, Rgb Foreground, Rgb Background, double Minimum, double Printed);

    [Fact(DisplayName = "[AC-44a] every contrast pair of 01 section 7.3, recomputed from the shipped tokens, meets its stated minimum")]
    public void EveryPairOfSection73MeetsItsStatedMinimum()
    {
        var failures = new List<string>();
        foreach (var pair in Pairs())
        {
            var ratio = Ratio(pair.Foreground, pair.Background);
            if (ratio < pair.Minimum)
            {
                failures.Add($"{pair.Name}: {Show(ratio)}:1 is below the minimum of {Show(pair.Minimum)}:1");
            }
        }

        Assert.Empty(failures);
    }

    [Fact]
    public void Section73_ListsThirtySevenPairsAndAllOfThemAreChecked()
    {
        // 27 rows of the dark table (the rows with two surfaces count twice), 5 ring rows and 5 initials rows.
        Assert.Equal(37, Pairs().Count);
    }

    [Fact]
    public void RecomputedRatios_AgreeWithTheTablePrintedInSection73()
    {
        var differences = new List<string>();
        foreach (var pair in Pairs())
        {
            var ratio = Ratio(pair.Foreground, pair.Background);

            // The table rounds to two decimals, so the unrounded ratio is within half a unit of the last printed digit.
            if (Math.Abs(ratio - pair.Printed) > 0.005)
            {
                differences.Add($"{pair.Name}: the table prints {Show(pair.Printed)}:1, the tokens give {Show(ratio)}:1");
            }
        }

        Assert.Empty(differences);
    }

    [Fact]
    public void PeekSurface_IsTheSurfaceColourAtEightySixPercent()
    {
        var (r, g, b, alpha) = ParseRgba(RealmPalette.SheetBgPeek);

        Assert.Equal(Hex(RealmPalette.Surface), new Rgb((int)r, (int)g, (int)b));
        Assert.Equal(0.86, alpha, 6);
    }

    [Fact]
    public void Formula_GivesTwentyOneForBlackOnWhiteAndSeparatesTheTextBoundary()
    {
        var white = Hex("#FFFFFF");

        Assert.Equal(21.0, Ratio(white, Hex("#000000")), 6);
        Assert.Equal(1.0, Ratio(white, white), 6);

        // The well-known boundary: #767676 is the lightest grey that passes 4.5:1 on white, #777777 does not.
        Assert.True(Ratio(Hex("#767676"), white) >= TextMinimum);
        Assert.True(Ratio(Hex("#777777"), white) < TextMinimum);
    }

    // ---- the pairs of 01 section 7.3, in the order of the table -------------------------------------------------------------------------------

    private static List<Pair> Pairs()
    {
        var bg = Hex(RealmPalette.Bg);
        var surface = Hex(RealmPalette.Surface);
        var surface2 = Hex(RealmPalette.Surface2);
        var plum = Hex(RealmPalette.Plum);
        var text = Hex(RealmPalette.Text);
        var text2 = Hex(RealmPalette.Text2);
        var gold = Hex(RealmPalette.Primary);
        var onPrimary = Hex(RealmPalette.OnPrimary);
        var error = Hex(RealmPalette.Error);
        var pinOutline = Hex(RealmPalette.PinOutline);

        // The Peek surface over the worst-case map (pure white) and over a mid-grey map.
        var peekOverWhite = Blend(RealmPalette.SheetBgPeek, Hex("#FFFFFF"));
        var peekOverGrey = Blend(RealmPalette.SheetBgPeek, Hex(MidGreyMap));
        var initials = Hex(Initials);

        return
        [
            // Dark theme.
            new("text on surface", text, surface, TextMinimum, 15.23),
            new("text on surface-2", text, surface2, TextMinimum, 13.51),
            new("text on bg", text, bg, TextMinimum, 17.01),
            new("text on plum", text, plum, TextMinimum, 13.67),
            new("text-2 on surface", text2, surface, TextMinimum, 8.86),
            new("text-2 on surface-2", text2, surface2, TextMinimum, 7.86),
            new("text-2 on plum", text2, plum, TextMinimum, 7.95),
            new("gold on surface", gold, surface, TextMinimum, 9.58),
            new("gold on surface-2", gold, surface2, TextMinimum, 8.50),
            new("on-primary on gold", onPrimary, gold, TextMinimum, 10.24),
            new("error on surface", error, surface, TextMinimum, 6.83),
            new("error on surface-2", error, surface2, TextMinimum, 6.07),
            new("success on surface", Hex(RealmPalette.Success), surface, TextMinimum, 9.84),
            new("success on surface-2", Hex(RealmPalette.Success), surface2, TextMinimum, 8.73),
            new("warning on surface", Hex(RealmPalette.Warning), surface, TextMinimum, 9.72),
            new("warning on surface-2", Hex(RealmPalette.Warning), surface2, TextMinimum, 8.62),
            new("info on surface", Hex(RealmPalette.Info), surface, TextMinimum, 9.11),
            new("stale on surface", Hex(RealmPalette.Stale), surface, TextMinimum, 6.64),
            new("secondary on surface", Hex(RealmPalette.Secondary), surface, TextMinimum, 7.00),
            new("text on the Peek surface over white", text, peekOverWhite, TextMinimum, 9.96),
            new("text-2 on the Peek surface over white", text2, peekOverWhite, TextMinimum, 5.80),
            new("text on the Peek surface over a mid-grey map", text, peekOverGrey, TextMinimum, 10.30),
            new("text-2 on the Peek surface over a mid-grey map", text2, peekOverGrey, TextMinimum, 5.99),
            new("low-battery badge text on error", Hex(LowBatteryBadgeText), error, TextMinimum, 7.29),
            new("bar fill on bar track (graphic)", Hex(RealmPalette.BarFill), Hex(RealmPalette.BarTrack), GraphicMinimum, 3.38),
            new("handle bar on the Peek surface over white (graphic)", Hex(RealmPalette.LineStrong), peekOverWhite, GraphicMinimum, 3.30),
            new("focus ring on surface (graphic)", Hex(RealmPalette.Focus), surface, GraphicMinimum, 9.58),

            // Map-side graphics: each ring against the pin outline.
            new("ring driving on the pin outline (graphic)", Hex(MapPalette.RingDriving), pinOutline, GraphicMinimum, 8.77),
            new("ring at place on the pin outline (graphic)", Hex(MapPalette.RingAtPlace), pinOutline, GraphicMinimum, 10.73),
            new("ring out on the pin outline (graphic)", Hex(MapPalette.RingOut), pinOutline, GraphicMinimum, 17.01),
            new("ring stale on the pin outline (graphic)", Hex(MapPalette.RingStale), pinOutline, GraphicMinimum, 7.42),
            new("ring static on the pin outline (graphic)", Hex(MapPalette.RingStatic), pinOutline, GraphicMinimum, 8.07),

            // Initials on the member colours.
            new("initials on king", initials, Hex(MemberKing), InitialsMinimum, 10.28),
            new("initials on queen", initials, Hex(MemberQueen), InitialsMinimum, 7.64),
            new("initials on jester", initials, Hex(MemberJester), InitialsMinimum, 9.77),
            new("initials on cryptid", initials, Hex(MemberCryptid), InitialsMinimum, 8.60),
            new("initials on prince", initials, Hex(MemberPrince), InitialsMinimum, 11.46),
        ];
    }

    // ---- the WCAG 2.1 formula ------------------------------------------------------------------------------------------------------------------

    private static double Channel(int value)
    {
        var c = value / 255.0;
        return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }

    private static double Luminance(Rgb colour) => (0.2126 * Channel(colour.R)) + (0.7152 * Channel(colour.G)) + (0.0722 * Channel(colour.B));

    private static double Ratio(Rgb a, Rgb b)
    {
        var first = Luminance(a);
        var second = Luminance(b);
        var lighter = Math.Max(first, second);
        var darker = Math.Min(first, second);
        return (lighter + 0.05) / (darker + 0.05);
    }

    // ---- colours -------------------------------------------------------------------------------------------------------------------------------

    // "#RRGGBB".
    private static Rgb Hex(string value) => new(
        int.Parse(value.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
        int.Parse(value.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
        int.Parse(value.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));

    // "rgba(20,26,51,0.86)".
    private static (double R, double G, double B, double A) ParseRgba(string value)
    {
        var open = value.IndexOf('(');
        var close = value.IndexOf(')');
        var parts = value[(open + 1)..close].Split(',');
        return (Number(parts[0]), Number(parts[1]), Number(parts[2]), Number(parts[3]));
    }

    private static double Number(string text) => double.Parse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture);

    // The colour of `over` ("rgba(r,g,b,a)") laid on the opaque `under`, per channel: alpha * over + (1 - alpha) * under.
    private static Rgb Blend(string over, Rgb under)
    {
        var (r, g, b, alpha) = ParseRgba(over);
        return new Rgb(Mix(r, under.R, alpha), Mix(g, under.G, alpha), Mix(b, under.B, alpha));
    }

    private static int Mix(double over, int under, double alpha) => (int)Math.Round((alpha * over) + ((1 - alpha) * under));

    private static string Show(double value) => value.ToString("0.00", CultureInfo.InvariantCulture);
}
