using Realm.Domain;
using Realm.Infrastructure.Ha;

namespace Realm.Infrastructure.Ingestion;

/// <summary>
/// What the ingestion pipeline's one consumer reads (03 section 2.8): a closed set. <see cref="FeedItem"/> carries what the websocket reports (a
/// <see cref="HaSnapshot"/> with replace semantics, or an <see cref="HaStateChanged"/>); <see cref="ZonesUpdated"/> and <see cref="DiscoveryUpdated"/> carry
/// what the discovery refresher found. The backfill batch of 03 section 2.8 arrives with the backfill service (S14b).
/// </summary>
public abstract record IngestItem;

/// <summary>An item of the HA websocket feed, in the order HA sent it.</summary>
public sealed record FeedItem(HaFeedItem Item) : IngestItem;

/// <summary>The zones as the five-minute zone template reports them; replaces the zone list.</summary>
public sealed record ZonesUpdated(IReadOnlyList<RawPlace> Zones) : IngestItem;

/// <summary>A discovery run's result (members, vehicles, zones, time zone); it replaces what the pipeline knows about who the members are.</summary>
public sealed record DiscoveryUpdated(HaDiscoveryResult Discovery) : IngestItem;
