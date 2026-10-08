using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Realm.TestKit;

/// <summary>
/// A Home Assistant websocket on 127.0.0.1 (03 section 8.1 rule 5): Kestrel plus the protocol of research ha-addon-ingress section 4.3 as the Supervisor proxy
/// speaks it. Each connection is played by a <see cref="FakeHaSession"/>: <c>auth_required</c>, the client's <c>auth</c> checked against
/// <see cref="ExpectedToken"/> (<c>auth_ok</c> or <c>auth_invalid</c>), a result for every command, and for <c>subscribe_entities</c> the snapshot of
/// <see cref="SetEntity"/>'s entities in the compressed form (<c>s</c>, <c>a</c>, <c>c</c>, <c>lc</c>, <c>lu</c> as float epoch seconds). Pings are answered.
/// What an attempt does before that (refuse the upgrade with a 502, stay silent) is scripted with <see cref="ScriptNext"/>; a test sends later diffs,
/// drops the connection or stops the pongs through the session. The URL carries no secret and the fake never logs.
/// </summary>
public sealed class FakeHaServer : IAsyncDisposable
{
    /// <summary>The first and default instant of an entity set with <see cref="SetEntity"/>: 2025-09-30T02:40:00.123Z as epoch seconds.</summary>
    public const double DefaultEpoch = 1759200000.123;

    private readonly KestrelHost _host;
    private readonly object _gate = new();
    private readonly Queue<FakeHaAttempt> _script = new();
    private readonly SortedDictionary<string, string> _entities = new(StringComparer.Ordinal);
    private readonly Channel<FakeHaSession> _accepted = Channel.CreateUnbounded<FakeHaSession>();
    private readonly List<FakeHaSession> _sessions = [];
    private readonly List<(int Count, TaskCompletionSource Completion)> _attemptWaiters = [];
    private readonly CancellationTokenSource _stopping = new();
    private int _attempts;

    private FakeHaServer(KestrelHost host, string expectedToken)
    {
        _host = host;
        ExpectedToken = expectedToken;
        WebSocketUri = new UriBuilder(host.BaseAddress) { Scheme = "ws", Path = "core/websocket" }.Uri;
    }

    /// <summary>The URI a client connects to, <c>ws://127.0.0.1:port/core/websocket</c> (the path the Supervisor proxy serves).</summary>
    public Uri WebSocketUri { get; }

    /// <summary>The <c>access_token</c> the server accepts; a test changes it to make the next handshake pass or fail.</summary>
    public string ExpectedToken { get; set; }

    /// <summary>True sends the result of <c>subscribe_entities</c> and the snapshot together in one array frame.</summary>
    public bool CoalesceInitialReply { get; set; }

    /// <summary>
    /// What the server answers to a command it has no built-in answer for, by the command's <c>type</c>: a success carrying a result, or a failure with an error code and message.
    /// A command with no entry keeps the answer of a Home Assistant that does not know it, <c>unknown_command</c>.
    /// </summary>
    public Dictionary<string, (bool Success, string? Code, string? Message, string? ResultJson)> CommandReplies { get; } = [];

    /// <summary>Commands of these types are received and never answered (a Home Assistant that hangs).</summary>
    public HashSet<string> SilentCommands { get; } = [];

    /// <summary>Answers the command <paramref name="type"/> with a success.</summary>
    public void ReplyOk(string type, string? resultJson = null) => CommandReplies[type] = (true, null, null, resultJson);

    /// <summary>Answers the command <paramref name="type"/> with an error.</summary>
    public void ReplyError(string type, string code, string message = "Refused.") => CommandReplies[type] = (false, code, message, null);

    /// <summary>When set, the snapshot is sent as one websocket message split into fragments of this many bytes.</summary>
    public int? SnapshotFragmentBytes { get; set; }

    /// <summary>How many upgrade requests arrived so far, refused ones included.</summary>
    public int AttemptCount => Volatile.Read(ref _attempts);

    /// <summary>The sessions that were accepted, in order.</summary>
    public IReadOnlyList<FakeHaSession> Sessions
    {
        get
        {
            lock (_gate)
            {
                return _sessions.ToArray();
            }
        }
    }

