using System.Buffers;
using System.Net;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Realm.Infrastructure.Hosting;

namespace Realm.Infrastructure.Ha;

/// <summary>
/// The websocket to Home Assistant Core through the Supervisor (03 section 2.5): connect, authenticate with the <c>SUPERVISOR_TOKEN</c>, turn on
/// <c>coalesce_messages</c>, subscribe to <c>subscribe_entities</c> (with <c>entity_ids</c> when a watch list is set), keep an <see cref="HaEntityStore"/>
/// from the compressed events and hand the result on as <see cref="HaFeedItem"/>s, in order, through the sink (whose back-pressure slows the reader; a
/// diff is never dropped). A ping goes out every 30 s and a missing <c>pong</c> forces a reconnect. A lost connection is retried after 1, 2, 5, 10 and then
/// 30 s (with jitter, starting over after a stable minute); <c>auth_invalid</c> is retried every 60 s; a missing token does nothing.
/// Only the state, counters and instants leave this class; the token is sent as the <c>access_token</c> of the auth message and is never logged.
/// </summary>
public sealed class HaWebSocketConnection : BackgroundService
{
    private const int ChunkBytes = 8192;

    private readonly HaWebSocketOptions _options;
    private readonly Func<HaFeedItem, CancellationToken, ValueTask> _sink;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly ResilientLoop _loop;
    private readonly HaEntityStore _store = new();
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private readonly Lock _gate = new();
    private readonly Channel<bool> _watchBell = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
        SingleReader = true,
    });

    // State and bookkeeping, all guarded by _gate.
    private HaConnectionState _state = HaConnectionState.Connecting;
    private bool _announced; // the first state is reported even when it equals the initial one
    private DateTimeOffset? _outageSince;
    private DateTimeOffset? _lastActivity;
    private DateTimeOffset? _nextAttempt;
    private int _attempt;
    private bool _everAttempted;
    private bool _episodeLogged;
    private bool _authFailureLogged;
    private string[]? _watchList;
    private int _watchVersion;
    private int _subscribedVersion;
    private int _subscribeCommandId;
    private bool _subscribeAcked;
    private int _pongId;
    private TaskCompletionSource? _pongWaiter;

    private int _commandId;
    private long _reconnects;
    private long _messages;
    private ArrayBufferWriter<byte> _assembly = new();

    /// <param name="options">Endpoint, token and timings.</param>
    /// <param name="sink">Receives every <see cref="HaFeedItem"/> in order; a slow sink slows the socket reader.</param>
    /// <param name="time">The clock of every wait and timeout (the system clock in Live, a manual one in tests).</param>
    public HaWebSocketConnection(HaWebSocketOptions options, Func<HaFeedItem, CancellationToken, ValueTask> sink, TimeProvider time, ILogger<HaWebSocketConnection> logger)
    {
        ArgumentOutOfRangeException.ThrowIfZero(options.Backoff.Count);
        _options = options;
        _sink = sink;
        _time = time;
        _logger = logger;
        _loop = new ResilientLoop(nameof(HaWebSocketConnection), logger, time);
    }

    /// <summary>
    /// Raised on every change of <see cref="HaConnectionStatus.State"/>, on the thread that made it; subscribers must be quick and must not throw. It is NOT
    /// raised when a state event or a ping reply arrives: <see cref="HaConnectionStatus.LastActivityUtc"/> of the status it carries is the activity at the
    /// moment of the change, so a consumer that wants the 90 s rule of 02 section 1.8 reads <see cref="Status"/> when it needs it.
    /// </summary>
    public event Action<HaConnectionStatus>? StatusChanged;

    public ServiceHealth Health => _loop.Health;

    /// <summary>
    /// The current state, instants and attempt counter, read live (every call builds a new record, so a state event or a ping reply that arrived a moment ago
    /// is in it); map it with <see cref="HaConnectionStatus.ToConnectionVm"/>. Safe from any thread.
    /// </summary>
    public HaConnectionStatus Status
    {
        get
        {
            lock (_gate)
            {
                return BuildStatus();
            }
        }
    }

    /// <summary>Connections lost after they had been established (diagnostics, 03 section 2.5).</summary>
    public long ReconnectCount => Interlocked.Read(ref _reconnects);

    /// <summary>Messages received after the auth handshake, since start (diagnostics).</summary>
    public long MessageCount => Interlocked.Read(ref _messages);

    /// <summary>How many entities the store holds right now.</summary>
    public int EntityCount => _store.Count;

    /// <summary>
    /// Sets the entity ids to subscribe to (sorted and de-duplicated; null or empty means no filter, so every entity). The next connection uses it; a connection
    /// that is already up unsubscribes and subscribes again when the set changed, and the new snapshot replaces what the store held.
    /// </summary>
    public void SetWatchList(IReadOnlyCollection<string>? entityIds)
    {
        var ids = entityIds is null || entityIds.Count == 0 ? null : entityIds.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        lock (_gate)
        {
            if ((_watchList ?? []).SequenceEqual(ids ?? []))
            {
                return;
            }

            _watchList = ids;
            _watchVersion++;
        }

        _watchBell.Writer.TryWrite(true);
    }

    public override void Dispose()
    {
        _sendGate.Dispose();
        base.Dispose();
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return _loop.RunAsync(RunAsync, stoppingToken);
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        // StartAsync must return at once (03 section 2.4).
        await Task.Yield();
        if (string.IsNullOrEmpty(_options.Token))
        {
            SetState(HaConnectionState.NotConfigured);
            _logger.LogInformation("No Home Assistant token is set; the websocket connection stays off");
            return;
        }

        lock (_gate)
        {
            _outageSince = _time.GetUtcNow();
        }

        var failures = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            BeginAttempt();
            var outcome = await RunSessionAsync(cancellationToken);
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            TimeSpan wait;
            if (outcome.End == SessionEnd.AuthInvalid)
            {
                wait = _options.AuthRetryInterval;
                MarkAuthFailed(wait);
            }
            else
            {
                if (outcome.ConnectedFor >= _options.StableAfter)
                {
                    failures = 0;
                }

                var schedule = _options.Backoff;
                var step = schedule[Math.Min(failures, schedule.Count - 1)];
                failures = Math.Min(failures + 1, schedule.Count);
                wait = TimeSpan.FromTicks((long)(step.Ticks * _options.Jitter()));
                MarkLost(outcome, wait);
            }

            _logger.LogDebug("Next connection attempt in {Seconds} s", wait.TotalSeconds);
            try
            {
                await Task.Delay(wait, _time, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    // One connection from the first byte to its end. Never throws: whatever goes wrong is the outcome.
    private async Task<SessionOutcome> RunSessionAsync(CancellationToken cancellationToken)
    {
        using var socket = new ClientWebSocket();
        socket.Options.CollectHttpResponseDetails = true;
        _assembly = new ArrayBufferWriter<byte>();
        Interlocked.Exchange(ref _commandId, 0); // the ids of HA's commands count per connection
        DateTimeOffset? connectedAt = null;
        try
        {
            if (!await HandshakeAsync(socket, cancellationToken))
            {
                return new SessionOutcome(SessionEnd.AuthInvalid, "auth_invalid", null);
            }

            connectedAt = _time.GetUtcNow();
            MarkConnected(connectedAt.Value);
            var reason = await ServeAsync(socket, cancellationToken);
            return new SessionOutcome(SessionEnd.Lost, reason, _time.GetUtcNow() - connectedAt.Value);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await CloseAsync(socket);
            return new SessionOutcome(SessionEnd.Stopped, "stopping", null);
        }
        catch (Exception ex)
        {
            return new SessionOutcome(SessionEnd.Lost, Describe(ex, socket), connectedAt is { } at ? _time.GetUtcNow() - at : null);
        }
    }

    // connect, auth_required, auth, auth_ok, supported_features, subscribe_entities and its result, all within the handshake budget.
    // Returns false when HA answered auth_invalid.
    private async Task<bool> HandshakeAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        using var budget = new CancellationTokenSource(_options.HandshakeTimeout, _time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, budget.Token);
        var cancel = linked.Token;
        try
        {
            await socket.ConnectAsync(_options.Endpoint, cancel);
            SetAuthenticating();

            using (var first = await ReceiveFrameAsync(socket, cancel) ?? throw new InvalidDataException("Home Assistant closed the socket during the handshake"))
            {
                if (FirstType(first.RootElement) != "auth_required")
                {
                    throw new InvalidDataException("the handshake did not start with auth_required");
                }
            }

            await SendAsync(socket, Command(writer =>
            {
                writer.WriteString("type", "auth");
                writer.WriteString("access_token", _options.Token);
            }), cancel);

            using (var reply = await ReceiveFrameAsync(socket, cancel) ?? throw new InvalidDataException("Home Assistant closed the socket during the handshake"))
            {
                switch (FirstType(reply.RootElement))
                {
                    case "auth_ok":
                        break;
                    case "auth_invalid":
                        return false;
                    default:
                        throw new InvalidDataException("unexpected answer to the auth message");
                }
            }

            await SendAsync(socket, Command(writer =>
            {
                writer.WriteNumber("id", NextCommandId());
                writer.WriteString("type", "supported_features");
                writer.WriteStartObject("features");
                writer.WriteNumber("coalesce_messages", 1);
                writer.WriteEndObject();
            }), cancel);
            await SubscribeAsync(socket, cancel);

            // The result of the subscription (and, in the same coalesced frame or right after it, the snapshot) completes the handshake.
            while (!SubscribeAcked)
            {
                using var frame = await ReceiveFrameAsync(socket, cancel) ?? throw new InvalidDataException("Home Assistant closed the socket during the handshake");
                await HandleFrameAsync(frame.RootElement, cancel);
            }

            return true;
        }
        catch (Exception) when (budget.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("the handshake took too long");
        }
    }

    // The read loop of an established connection, with the ping loop, the re-subscription loop and the stop watcher beside it. Returns why it ended.
    private async Task<string> ServeAsync(ClientWebSocket socket, CancellationToken stopping)
    {
        // Cancelling a pending receive aborts the socket, so a stop request must not reach the receive: it closes the socket properly instead
        // (CloseOnStopAsync) and the answer of HA ends the loop. Cancelling `session` is the abort.
        using var session = new CancellationTokenSource();
        using var work = CancellationTokenSource.CreateLinkedTokenSource(stopping, session.Token);
        var close = CloseOnStopAsync(socket, session, stopping);
        var ping = PingLoopAsync(socket, session, work.Token);
        var resubscribe = ResubscribeLoopAsync(socket, session, work.Token);
        string? reason = null;
        try
        {
            while (true)
            {
                using var frame = await ReceiveFrameAsync(socket, session.Token);
                if (frame is null)
                {
                    reason = "closed by Home Assistant";
                    break;
                }

                await HandleFrameAsync(frame.RootElement, work.Token);
            }
        }
        catch (OperationCanceledException) when (!stopping.IsCancellationRequested)
        {
            // A side loop ended the session; its reason is read below.
        }
        finally
        {
            await session.CancelAsync();
            await Task.WhenAll(close, ping, resubscribe);
        }

        return reason ?? await ping ?? await resubscribe ?? "the session ended";
    }

    // On a stop request: a normal close goes out while the read loop keeps reading, and HA's answer ends that loop; no answer in time aborts the socket.
    private async Task CloseOnStopAsync(ClientWebSocket socket, CancellationTokenSource session, CancellationToken stopping)
    {
        try
        {
            using var either = CancellationTokenSource.CreateLinkedTokenSource(stopping, session.Token);
            await Task.Delay(Timeout.InfiniteTimeSpan, either.Token);
        }
        catch (OperationCanceledException)
        {
            // Either a stop was requested or the session is over; the checks below tell which.
        }

        if (!stopping.IsCancellationRequested || session.IsCancellationRequested)
        {
            return;
        }

        try
        {
            using var timeout = new CancellationTokenSource(_options.CloseTimeout, _time);
            await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "stopping", timeout.Token);
            await Task.Delay(_options.CloseTimeout, _time, session.Token);
            await session.CancelAsync();
        }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException)
        {
            // The answer arrived (the read loop ended the session) or the socket is already gone.
        }
    }

    // A ping every PingInterval; no pong within PongTimeout ends the session. Returns the reason it ended it, null when it was only cancelled.
    private async Task<string?> PingLoopAsync(ClientWebSocket socket, CancellationTokenSource session, CancellationToken cancel)
    {
        try
        {
            while (true)
            {
                await Task.Delay(_options.PingInterval, _time, cancel);
                var id = NextCommandId();
                var pong = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                lock (_gate)
                {
                    _pongId = id;
                    _pongWaiter = pong;
                }

                using var timeout = new CancellationTokenSource(_options.PongTimeout, _time);
                using var either = CancellationTokenSource.CreateLinkedTokenSource(cancel, timeout.Token);
                await SendAsync(socket, Command(writer =>
                {
                    writer.WriteNumber("id", id);
                    writer.WriteString("type", "ping");
                }), cancel);
                try
                {
                    await pong.Task.WaitAsync(either.Token);
                }
                catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
                {
                    await session.CancelAsync();
                    return $"no pong within {_options.PongTimeout.TotalSeconds} s";
                }
            }
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception ex)
        {
            await session.CancelAsync();
            return Describe(ex, socket);
        }
    }

    // Unsubscribes and subscribes again on the same socket when the watch list changed.
    private async Task<string?> ResubscribeLoopAsync(ClientWebSocket socket, CancellationTokenSource session, CancellationToken cancel)
    {
        try
        {
            while (await _watchBell.Reader.WaitToReadAsync(cancel))
            {
                _watchBell.Reader.TryRead(out _);
                int previous;
                lock (_gate)
                {
                    if (_watchVersion == _subscribedVersion)
                    {
                        continue;
                    }

                    previous = _subscribeCommandId;
                }

                await SendAsync(socket, Command(writer =>
                {
                    writer.WriteNumber("id", NextCommandId());
                    writer.WriteString("type", "unsubscribe_events");
                    writer.WriteNumber("subscription", previous);
                }), cancel);
                await SubscribeAsync(socket, cancel);
            }

            return null;
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception ex)
        {
            await session.CancelAsync();
            return Describe(ex, socket);
        }
    }

    private async Task SubscribeAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var id = NextCommandId();
        string[]? entityIds;
        lock (_gate)
        {
            entityIds = _watchList;
            _subscribedVersion = _watchVersion;
            _subscribeCommandId = id;
            _subscribeAcked = false;
        }

        _store.BeginSubscription(id);
        await SendAsync(socket, Command(writer =>
        {
            writer.WriteNumber("id", id);
            writer.WriteString("type", "subscribe_entities");
            if (entityIds is not null)
            {
                writer.WriteStartArray("entity_ids");
                foreach (var entityId in entityIds)
                {
                    writer.WriteStringValue(entityId);
                }

                writer.WriteEndArray();
            }
        }), cancellationToken);
    }

    private async Task HandleFrameAsync(JsonElement frame, CancellationToken cancellationToken)
    {
        var receivedAt = _time.GetUtcNow();
        foreach (var message in HaFrame.Messages(frame))
        {
            Interlocked.Increment(ref _messages);
            switch (HaFrame.TypeOf(message))
            {
                case "event":
                    Touch(receivedAt);
                    break;
                case "pong":
                    Touch(receivedAt);
                    HandlePong(HaFrame.IdOf(message));
                    break;
                case "result":
                    HandleResult(message);
                    break;
            }
        }

        foreach (var item in _store.ApplyFrame(frame, receivedAt))
        {
            await _sink(item, cancellationToken);
        }
    }

    private void HandlePong(int? id)
    {
        TaskCompletionSource? waiter = null;
        lock (_gate)
        {
            if (_pongWaiter is not null && _pongId == id)
            {
                waiter = _pongWaiter;
                _pongWaiter = null;
            }
        }

        waiter?.TrySetResult();
    }

    private void HandleResult(JsonElement message)
    {
        lock (_gate)
        {
            if (HaFrame.IdOf(message) != _subscribeCommandId)
            {
                return;
            }

            if (message.TryGetProperty("success", out var success) && success.ValueKind == JsonValueKind.True)
            {
                _subscribeAcked = true;
                return;
            }
        }

        var code = message.TryGetProperty("error", out var error) && error.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
        throw new InvalidDataException(code is { Length: <= 64 } ? $"subscribe_entities was rejected ({code})" : "subscribe_entities was rejected");
    }

    private bool SubscribeAcked
    {
        get
        {
            lock (_gate)
            {
                return _subscribeAcked;
            }
        }
    }

    // One message, assembled from however many fragments it came in, and parsed within the limits of 03 section 2.5. Null when HA closed the socket.
    private async Task<JsonDocument?> ReceiveFrameAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        _assembly.ResetWrittenCount();
        ValueWebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(_assembly.GetMemory(ChunkBytes), cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }

            _assembly.Advance(result.Count);
            if (_assembly.WrittenCount > _options.MaxMessageBytes)
            {
                throw new InvalidDataException($"a message is larger than {_options.MaxMessageBytes} bytes");
            }
        }
        while (!result.EndOfMessage);

        if (result.MessageType != WebSocketMessageType.Text)
        {
            throw new InvalidDataException("a binary message arrived");
        }

        // The document reads the assembly buffer in place: it is disposed before the next message is received.
        return JsonDocument.Parse(_assembly.WrittenMemory, new JsonDocumentOptions { MaxDepth = _options.MaxJsonDepth });
    }

    private async Task SendAsync(ClientWebSocket socket, ReadOnlyMemory<byte> message, CancellationToken cancellationToken)
    {
        await _sendGate.WaitAsync(cancellationToken);
        try
        {
            await socket.SendAsync(message, WebSocketMessageType.Text, true, cancellationToken);
        }
        finally
        {
            _sendGate.Release();
        }
    }

    private async Task CloseAsync(ClientWebSocket socket)
    {
        if (socket.State != WebSocketState.Open)
        {
            return;
        }

        try
        {
            using var timeout = new CancellationTokenSource(_options.CloseTimeout, _time);
            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "stopping", timeout.Token);
        }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException)
        {
            // The peer did not answer the close in time or the socket is already gone: the socket is disposed either way.
        }
    }

    private int NextCommandId()
    {
        return Interlocked.Increment(ref _commandId);
    }

    private static ReadOnlyMemory<byte> Command(Action<Utf8JsonWriter> write)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            write(writer);
            writer.WriteEndObject();
        }

        return buffer.WrittenMemory;
    }

    private static string? FirstType(JsonElement frame)
    {
        foreach (var message in HaFrame.Messages(frame))
        {
            return HaFrame.TypeOf(message);
        }

        return null;
    }

    // Why a connection ended, in words that carry no frame content and so cannot carry the token.
    private static string Describe(Exception error, ClientWebSocket socket)
    {
        switch (error)
        {
            case TimeoutException:
                return "timed out";
            case InvalidDataException:
                return error.Message;
            case JsonException:
                return "a message was not valid JSON within the limits";
            case WebSocketException webSocketError:
                // A refused upgrade carries the HTTP status (a 502 while Core restarts); 101 is the status of an upgrade that worked.
                var status = (int)socket.HttpStatusCode;
                return status is not (0 or (int)HttpStatusCode.SwitchingProtocols)
                    ? $"HTTP {status}"
                    : webSocketError.InnerException?.GetType().Name ?? webSocketError.WebSocketErrorCode.ToString();
            default:
                return error.GetType().Name;
        }
    }

    // --- state ---------------------------------------------------------------------------------------------------------------------------

    private HaConnectionStatus BuildStatus()
    {
        return new HaConnectionStatus(_state, _outageSince, _lastActivity, _attempt, _nextAttempt);
    }

    private void SetState(HaConnectionState state)
    {
        HaConnectionStatus status;
        lock (_gate)
        {
            if (_state == state && _announced)
            {
                return;
            }

            _announced = true;
            _state = state;
            status = BuildStatus();
        }

        _logger.LogDebug("Home Assistant connection is now {State}", state);
        try
        {
            StatusChanged?.Invoke(status);
        }
        catch (Exception ex)
        {
            // A broken subscriber must not take the connection down; only the type is logged.
            _logger.LogWarning("A StatusChanged subscriber failed ({Error})", ex.GetType().Name);
        }
    }

    private void BeginAttempt()
    {
        HaConnectionState state;
        lock (_gate)
        {
            _attempt++;
            _nextAttempt = null;

            // After auth_invalid the state stays AuthFailed through the next attempt, so a 60 s retry does not flicker.
            state = _state == HaConnectionState.AuthFailed ? HaConnectionState.AuthFailed : _everAttempted ? HaConnectionState.Reconnecting : HaConnectionState.Connecting;
            _everAttempted = true;
        }

        SetState(state);
    }

    private void SetAuthenticating()
    {
        lock (_gate)
        {
            if (_state == HaConnectionState.AuthFailed)
            {
                return;
            }
        }

        SetState(HaConnectionState.Authenticating);
    }

    private void MarkConnected(DateTimeOffset now)
    {
        lock (_gate)
        {
            _outageSince = null;
            _lastActivity = now;
            _nextAttempt = null;
            _attempt = 0;
            _episodeLogged = false;
            _authFailureLogged = false;
        }

        _logger.LogInformation("Connected to Home Assistant");
        SetState(HaConnectionState.Connected);
    }

    private void MarkAuthFailed(TimeSpan wait)
    {
        bool first;
        lock (_gate)
        {
            _outageSince ??= _time.GetUtcNow();
            _nextAttempt = _time.GetUtcNow() + wait;
            first = !_authFailureLogged;
            _authFailureLogged = true;
        }

        if (first)
        {
            _logger.LogWarning("Home Assistant refused the token (auth_invalid); trying again every {Seconds} s", _options.AuthRetryInterval.TotalSeconds);
        }

        SetState(HaConnectionState.AuthFailed);
    }

    private void MarkLost(SessionOutcome outcome, TimeSpan wait)
    {
        var wasConnected = outcome.ConnectedFor is not null;
        bool first;
        int attempt;
        lock (_gate)
        {
            _outageSince ??= _time.GetUtcNow();
            _nextAttempt = _time.GetUtcNow() + wait;
            first = !_episodeLogged;
            _episodeLogged = true;
            attempt = _attempt;
        }

        if (wasConnected)
        {
            Interlocked.Increment(ref _reconnects);
        }

        // One Information line per outage; the attempts inside it log at Debug (a 502 while Core restarts is routine, 03 section 2.5).
        if (!first)
        {
            _logger.LogDebug("Connection attempt {Attempt} failed ({Reason})", attempt, outcome.Reason);
        }
        else if (wasConnected)
        {
            _logger.LogInformation("Lost the connection to Home Assistant ({Reason}); reconnecting", outcome.Reason);
        }
        else
        {
            _logger.LogInformation("Cannot reach Home Assistant ({Reason}); retrying", outcome.Reason);
        }

        SetState(HaConnectionState.Reconnecting);
    }

    private void Touch(DateTimeOffset now)
    {
        lock (_gate)
        {
            _lastActivity = now;
        }
    }

    private enum SessionEnd
    {
        Stopped,
        Lost,
        AuthInvalid,
    }

    // ConnectedFor is null when the connection never got as far as being established.
    private readonly record struct SessionOutcome(SessionEnd End, string Reason, TimeSpan? ConnectedFor);
}
