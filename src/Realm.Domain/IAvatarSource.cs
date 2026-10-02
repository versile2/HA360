namespace Realm.Domain;

/// <summary>
/// Resolves a member id to the bytes of that member's avatar (03 section 2.2, 10.4). It never takes a URL from the caller: the upstream is chosen
/// server-side from the member's <c>avatar</c> option, and anything that is not a raster image is refused.
/// </summary>
public interface IAvatarSource
{
    /// <summary>
    /// The avatar of <paramref name="memberId"/>, or null when there is none to show: an id that is not a member, the option <c>none</c>, an upstream
    /// that failed or sent something that is not a raster image within 2 MB. A refusal is logged once at Warning by the implementation.
    /// </summary>
    Task<AvatarImage?> GetAsync(string memberId, CancellationToken cancellationToken);
}

/// <summary>An image as it is served: its bytes, its raster content type and an entity tag that changes when the bytes do.</summary>
/// <param name="ETag">A quoted strong entity tag; null when the source gives none (the avatar service always sets it).</param>
public record AvatarImage(byte[] Bytes, string ContentType, string? ETag = null);
