using Realm.Domain;

namespace Realm.Infrastructure.Ha;

/// <summary>
/// One entity touched by a compressed diff: <paramref name="New"/> is null when the entity was removed, <paramref name="Previous"/> is null when it was
/// added and not known before (03 section 2.5).
/// </summary>
public sealed record HaStateChanged(string EntityId, HaEntitySnapshot? New, HaEntitySnapshot? Previous, DateTimeOffset ReceivedAt) : HaFeedItem(ReceivedAt);
