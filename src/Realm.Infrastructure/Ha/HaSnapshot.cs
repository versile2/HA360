using Realm.Domain;

namespace Realm.Infrastructure.Ha;

/// <summary>
/// The complete set of watched entities as HA reported it right after a subscription (initial or after a reconnect). It has replace semantics:
/// an entity the consumer knew before and that is not in <paramref name="Entities"/> is gone. Entities are ordered by id.
/// </summary>
public sealed record HaSnapshot(IReadOnlyList<HaEntitySnapshot> Entities, DateTimeOffset ReceivedAt) : HaFeedItem(ReceivedAt);
