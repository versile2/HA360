using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Realm.Web.Hosting;

/// <summary>
/// Sets <c>Request.PathBase</c> from the <c>X-Ingress-Path</c> header (03 section 5.2). The Supervisor has already stripped the
/// prefix from the path, so <c>Request.Path</c> is left alone (unlike <c>UsePathBase</c>). A value is accepted when it starts with
/// <c>/</c> and has no whitespace, quote or angle bracket; anything else is ignored with one Warning per distinct value.
/// </summary>
public sealed partial class IngressPathBase
{
    private const string HeaderName = "X-Ingress-Path";
    private const int MaxRememberedValues = 16;

    private readonly RequestDelegate _next;
    private readonly ILogger<IngressPathBase> _logger;
    private readonly ConcurrentDictionary<string, bool> _warned = new();

    public IngressPathBase(RequestDelegate next, ILogger<IngressPathBase> logger)
    {
        _next = next;
        _logger = logger;
    }

    public Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue(HeaderName, out var header))
        {
            var value = header.ToString();
            if (value.Length > 0)
            {
                if (IsValid(value))
                {
                    var pathBase = value.TrimEnd('/');
                    if (pathBase.Length > 0)
                    {
                        context.Request.PathBase = new PathString(pathBase);
                    }
                }
                else if (_warned.Count < MaxRememberedValues && _warned.TryAdd(value, true))
                {
                    LogIgnored(_logger, value);
                }
            }
        }

        return _next(context);
    }

    private static bool IsValid(string value)
    {
        if (value[0] != '/')
        {
            return false;
        }

        foreach (var c in value)
        {
            if (char.IsWhiteSpace(c) || c is '"' or '\'' or '<' or '>')
            {
                return false;
            }
        }

        return true;
    }

    [LoggerMessage(EventId = 6001, Level = LogLevel.Warning,
        Message = "Ignoring X-Ingress-Path value '{Value}': it must start with '/' and contain no whitespace, quote or angle bracket.")]
    private static partial void LogIgnored(ILogger logger, string value);
}
