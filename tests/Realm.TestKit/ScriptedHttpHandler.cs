using System.Net;
using System.Text;

namespace Realm.TestKit;

/// <summary>
/// An <see cref="HttpMessageHandler"/> that answers from a script and never touches the network (03 section 8.1 rules 5 and 6). Queue what the next
/// requests get with <see cref="Respond"/>, <see cref="Fail"/> or <see cref="RespondWith"/>; a request that finds the queue empty throws, so a test that
/// makes an unexpected call fails at once. Every request is recorded, with its body and the instant the injected clock gave it.
/// </summary>
public sealed class ScriptedHttpHandler : HttpMessageHandler
{
    private readonly object _gate = new();
    private readonly Queue<Func<HttpRequestMessage, Task<HttpResponseMessage>>> _script = new();
    private readonly List<ScriptedRequest> _requests = [];
    private readonly TimeProvider _time;

    /// <param name="time">Stamps the requests (<see cref="ScriptedRequest.At"/>); the system clock when null.</param>
    public ScriptedHttpHandler(TimeProvider? time = null)
    {
        _time = time ?? TimeProvider.System;
    }

    /// <summary>The requests seen so far, in order.</summary>
    public IReadOnlyList<ScriptedRequest> Requests
    {
        get
        {
            lock (_gate)
            {
                return _requests.ToArray();
            }
        }
    }

    /// <summary>The next request gets this status and body (<paramref name="mediaType"/> is the content type of a non-null body).</summary>
    public ScriptedHttpHandler Respond(HttpStatusCode status, string? body = null, string mediaType = "application/json")
    {
        return RespondWith(_ => new HttpResponseMessage(status)
        {
            Content = body is null ? null : new StringContent(body, Encoding.UTF8, mediaType),
        });
    }

    /// <summary>The next request gets the response this function builds from it.</summary>
    public ScriptedHttpHandler RespondWith(Func<HttpRequestMessage, HttpResponseMessage> response)
    {
        lock (_gate)
        {
            _script.Enqueue(request => Task.FromResult(response(request)));
        }

        return this;
    }

    /// <summary>The next request throws this exception (a refused connection, a timeout).</summary>
    public ScriptedHttpHandler Fail(Exception error)
    {
        lock (_gate)
        {
            _script.Enqueue(_ => Task.FromException<HttpResponseMessage>(error));
        }

        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var headers = request.Headers.ToDictionary(header => header.Key, header => string.Join(",", header.Value), StringComparer.OrdinalIgnoreCase);
        Func<HttpRequestMessage, Task<HttpResponseMessage>>? next;
        lock (_gate)
        {
            _requests.Add(new ScriptedRequest(request.Method, request.RequestUri, headers, body, _time.GetUtcNow()));
            _script.TryDequeue(out next);
        }

        return next is null
            ? throw new InvalidOperationException($"No response is scripted for {request.Method} {request.RequestUri}")
            : await next(request);
    }
}
