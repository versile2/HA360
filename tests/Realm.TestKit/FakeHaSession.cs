using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Realm.TestKit;

/// <summary>
/// One websocket connection accepted by <see cref="FakeHaServer"/>. The server plays the protocol by itself (<c>auth_required</c>, <c>auth_ok</c> or
/// <c>auth_invalid</c>, a result for every command, the snapshot after <c>subscribe_entities</c>, a <c>pong</c> for every <c>ping</c>); the session lets a
/// test see what the client sent and push frames, drop the connection or stop answering pings.
/// </summary>
public sealed class FakeHaSession
{
    private readonly WebSocket _socket;
    private readonly FakeHaServer _server;
    private readonly bool _silent;
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private readonly object _gate = new();
    private readonly List<(string Type, string Raw)> _received = [];
    private readonly List<(string Type, int Index, TaskCompletionSource<string> Completion)> _waiters = [];
    private readonly TaskCompletionSource _ended = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _subscriptionId;
    private volatile bool _answerPings = true;

    internal FakeHaSession(FakeHaServer server, WebSocket socket, int attempt, bool silent)
    {
        _server = server;
        _socket = socket;
        _silent = silent;
        Attempt = attempt;
    }

    /// <summary>The number of the upgrade attempt that made this session (1 for the first).</summary>
    public int Attempt { get; }

    /// <summary>False: <c>ping</c> commands are received but never answered.</summary>
    public bool AnswerPings
    {
        get => _answerPings;
        set => _answerPings = value;
    }

    /// <summary>The command id of the latest <c>subscribe_entities</c> (0 before there was one); events are sent with it.</summary>
    public int SubscriptionId => Volatile.Read(ref _subscriptionId);

    /// <summary>What the client sent as the close status when it closed the socket, or null while it has not.</summary>
    public WebSocketCloseStatus? ClientCloseStatus { get; private set; }

    /// <summary>Completes when the connection is over, whoever ended it.</summary>
    public Task Ended => _ended.Task;

    /// <summary>The raw text of every message the client sent, in order.</summary>
    public IReadOnlyList<string> Received
    {
        get
        {
            lock (_gate)
            {
                return _received.Select(message => message.Raw).ToArray();
            }
        }
    }

    /// <summary>The <c>type</c> of every message the client sent, in order.</summary>
    public IReadOnlyList<string> ReceivedTypes
    {
        get
        {
            lock (_gate)
            {
                return _received.Select(message => message.Type).ToArray();
            }
        }
    }

    /// <summary>The raw text of the (<paramref name="index"/> + 1)th message of this <c>type</c> the client sends, as soon as it has.</summary>
    public Task<string> WaitForMessageAsync(string type, int index = 0)
    {
        lock (_gate)
        {
            var seen = _received.Where(message => message.Type == type).Skip(index).FirstOrDefault();
            if (seen.Raw is not null)
            {
                return Task.FromResult(seen.Raw);
            }

            var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            _waiters.Add((type, index, completion));
            return completion.Task;
        }
    }

    /// <summary>Sends an <c>event</c> message of the latest subscription whose body is <paramref name="eventJson"/> (for example <c>{"r":["zone.home"]}</c>).</summary>
    public Task SendEventAsync(string eventJson) => SendTextAsync(EventMessage(eventJson));

    /// <summary>Sends one frame that is a JSON array of <c>event</c> messages: what <c>coalesce_messages</c> allows.</summary>
    public Task SendCoalescedAsync(params string[] eventJsons) => SendTextAsync("[" + string.Join(",", eventJsons.Select(EventMessage)) + "]");

    /// <summary>Sends exactly this text as one websocket message.</summary>
    public Task SendTextAsync(string text) => SendFragmentedAsync(text, int.MaxValue);

