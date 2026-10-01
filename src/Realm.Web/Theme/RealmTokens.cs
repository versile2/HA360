using System.Security.Cryptography;
using System.Text;

namespace Realm.Web.Theme;

/// <summary>
/// The stylesheet served at <c>GET css/tokens.css</c> (03 sections 3.8 and 5.3): <see cref="RealmPalette"/> as <c>--realm-*</c>
/// custom properties on <c>:root</c>. It is built once, so its content hash is a stable strong ETag.
/// </summary>
public static class RealmTokens
{
    private static readonly (string Name, string Value)[] Tokens =
    [
        ("bg", RealmPalette.Bg),
        ("surface", RealmPalette.Surface),
        ("surface-2", RealmPalette.Surface2),
        ("plum", RealmPalette.Plum),
        ("line", RealmPalette.Line),
        ("line-strong", RealmPalette.LineStrong),
        ("text", RealmPalette.Text),
        ("text-2", RealmPalette.Text2),
        ("text-disabled", RealmPalette.TextDisabled),
        ("primary", RealmPalette.Primary),
        ("on-primary", RealmPalette.OnPrimary),
        ("secondary", RealmPalette.Secondary),
        ("on-secondary", RealmPalette.OnSecondary),
        ("tertiary", RealmPalette.Tertiary),
        ("on-tertiary", RealmPalette.OnTertiary),
        ("success", RealmPalette.Success),
        ("warning", RealmPalette.Warning),
        ("error", RealmPalette.Error),
        ("info", RealmPalette.Info),
        ("stale", RealmPalette.Stale),
        ("bar-track", RealmPalette.BarTrack),
        ("bar-fill", RealmPalette.BarFill),
        ("control-bg", RealmPalette.ControlBg),
        ("control-icon", RealmPalette.ControlIcon),
        ("sheet-bg-peek", RealmPalette.SheetBgPeek),
        ("scrim", RealmPalette.Scrim),
        ("focus", RealmPalette.Focus),
        ("pin-outline", RealmPalette.PinOutline),
    ];

    private static readonly string Content = Build();

    private static readonly string Tag = $"\"{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Content)))}\"";

    /// <summary>The stylesheet text.</summary>
    public static string Css() => Content;

    /// <summary>A strong entity tag (quoted) taken from the hash of <see cref="Css"/>.</summary>
    public static string ETag() => Tag;

    private static string Build()
    {
        var css = new StringBuilder();
        css.Append(":root {\n");
        css.Append("  color-scheme: dark;\n");
        foreach (var (name, value) in Tokens)
        {
            css.Append("  --realm-").Append(name).Append(": ").Append(value).Append(";\n");
        }

        css.Append("}\n");
        return css.ToString();
    }
}
