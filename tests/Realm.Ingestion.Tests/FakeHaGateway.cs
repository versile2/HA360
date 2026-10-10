using Realm.Domain;

namespace Realm.Ingestion.Tests;

/// <summary>
/// An <see cref="IHaGateway"/> that answers from fields and counts what it is asked, so a test of the discovery refresher or the avatar service needs no
/// socket and no HTTP. A call that a test did not prepare for throws, so an unexpected call fails at once.
/// </summary>
internal sealed class FakeHaGateway : IHaGateway
{
    private readonly object _gate = new();
    private readonly List<IReadOnlyCollection<string>?> _watchLists = [];
    private readonly List<HistoryCall> _historyCalls = [];
    private int _imageCalls;
    private int _configCalls;
    private int _stateCalls;
    private int _zoneCalls;
    private int _integrationCalls;
    private readonly List<Notification> _notifications = [];

    public event Action? Connected;

    public Func<string, int, CancellationToken, Task<AvatarImage?>>? Image { get; set; }

    public HaConfig Config { get; set; } = new("UTC", "2026.9.1");

    public IReadOnlyList<HaEntitySnapshot> States { get; set; } = [];

    public HaIntegrationEntities Integration { get; set; } = new([], []);

    public IReadOnlyList<RawPlace> Zones { get; set; } = [];

    /// <summary>Answers a history request (entity, start, end, with attributes); a request while it is not set throws, so an unexpected one fails at once.</summary>
    public Func<string, DateTimeOffset, DateTimeOffset, bool, IReadOnlyList<HaEntitySnapshot>>? History { get; set; }

    /// <summary>When set, <see cref="GetConfigAsync"/> throws it (Home Assistant is down).</summary>
    public Exception? Failure { get; set; }

    /// <summary>The zones <see cref="CreateZoneAsync"/> was asked to create, in order.</summary>
    public List<NewZone> CreatedZones { get; } = [];

    /// <summary>When set, <see cref="CreateZoneAsync"/> throws it (Home Assistant refused).</summary>
    public Exception? CreateZoneFailure { get; set; }

    public Task CreateZoneAsync(NewZone zone, CancellationToken ct)
    {
        lock (_gate)
        {
            CreatedZones.Add(zone);
        }

        return CreateZoneFailure is { } failure ? Task.FromException(failure) : Task.CompletedTask;
    }

    public int ImageCalls => Volatile.Read(ref _imageCalls);

    public int ConfigCalls => Volatile.Read(ref _configCalls);

    public int StateCalls => Volatile.Read(ref _stateCalls);

    public int ZoneCalls => Volatile.Read(ref _zoneCalls);

    public int IntegrationCalls => Volatile.Read(ref _integrationCalls);

    /// <summary>When set, <see cref="NotifyAsync"/> throws it (the notification could not be created).</summary>
    public Exception? NotifyFailure { get; set; }

    /// <summary>Every notification that was created, in order (a failed one is not in the list).</summary>
    public IReadOnlyList<Notification> Notifications
    {
        get
        {
            lock (_gate)
            {
                return _notifications.ToArray();
            }
        }
    }

    /// <summary>Every watch list that was set, in order (null for a cleared one).</summary>
    public IReadOnlyList<IReadOnlyCollection<string>?> WatchLists
    {
        get
        {
            lock (_gate)
            {
                return _watchLists.ToArray();
            }
        }
    }

    /// <summary>Every history request that was made, in order, including the ones that failed.</summary>
    public IReadOnlyList<HistoryCall> HistoryCalls
    {
        get
        {
            lock (_gate)
            {
                return _historyCalls.ToArray();
            }
        }
    }

    public void RaiseConnected() => Connected?.Invoke();

    public void SetWatchList(IReadOnlyCollection<string>? entityIds)
    {
        lock (_gate)
        {
            _watchLists.Add(entityIds?.ToArray());
        }
    }

    public Task<HaConfig> GetConfigAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _configCalls);
        return Failure is { } failure ? Task.FromException<HaConfig>(failure) : Task.FromResult(Config);
    }

    public Task<IReadOnlyList<HaEntitySnapshot>> GetStatesAsync(Func<string, bool>? include, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _stateCalls);
        IReadOnlyList<HaEntitySnapshot> states = [.. States.Where(state => include?.Invoke(state.EntityId) ?? true)];
        return Task.FromResult(states);
    }

    public Task<HaIntegrationEntities> GetIntegrationEntitiesAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _integrationCalls);
        return Task.FromResult(Integration);
    }

    public Task<IReadOnlyList<RawPlace>> GetZonesAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _zoneCalls);
        return Task.FromResult(Zones);
    }

    public Task<IReadOnlyList<HaEntitySnapshot>> GetHistoryAsync(string entityId, DateTimeOffset start, DateTimeOffset end, bool withAttributes, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _historyCalls.Add(new HistoryCall(entityId, start, end, withAttributes));
        }

        return History is { } history
            ? Task.FromResult(history(entityId, start, end, withAttributes))
            : throw new InvalidOperationException("No history call is expected");
    }

    public Task NotifyAsync(string notificationId, string title, string message, CancellationToken cancellationToken)
    {
        if (NotifyFailure is { } failure)
        {
            return Task.FromException(failure);
        }

        lock (_gate)
        {
            _notifications.Add(new Notification(notificationId, title, message));
        }

        return Task.CompletedTask;
    }

    public Task<AvatarImage?> GetImageAsync(string pathAndQuery, int maxBytes, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _imageCalls);
        return Image is { } image ? image(pathAndQuery, maxBytes, cancellationToken) : throw new InvalidOperationException("No image call is expected");
    }

    /// <summary>One persistent notification as it was created.</summary>
    internal sealed record Notification(string Id, string Title, string Message);

    /// <summary>One history request as the backfill made it.</summary>
    internal sealed record HistoryCall(string EntityId, DateTimeOffset Start, DateTimeOffset End, bool WithAttributes);
}
