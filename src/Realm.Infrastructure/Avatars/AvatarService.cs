using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Realm.Domain;
using Realm.Infrastructure.Diagnostics;
using Realm.Infrastructure.Ha;

namespace Realm.Infrastructure.Avatars;

/// <summary>
/// The avatar proxy (03 section 10.4, the simple version of D41). It takes a member id and nothing else: the picture it fetches is the one the member's
/// <c>avatar</c> option chose when discovery ran (<see cref="ResolvedMember.AvatarUpstream"/>), either an HA <c>image/serve/...</c> path, fetched through
/// <see cref="IHaGateway.GetImageAsync"/> with the Supervisor's relative base address, or a Life360 HTTPS URL, fetched with a client that has no token and
/// does not follow redirects. Only raster images (JPEG, PNG, WebP, GIF, AVIF; SVG is refused) of at most 2 MB are served, and the bytes must look like the
/// image the content type claims. Every refusal or upstream error answers "no avatar" and logs one Warning; the UI then draws initials.
/// </summary>
/// <remarks>
/// The cache is one file per member, <c>{cacheDirectory}/{MemberId}</c>, overwritten when it is 24 hours old (the file's modification time is set from the
/// injected clock). The content type is not stored: it is read back from the bytes, and the entity tag is a hash of them. A refusal is remembered per member
/// for the same period (R3-08): the upstream is not asked again, and nothing more is logged, until it is over. A stale file is served while a refresh fails
/// or the member is in that period, instead of being dropped.
/// </remarks>
public sealed partial class AvatarService : IAvatarSource
{
    /// <summary>The size limit of an avatar (03 section 10.4).</summary>
    public const int MaxBytes = 2 * 1024 * 1024;

    /// <summary>How long a cached avatar is used before it is fetched again (02 section 2.6).</summary>
    public static readonly TimeSpan RevalidateAfter = TimeSpan.FromHours(24);

    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static readonly string[] RasterTypes = ["image/jpeg", "image/png", "image/webp", "image/gif", "image/avif"];

    private readonly DiscoveryState _discovery;
    private readonly IHaGateway _gateway;
    private readonly HttpClient _life360;
    private readonly string _cacheDirectory;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly ServiceCounters? _counters;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _refusedAt = new(StringComparer.Ordinal);

    /// <param name="discovery">Who the members are and which picture each one shows.</param>
    /// <param name="gateway">Fetches the pictures of the HA kind.</param>
    /// <param name="life360">Fetches the pictures of the Life360 kind. It must not follow redirects and must not carry any credential; it is never used for an HA path.</param>
    /// <param name="cacheDirectory">Where the one file per member lives (<c>/data/cache/avatars</c> in the add-on).</param>
    /// <param name="time">The clock of the 24 hour revalidation.</param>
    /// <param name="logger">Where each refusal is logged once (the member id and a fixed reason).</param>
    /// <param name="counters">Where fetches and failures are counted for <c>diagnostics.json</c>; null counts nothing.</param>
    public AvatarService(
        DiscoveryState discovery,
        IHaGateway gateway,
        HttpClient life360,
        string cacheDirectory,
        TimeProvider time,
        ILogger<AvatarService> logger,
        ServiceCounters? counters = null)
    {
        _discovery = discovery;
        _gateway = gateway;
        _life360 = life360;
        _cacheDirectory = cacheDirectory;
        _time = time;
        _logger = logger;
        _counters = counters;
    }

    /// <inheritdoc />
    public async Task<AvatarImage?> GetAsync(string memberId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(memberId);

        // The id is checked against the member id form first and then against the members themselves, so nothing a client sends reaches a path or a URL.
        if (!IdForm().IsMatch(memberId)
            || _discovery.Current.Members.FirstOrDefault(m => string.Equals(m.Id, memberId, StringComparison.Ordinal)) is not { AvatarUpstream: { } upstream })
        {
            _logger.LogWarning("An avatar was requested for a member that has none");
            return null;
        }

        var gate = _gates.GetOrAdd(memberId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (ReadCache(memberId, allowStale: false) is { } cached)
            {
                return cached;
            }

            // R3-08: a refusal stands for the revalidation period; the stale file (if any) is what is served meanwhile.
            if (_refusedAt.TryGetValue(memberId, out var refused) && _time.GetUtcNow() - refused is { } since && since >= TimeSpan.Zero && since < RevalidateAfter)
            {
                return ReadCache(memberId, allowStale: true);
            }

            var fetched = await FetchAsync(memberId, upstream, cancellationToken);
            if (fetched is not null)
            {
                _refusedAt.TryRemove(memberId, out _);
                WriteCache(memberId, fetched.Bytes);
                return fetched;
            }

            _refusedAt[memberId] = _time.GetUtcNow();
            return ReadCache(memberId, allowStale: true);
        }
        finally
        {
            gate.Release();
        }
    }

    // ---- fetching -----------------------------------------------------------------------------------------------

