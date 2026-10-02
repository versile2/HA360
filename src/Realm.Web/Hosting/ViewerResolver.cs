using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Realm.Domain;

namespace Realm.Web.Hosting;

/// <summary>
/// Who "me" is, resolved once per page load in static SSR (03 section 5.5, R-082): the chain around <see cref="IRealmSession.ResolveMe"/>, in order, the person link of the HA user
/// (<see cref="ViewerSource.Person"/>), the optional <c>me_fallback_member</c> (<c>Me:Fallback</c>, <see cref="ViewerSource.Fallback"/>), the first live member in sort order
/// (<see cref="ViewerSource.FirstLive"/>), and nobody (<see cref="ViewerSource.None"/>, when there are no members).
/// </summary>
/// <remarks>
/// <para>
/// The HA user id is an argument of <see cref="Resolve"/> and nothing more: it is never kept, never logged and never part of what is returned. Only the member slug leaves, and
/// the log line says <c>viewer=&lt;member id&gt; via=&lt;source&gt;</c> (D38). The data comes from a session of the host's default mode that this scoped service creates when it is first asked and
/// disposes with the request, as prerendering does for a page: the session is cheap and holds nothing until someone subscribes (03 section 2.2).
/// </para>
/// </remarks>
public sealed partial class ViewerResolver : IViewerResolver, IAsyncDisposable
{
    /// <summary>The configuration key of the optional fallback member (the add-on option <c>me_fallback_member</c>).</summary>
    public const string FallbackKey = "Me:Fallback";

    private readonly IRealmSessionFactory _sessions;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ViewerResolver> _logger;
    private IRealmSession? _session;

    /// <summary>Creates the resolver of one request scope.</summary>
    public ViewerResolver(IRealmSessionFactory sessions, IConfiguration configuration, ILogger<ViewerResolver> logger)
    {
        _sessions = sessions;
        _configuration = configuration;
        _logger = logger;
    }

    /// <inheritdoc />
    public ViewerResolution Resolve(string? haUserId)
    {
        var session = _session ??= _sessions.Create(null);
        var members = session.Current.Members;
        var resolution = Chain(session, members, haUserId);
        LogResolution(_logger, resolution.MemberId ?? "none", resolution.Source);
        return resolution;
    }

    /// <summary>Disposes the session this resolver created, if any.</summary>
    public ValueTask DisposeAsync()
    {
        var session = _session;
        _session = null;
        return session?.DisposeAsync() ?? ValueTask.CompletedTask;
    }

    private ViewerResolution Chain(IRealmSession session, IReadOnlyList<MemberVm> members, string? haUserId)
    {
        if (session.ResolveMe(haUserId) is { Length: > 0 } person)
        {
            return new ViewerResolution(person, ViewerSource.Person);
        }

        // A fallback that names no member is no fallback: "me" would then be the first live member anyway, and the log should say so.
        var fallback = _configuration[FallbackKey]?.Trim();
        if (!string.IsNullOrEmpty(fallback) && members.Any(member => member.Id == fallback))
        {
            return new ViewerResolution(fallback, ViewerSource.Fallback);
        }

        var first = members.Where(member => member.Kind == MemberKind.Live).OrderBy(member => member.SortOrder).FirstOrDefault();
        return first is null ? new ViewerResolution(null, ViewerSource.None) : new ViewerResolution(first.Id, ViewerSource.FirstLive);
    }

    [LoggerMessage(EventId = 7001, Level = LogLevel.Debug, Message = "viewer={MemberId} via={Source}")]
    private static partial void LogResolution(ILogger logger, string memberId, ViewerSource source);
}
