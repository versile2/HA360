using System.Text.RegularExpressions;

namespace Realm.Infrastructure.Avatars;

/// <summary>
/// What a member's picture reference (an <c>entity_picture</c> of HA) may be (03 section 10.4): an HA <c>image/serve/{id}/{size}</c> path, fetched through
/// the Supervisor, or a Life360 HTTPS URL as discovered by HA. Anything else is not a servable avatar. Pure: no I/O.
/// </summary>
public static partial class AvatarUpstream
{
    /// <summary>The form of an HA image path: with or without a leading slash and <c>api/</c>.</summary>
    public static bool IsHaImagePath(string? value) => value is not null && HaImage().IsMatch(value.Trim());

    /// <summary>An absolute HTTPS URL on <c>life360.com</c> or one of its subdomains, with no credentials in it.</summary>
    public static bool IsLife360Url(string? value, out Uri uri)
    {
        if (Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var parsed)
            && parsed.Scheme == Uri.UriSchemeHttps
            && string.IsNullOrEmpty(parsed.UserInfo)
            && (parsed.Host.Equals("life360.com", StringComparison.OrdinalIgnoreCase) || parsed.Host.EndsWith(".life360.com", StringComparison.OrdinalIgnoreCase)))
        {
            uri = parsed;
            return true;
        }

        uri = null!;
        return false;
    }

    /// <summary>True when <paramref name="value"/> is one of the two servable forms.</summary>
    public static bool IsServable(string? value) => IsHaImagePath(value) || IsLife360Url(value, out _);

    [GeneratedRegex("^/?(?:api/)?image/serve/[A-Za-z0-9_-]{1,64}/[A-Za-z0-9_-]{1,32}$", RegexOptions.CultureInvariant)]
    private static partial Regex HaImage();
}