    private async Task<AvatarImage?> FetchAsync(string memberId, string upstream, CancellationToken cancellationToken)
    {
        _counters?.RecordAvatarFetch();
        try
        {
            Raw raw;
            if (AvatarUpstream.IsHaImagePath(upstream))
            {
                var image = await _gateway.GetImageAsync(upstream, MaxBytes, cancellationToken);
                raw = image is null ? Raw.Failed("the upstream did not answer 200") : new Raw(image.Bytes, image.ContentType, null);
            }
            else if (AvatarUpstream.IsLife360Url(upstream, out var uri))
            {
                raw = await FetchLife360Async(uri, cancellationToken);
            }
            else
            {
                raw = Raw.Failed("the picture is not an HA image path or a Life360 address");
            }

            if (raw.Failure is not null)
            {
                return Refused(memberId, raw.Failure);
            }

            var bytes = raw.Bytes!;
            if (!RasterTypes.Contains(MediaType(raw.ContentType), StringComparer.Ordinal))
            {
                return Refused(memberId, "the content type is not a raster image type");
            }

            if (bytes.Length > MaxBytes)
            {
                return Refused(memberId, "the image is larger than 2 MB");
            }

            return Sniff(bytes) is { } served
                ? new AvatarImage(bytes, served, ETagOf(bytes))
                : Refused(memberId, "the bytes are not a raster image");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (InvalidDataException)
        {
            return Refused(memberId, "the image is larger than 2 MB");
        }
        catch (Exception ex)
        {
            // Only the type of the exception: a message can carry the address that failed.
            return Refused(memberId, "the upstream failed (" + ex.GetType().Name + ")");
        }
    }

    private async Task<Raw> FetchLife360Async(Uri uri, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        using var response = await _life360.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            // A redirect is a failure on purpose (03 section 10.4): the client was told not to follow it, and a 3xx is never 200.
            return Raw.Failed(((int)response.StatusCode) is >= 300 and < 400 ? "the upstream redirected" : "the upstream did not answer 200");
        }

        if (response.Content.Headers.ContentLength > MaxBytes)
        {
            return Raw.Failed("the image is larger than 2 MB");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var bytes = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (bytes.Length + read > MaxBytes)
            {
                return Raw.Failed("the image is larger than 2 MB");
            }

            bytes.Write(buffer, 0, read);
        }

        return new Raw(bytes.ToArray(), response.Content.Headers.ContentType?.MediaType, null);
    }

    private AvatarImage? Refused(string memberId, string reason)
    {
        _counters?.RecordAvatarFailure();
        _logger.LogWarning("The avatar of member {MemberId} was not served: {Reason}", memberId, reason);
        return null;
    }

    private static string MediaType(string? contentType)
    {
        var text = contentType ?? string.Empty;
        var semicolon = text.IndexOf(';', StringComparison.Ordinal);
        return (semicolon < 0 ? text : text[..semicolon]).Trim().ToLowerInvariant();
    }

    // ---- the cache ----------------------------------------------------------------------------------------------

    private string PathOf(string memberId) => Path.Combine(_cacheDirectory, memberId);

    // A fresh file that still holds a raster image, or null (missing, 24 hours old, unreadable or not an image: all of them mean "fetch it"). With
    // allowStale the age is ignored: the file of an earlier fetch is better than no picture when the upstream refuses.
    private AvatarImage? ReadCache(string memberId, bool allowStale)
    {
        try
        {
            var path = PathOf(memberId);
            if (!File.Exists(path))
            {
                return null;
            }

            var written = new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero);
            var age = _time.GetUtcNow() - written;
            if (!allowStale && (age >= RevalidateAfter || age < TimeSpan.FromMinutes(-5)))
            {
                return null;
            }

            var bytes = File.ReadAllBytes(path);
            return bytes.Length is > 0 and <= MaxBytes && Sniff(bytes) is { } type ? new AvatarImage(bytes, type, ETagOf(bytes)) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // Written beside the target and moved over it, so a reader never sees half a file. A cache that cannot be written costs only the next fetch.
    private void WriteCache(string memberId, byte[] bytes)
    {
        try
        {
            Directory.CreateDirectory(_cacheDirectory);
            var path = PathOf(memberId);
            var temp = path + ".tmp";
            File.WriteAllBytes(temp, bytes);
            File.SetLastWriteTimeUtc(temp, _time.GetUtcNow().UtcDateTime);
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning("The avatar of member {MemberId} could not be cached ({ErrorType})", memberId, ex.GetType().Name);
        }
    }

    // ---- what an image looks like -------------------------------------------------------------------------------

    private static string ETagOf(byte[] bytes) => "\"" + Convert.ToHexString(SHA256.HashData(bytes))[..32].ToLowerInvariant() + "\"";

    /// <summary>The raster content type the signature of <paramref name="bytes"/> shows, or null when it is not one of the five.</summary>
    internal static string? Sniff(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            return "image/jpeg";
        }

        if (bytes.Length >= 8 && bytes[..8].SequenceEqual(PngSignature))
        {
            return "image/png";
        }

        if (bytes.Length >= 6 && (bytes[..6].SequenceEqual("GIF87a"u8) || bytes[..6].SequenceEqual("GIF89a"u8)))
        {
            return "image/gif";
        }

        if (bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes[8..12].SequenceEqual("WEBP"u8))
        {
            return "image/webp";
        }

        if (bytes.Length >= 12 && bytes[4..8].SequenceEqual("ftyp"u8) && (bytes[8..12].SequenceEqual("avif"u8) || bytes[8..12].SequenceEqual("avis"u8)))
        {
            return "image/avif";
        }

        return null;
    }

    // \z, not $: "king\n" is not a member id.
    [GeneratedRegex(@"^[a-z][a-z0-9_]{0,23}\z", RegexOptions.CultureInvariant)]
    private static partial Regex IdForm();

    private sealed record Raw(byte[]? Bytes, string? ContentType, string? Failure)
    {
        public static Raw Failed(string reason) => new(null, null, reason);
    }
}
