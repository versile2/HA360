namespace Realm.Infrastructure.Ha;

/// <summary>
/// What <see cref="HaWebSocketConnection"/> hands to the ingestion layer (03 section 2.5): an <see cref="HaSnapshot"/> after every (re)subscription and an
/// <see cref="HaStateChanged"/> for every entity a later diff touches. Items are delivered in the order HA sent them; diffs are order-dependent.
/// </summary>
public abstract record HaFeedItem(DateTimeOffset ReceivedAt);
