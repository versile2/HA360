using Realm.Domain;

namespace Realm.Infrastructure.Ha;

/// <summary>
/// <see cref="IHaGateway"/> over the two halves that exist (03 section 2.2): the websocket takes the watch list and reports Connected, the REST client makes
/// the read-only calls. A thin composition, no logic of its own.
/// </summary>
public sealed class HaGateway : IHaGateway, IDisposable
{
    private readonly HaWebSocketConnection _connection;
    private readonly HaRestClient _rest;
    private HaConnectionState _lastState = HaConnectionState.Connecting;

    public HaGateway(HaWebSocketConnection connection, HaRestClient rest)
    {
        _connection = connection;
        _rest = rest;
        _connection.StatusChanged += OnStatus;
    }

    /// <inheritdoc />
    public event Action? Connected;

    /// <inheritdoc />
    public void SetWatchList(IReadOnlyCollection<string>? entityIds) => _connection.SetWatchList(entityIds);

    /// <inheritdoc />
    public Task<HaConfig> GetConfigAsync(CancellationToken cancellationToken) => _rest.GetConfigAsync(cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<HaEntitySnapshot>> GetStatesAsync(Func<string, bool>? include, CancellationToken cancellationToken) =>
        _rest.GetStatesAsync(include, cancellationToken);

    /// <inheritdoc />
    public Task<HaIntegrationEntities> GetIntegrationEntitiesAsync(CancellationToken cancellationToken) => _rest.GetIntegrationEntitiesAsync(cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<RawPlace>> GetZonesAsync(CancellationToken cancellationToken) => _rest.GetZonesAsync(cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<HaEntitySnapshot>> GetHistoryAsync(string entityId, DateTimeOffset start, DateTimeOffset end, bool withAttributes, CancellationToken cancellationToken) =>
        _rest.GetHistoryAsync(entityId, start, end, withAttributes, cancellationToken);

    /// <inheritdoc />
    public Task<AvatarImage?> GetImageAsync(string pathAndQuery, int maxBytes, CancellationToken cancellationToken) =>
        _rest.GetImageAsync(pathAndQuery, maxBytes, cancellationToken);

    public void Dispose() => _connection.StatusChanged -= OnStatus;

    // The event is raised on the connection's thread for every state change; Connected is raised on the way into the state, not on every change inside it.
    private void OnStatus(HaConnectionStatus status)
    {
        var entered = status.State == HaConnectionState.Connected && _lastState != HaConnectionState.Connected;
        _lastState = status.State;
        if (entered)
        {
            Connected?.Invoke();
        }
    }
}
