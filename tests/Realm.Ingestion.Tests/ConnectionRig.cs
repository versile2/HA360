using Realm.Infrastructure.Ha;
using Realm.TestKit;

namespace Realm.Ingestion.Tests;

/// <summary>
/// A <see cref="HaWebSocketConnection"/> wired to a <see cref="FakeHaServer"/> over real loopback sockets and to a <see cref="ManualTimeProvider"/>, with
/// every feed item and state change recorded. The connection waits on the manual clock and on real I/O, so a test waits for what it needs (a state, items, a
/// timer of a known length) and then moves time; each wait has a real-time watchdog that fails with the evidence instead of hanging.
/// </summary>
internal sealed class ConnectionRig : IAsyncDisposable
{
    public const string Token = "fake-token-1234";

    public static readonly DateTimeOffset Start = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);

    // A hang guard only: nothing in a passing test waits this long.
    private static readonly TimeSpan Watchdog = TimeSpan.FromSeconds(30);

    private readonly object _gate = new();
    private readonly List<HaFeedItem> _items = [];
    private readonly List<HaConnectionStatus> _history = [];
    private readonly List<(Func<bool> Ready, TaskCompletionSource Completion)> _waiters = [];
    private int _cursor;

    private ConnectionRig(FakeHaServer server, HaWebSocketOptions options)
    {
        Server = server;
        Options = options;
        Time = new ManualTimeProvider(Start);
        Log = new RecordingLogger<HaWebSocketConnection>();
        Connection = new HaWebSocketConnection(options, OnItem, Time, Log);
        Connection.StatusChanged += OnStatus;
    }

    public FakeHaServer Server { get; }

    public HaWebSocketOptions Options { get; }

    public ManualTimeProvider Time { get; }

    public RecordingLogger<HaWebSocketConnection> Log { get; }

    public HaWebSocketConnection Connection { get; }

    /// <summary>The states the connection went through, in order.</summary>
    public IReadOnlyList<HaConnectionState> StateHistory
    {
        get
        {
            lock (_gate)
            {
                return _history.Select(status => status.State).ToArray();
            }
        }
    }

    /// <param name="seed">Sets the entities the fake server reports.</param>
    /// <param name="token">The token the connection sends; the server accepts <see cref="Token"/>.</param>
    /// <param name="configure">
    /// Changes the options (the endpoint and the token are already set; the jitter is already 1 and the handshake budget is 5 minutes, so that a timer of
    /// 10 s on the manual clock is a back-off wait or a pong wait and never the handshake budget).
    /// </param>
    /// <param name="start">False leaves the connection stopped, so the test can set a watch list first.</param>
    public static async Task<ConnectionRig> StartAsync(
        Action<FakeHaServer>? seed = null,
        string? token = Token,
        Func<HaWebSocketOptions, HaWebSocketOptions>? configure = null,
        bool start = true)
    {
        var server = await FakeHaServer.StartAsync(Token);
        seed?.Invoke(server);
        var options = new HaWebSocketOptions { Endpoint = server.WebSocketUri, Token = token, Jitter = () => 1.0, HandshakeTimeout = TimeSpan.FromMinutes(5) };
        var rig = new ConnectionRig(server, configure is null ? options : configure(options));
        if (start)
        {
            await rig.Connection.StartAsync(CancellationToken.None);
        }

        return rig;
    }

    public Task<FakeHaSession> NextSessionAsync() => Guard(Server.NextSessionAsync(), "the next accepted session");

    /// <summary>Waits for the first not yet awaited state change to <paramref name="state"/>; states are awaited in order.</summary>
    public async Task<HaConnectionStatus> WaitForStateAsync(HaConnectionState state)
    {
        var found = -1;
        await Guard(
            WaitUntil(() =>
            {
                for (var i = _cursor; i < _history.Count; i++)
                {
                    if (_history[i].State == state)
                    {
                        found = i;
                        return true;
                    }
                }

                return false;
            }),
            $"state {state}");
        lock (_gate)
        {
            _cursor = found + 1;
            return _history[found];
        }
    }

    /// <summary>Waits until at least <paramref name="count"/> feed items arrived and returns all of them.</summary>
    public async Task<IReadOnlyList<HaFeedItem>> WaitForItemsAsync(int count)
    {
        await Guard(WaitUntil(() => _items.Count >= count), $"{count} feed items");
        lock (_gate)
        {
            return _items.ToArray();
        }
    }

    /// <summary>Waits until a timer that was set for exactly <paramref name="dueTime"/> is waiting on the manual clock.</summary>
    public Task TimerAsync(TimeSpan dueTime) => Guard(Time.WaitForTimerAsync(dueTime), $"a timer of {dueTime}");

    public async Task<T> Guard<T>(Task<T> task, string what)
    {
        await Guard((Task)task, what);
        return await task;
    }

    public async Task Guard(Task task, string what)
    {
        try
        {
            await task.WaitAsync(Watchdog);
        }
        catch (TimeoutException)
        {
            throw new TimeoutException(
                $"Timed out waiting for {what}. States: [{string.Join(", ", StateHistory)}]; upgrade attempts: {Server.AttemptCount}; "
                + $"timers waiting: {Time.ActiveTimerCount}; log:\n{Log.Text}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        using var stopping = new CancellationTokenSource(Watchdog);
        await Connection.StopAsync(stopping.Token);
        Connection.Dispose();
        await Server.DisposeAsync();
    }

    private ValueTask OnItem(HaFeedItem item, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _items.Add(item);
        }

        Notify();
        return ValueTask.CompletedTask;
    }

    private void OnStatus(HaConnectionStatus status)
    {
        lock (_gate)
        {
            _history.Add(status);
        }

        Notify();
    }

    // The condition is evaluated under the lock that guards the lists it reads.
    private Task WaitUntil(Func<bool> ready)
    {
        lock (_gate)
        {
            if (ready())
            {
                return Task.CompletedTask;
            }

            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _waiters.Add((ready, completion));
            return completion.Task;
        }
    }

    private void Notify()
    {
        List<TaskCompletionSource> ready = [];
        lock (_gate)
        {
            for (var i = _waiters.Count - 1; i >= 0; i--)
            {
                if (_waiters[i].Ready())
                {
                    ready.Add(_waiters[i].Completion);
                    _waiters.RemoveAt(i);
                }
            }
        }

        foreach (var completion in ready)
        {
            completion.TrySetResult();
        }
    }
}
