namespace Realm.Domain;

/// <summary>
/// The door to Home Assistant (03 section 2.2): the entity watch list in, and the REST calls that make up the complete list of 02 section 10.3
/// (config, states, template, history, image: all read-only; and, since 0.2.0, the one call that writes: a persistent notification). The live state stream (<c>HaStateChanged</c>) is not a member: it is an Infrastructure record that
/// the websocket hands to the ingestion pipeline directly, so the Domain stays free of it. A failed call throws; the retries of 502, 503 and 504, timeouts
/// and refused connections are done inside.
/// </summary>
public interface IHaGateway
{
    /// <summary>Raised each time the websocket reaches Connected; the discovery refresher refreshes the zones then (03 section 2.4). Raised on the connection's thread.</summary>
    event Action? Connected;

    /// <summary>
    /// Sets the entity ids to watch (zones included, or they never update live, R-096). Null or empty means no filter. A connection that is
    /// already up subscribes again when the set changed.
    /// </summary>
    void SetWatchList(IReadOnlyCollection<string>? entityIds);

    /// <summary><c>GET config</c>: the time zone and the version.</summary>
    Task<HaConfig> GetConfigAsync(CancellationToken cancellationToken);

    /// <summary><c>GET states</c>, one snapshot per entity for which <paramref name="include"/> returns true (all when it is null).</summary>
    Task<IReadOnlyList<HaEntitySnapshot>> GetStatesAsync(Func<string, bool>? include, CancellationToken cancellationToken);

    /// <summary>The discovery template of 02 section 1.2 step 2: the entities of the life360 and mobile_app integrations.</summary>
    Task<HaIntegrationEntities> GetIntegrationEntitiesAsync(CancellationToken cancellationToken);

    /// <summary>The zone template of 02 section 1.2 step 7: every zone with its position and radius.</summary>
    Task<IReadOnlyList<RawPlace>> GetZonesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// <c>GET history/period</c> of one entity in [<paramref name="start"/>, <paramref name="end"/>), oldest first, in the one snapshot shape. With
    /// <paramref name="withAttributes"/> false (sensors) the rows carry no attributes. Requests are spaced 250 ms apart (02 section 1.1).
    /// </summary>
    Task<IReadOnlyList<HaEntitySnapshot>> GetHistoryAsync(string entityId, DateTimeOffset start, DateTimeOffset end, bool withAttributes, CancellationToken cancellationToken);

    /// <summary>
    /// <c>POST services/persistent_notification/create</c> (the one call of the add-on that changes anything in Home Assistant, 02 section 10.3): creates the
    /// notification <paramref name="notificationId"/>, or replaces it when it exists, so a repeated event never piles up. The id is the caller's stable name
    /// (<c>ha_cartographer_&lt;entity_id&gt;</c>). Never called in Demo.
    /// </summary>
    Task NotifyAsync(string notificationId, string title, string message, CancellationToken cancellationToken);

    /// <summary>
    /// <c>GET</c> of an <c>image/serve/...</c> path (a leading <c>/api/</c> is accepted): the bytes and content type, or null when HA does not answer 200.
    /// Any other path is refused with an <see cref="ArgumentException"/>; a body above <paramref name="maxBytes"/> throws an <see cref="InvalidDataException"/>.
    /// </summary>
    Task<AvatarImage?> GetImageAsync(string pathAndQuery, int maxBytes, CancellationToken cancellationToken);
}

/// <summary>The part of <c>GET config</c> the add-on uses.</summary>
/// <param name="TimeZone">HA's IANA time zone id.</param>
/// <param name="Version">HA's version text, null when absent.</param>
public record HaConfig(string TimeZone, string? Version);

/// <summary>The entity ids that <c>integration_entities</c> reports for the two integrations the add-on reads (02 section 1.2 step 2).</summary>
public record HaIntegrationEntities(
    IReadOnlyList<string> Life360,
    IReadOnlyList<string> MobileApp);