    public static async Task<FakeHaServer> StartAsync(string expectedToken = "fake-token-1234")
    {
        FakeHaServer? server = null;
        var host = await KestrelHost.StartAsync(
            _ => { },
            app =>
            {
                app.UseWebSockets();
                app.Map("/core/websocket", context => server!.HandleAsync(context)); // set below, before the first request can arrive
            });
        server = new FakeHaServer(host, expectedToken);
        return server;
    }

    /// <summary>Queues what the next upgrade attempts do, one entry per attempt; once the queue is empty every attempt is accepted.</summary>
    public void ScriptNext(params FakeHaAttempt[] attempts)
    {
        lock (_gate)
        {
            foreach (var attempt in attempts)
            {
                _script.Enqueue(attempt);
            }
        }
    }

    /// <summary>
    /// Sets an entity the snapshot reports. <paramref name="attributes"/> are written as JSON; <paramref name="lastUpdated"/> is left out of the frame
    /// when null, as HA does when it equals <paramref name="lastChanged"/>.
    /// </summary>
    public void SetEntity(string entityId, string state, IReadOnlyDictionary<string, object?>? attributes = null, double lastChanged = DefaultEpoch, double? lastUpdated = null)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("s", state);
            writer.WriteStartObject("a");
            foreach (var (key, value) in attributes ?? new Dictionary<string, object?>())
            {
                writer.WritePropertyName(key);
                JsonSerializer.Serialize(writer, value);
            }

            writer.WriteEndObject();
            writer.WriteString("c", "01JCONTEXT");
            writer.WriteNumber("lc", lastChanged);
            if (lastUpdated is { } updated)
            {
                writer.WriteNumber("lu", updated);
            }

            writer.WriteEndObject();
        }

        lock (_gate)
        {
            _entities[entityId] = Encoding.UTF8.GetString(stream.ToArray());
        }
    }

    public void RemoveEntity(string entityId)
    {
        lock (_gate)
        {
            _entities.Remove(entityId);
        }
    }

    /// <summary>The next accepted session that no earlier call has returned; waits for it.</summary>
    public async Task<FakeHaSession> NextSessionAsync(CancellationToken cancellationToken = default)
    {
        return await _accepted.Reader.ReadAsync(cancellationToken);
    }

    /// <summary>Completes when at least <paramref name="count"/> upgrade attempts have arrived.</summary>
    public Task WaitForAttemptsAsync(int count)
    {
        lock (_gate)
        {
            if (AttemptCount >= count)
            {
                return Task.CompletedTask;
            }

            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _attemptWaiters.Add((count, completion));
            return completion.Task;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stopping.CancelAsync();
        foreach (var session in Sessions)
        {
            session.Abort();
        }

        await _host.DisposeAsync();
        _stopping.Dispose();
    }

    // The event body for a subscription: {"a": {entity: compressed state, ...}} for the entities that exist (and are listed, when ids are given).
    internal string SnapshotEvent(IReadOnlyList<string>? entityIds)
    {
        lock (_gate)
        {
            var wanted = _entities.Where(entity => entityIds is null || entityIds.Contains(entity.Key, StringComparer.Ordinal));
            return "{\"a\":{" + string.Join(",", wanted.Select(entity => JsonSerializer.Serialize(entity.Key) + ":" + entity.Value)) + "}}";
        }
    }

    private async Task HandleAsync(HttpContext context)
    {
        FakeHaAttempt attempt;
        int number;
        List<TaskCompletionSource> ready = [];
        lock (_gate)
        {
            number = Interlocked.Increment(ref _attempts);
            attempt = _script.Count > 0 ? _script.Dequeue() : FakeHaAttempt.Accept();
            for (var i = _attemptWaiters.Count - 1; i >= 0; i--)
            {
                if (_attemptWaiters[i].Count <= number)
                {
                    ready.Add(_attemptWaiters[i].Completion);
                    _attemptWaiters.RemoveAt(i);
                }
            }
        }

        foreach (var completion in ready)
        {
            completion.TrySetResult();
        }

        if (attempt.RejectStatus is { } status)
        {
            context.Response.StatusCode = status;
            return;
        }

        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        var session = new FakeHaSession(this, socket, number, attempt.Stall);
        lock (_gate)
        {
            _sessions.Add(session);
        }

        _accepted.Writer.TryWrite(session);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, _stopping.Token);
        await session.RunAsync(linked.Token);
    }
}