    /// <summary>Sends this text as one websocket message split into fragments of at most <paramref name="fragmentBytes"/> bytes.</summary>
    public async Task SendFragmentedAsync(string text, int fragmentBytes)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        await _sendGate.WaitAsync();
        try
        {
            for (var offset = 0; ; offset += fragmentBytes)
            {
                var count = (int)Math.Min(fragmentBytes, (long)bytes.Length - offset);
                var last = offset + count >= bytes.Length;
                await _socket.SendAsync(bytes.AsMemory(offset, count), WebSocketMessageType.Text, last, CancellationToken.None);
                if (last)
                {
                    return;
                }
            }
        }
        finally
        {
            _sendGate.Release();
        }
    }

    /// <summary>Drops the connection without a close handshake, as a crash or a cut cable does.</summary>
    public void Abort() => _socket.Abort();

    /// <summary>Starts a clean close from the server's side.</summary>
    public Task CloseAsync() => _socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "closing", CancellationToken.None);

    internal async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (_silent)
            {
                while (await ReceiveAsync(cancellationToken) is not null)
                {
                    // Anything the client sends is ignored.
                }

                return;
            }

            await SendTextAsync("""{"type":"auth_required","ha_version":"2026.9.3"}""");
            var auth = await ReceiveAsync(cancellationToken);
            if (auth is null)
            {
                return;
            }

            using (var document = JsonDocument.Parse(auth))
            {
                var token = document.RootElement.TryGetProperty("access_token", out var value) ? value.GetString() : null;
                if (token != _server.ExpectedToken)
                {
                    await SendTextAsync("""{"type":"auth_invalid","message":"Invalid access"}""");
                    await CloseAsync();
                    return;
                }
            }

            await SendTextAsync("""{"type":"auth_ok","ha_version":"2026.9.3"}""");
            while (await ReceiveAsync(cancellationToken) is { } text)
            {
                await AnswerAsync(text);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException or ObjectDisposedException)
        {
            // The client or the test ended the connection.
        }
        finally
        {
            _ended.TrySetResult();
        }
    }

    private async Task AnswerAsync(string text)
    {
        using var document = JsonDocument.Parse(text);
        var message = document.RootElement;
        var type = message.TryGetProperty("type", out var typeValue) ? typeValue.GetString() ?? string.Empty : string.Empty;
        var id = message.TryGetProperty("id", out var idValue) && idValue.TryGetInt32(out var number) ? number : 0;
        switch (type)
        {
            case "supported_features":
            case "unsubscribe_events":
                await SendTextAsync($$"""{"id":{{id}},"type":"result","success":true,"result":null}""");
                break;
            case "subscribe_entities":
                Volatile.Write(ref _subscriptionId, id);
                IReadOnlyList<string>? ids = message.TryGetProperty("entity_ids", out var list)
                    ? list.EnumerateArray().Select(entity => entity.GetString() ?? string.Empty).ToArray()
                    : null;
                var result = $$"""{"id":{{id}},"type":"result","success":true,"result":null}""";
                var snapshot = _server.SnapshotEvent(ids);
                if (_server.CoalesceInitialReply)
                {
                    await SendTextAsync("[" + result + "," + EventMessage(snapshot) + "]");
                }
                else
                {
                    await SendTextAsync(result);
                    await SendFragmentedAsync(EventMessage(snapshot), _server.SnapshotFragmentBytes ?? int.MaxValue);
                }

                break;
            case "ping":
                if (_answerPings)
                {
                    await SendTextAsync($$"""{"id":{{id}},"type":"pong"}""");
                }

                break;
            default:
                if (_server.SilentCommands.Contains(type))
                {
                    break;
                }

                if (_server.CommandReplies.TryGetValue(type, out var reply))
                {
                    var body = reply.Success
                        ? "\"success\":true,\"result\":" + (reply.ResultJson ?? "null")
                        : "\"success\":false,\"error\":{\"code\":\"" + reply.Code + "\",\"message\":\"" + reply.Message + "\"}";
                    await SendTextAsync("{\"id\":" + id + ",\"type\":\"result\"," + body + "}");
                    break;
                }

                await SendTextAsync("{\"id\":" + id + ",\"type\":\"result\",\"success\":false,\"error\":{\"code\":\"unknown_command\",\"message\":\"Unknown command.\"}}");
                break;
        }
    }

    // One complete text message from the client, or null when the client closed the socket (its close status is kept).
    private async Task<string?> ReceiveAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        using var assembled = new MemoryStream();
        while (true)
        {
            var result = await _socket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                ClientCloseStatus = result.CloseStatus;

                // HA answers a close with a close: the client's clean shutdown waits for it.
                if (_socket.State == WebSocketState.CloseReceived)
                {
                    await _socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "closing", CancellationToken.None);
                }

                return null;
            }

            assembled.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
            {
                break;
            }
        }

        var text = Encoding.UTF8.GetString(assembled.ToArray());
        Record(text);
        return text;
    }

    private void Record(string text)
    {
        string type;
        using (var document = JsonDocument.Parse(text))
        {
            type = document.RootElement.TryGetProperty("type", out var value) ? value.GetString() ?? string.Empty : string.Empty;
        }

        List<TaskCompletionSource<string>> ready = [];
        lock (_gate)
        {
            _received.Add((type, text));
            var count = _received.Count(message => message.Type == type);
            for (var i = _waiters.Count - 1; i >= 0; i--)
            {
                if (_waiters[i].Type == type && _waiters[i].Index == count - 1)
                {
                    ready.Add(_waiters[i].Completion);
                    _waiters.RemoveAt(i);
                }
            }
        }

        foreach (var completion in ready)
        {
            completion.TrySetResult(text);
        }
    }

    private string EventMessage(string eventJson) => $$"""{"id":{{SubscriptionId}},"type":"event","event":{{eventJson}}}""";
}
