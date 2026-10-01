using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Realm.Domain;

namespace Realm.Infrastructure.Ha;

/// <summary>
/// The websocket's picture of the watched entities, kept by applying the compressed <c>subscribe_entities</c> events (03 section 2.5, research
/// ha-addon-ingress section 4.3): <c>a</c> adds an entity, <c>c</c> changes one (<c>+</c> carries the changed fields, with a partial attribute map to merge,
/// <c>-.a</c> lists attribute keys to delete) and <c>r</c> removes entities; <c>lc</c> and <c>lu</c> are float epoch seconds. Every entity is kept as an
/// immutable <see cref="HaEntitySnapshot"/>, so what was handed out never changes under a consumer. The first event of a subscription is a snapshot
/// that replaces everything. Safe to call from several threads.
/// </summary>
public sealed class HaEntityStore
{
    private const string Unknown = "unknown";

    private static readonly double MinMilliseconds = DateTimeOffset.MinValue.ToUnixTimeMilliseconds();

    private static readonly double MaxMilliseconds = DateTimeOffset.MaxValue.ToUnixTimeMilliseconds();

    private readonly object _gate = new();
    private readonly Dictionary<string, HaEntitySnapshot> _entities = new(StringComparer.Ordinal);
    private int _subscriptionId;
    private bool _snapshotPending;

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _entities.Count;
            }
        }
    }

    public bool TryGet(string entityId, [NotNullWhen(true)] out HaEntitySnapshot? snapshot)
    {
        lock (_gate)
        {
            return _entities.TryGetValue(entityId, out snapshot);
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _entities.Clear();
        }
    }

    /// <summary>
    /// A subscription with this command id was sent: its first event is a snapshot that replaces the store, and events of any other
    /// subscription (an older one that is still being cancelled) are ignored from now on.
    /// </summary>
    public void BeginSubscription(int subscriptionId)
    {
        lock (_gate)
        {
            _subscriptionId = subscriptionId;
            _snapshotPending = true;
        }
    }

    /// <summary>
    /// Applies the <c>event</c> messages of the current subscription in a frame (an object or an array of messages), in order. The first one after
    /// <see cref="BeginSubscription"/> yields an <see cref="HaSnapshot"/>, the others one <see cref="HaStateChanged"/> per touched entity.
    /// Messages of other types and of other subscriptions are skipped.
    /// </summary>
    public IReadOnlyList<HaFeedItem> ApplyFrame(JsonElement frame, DateTimeOffset receivedAt)
    {
        List<HaFeedItem> items = [];
        foreach (var message in HaFrame.Messages(frame))
        {
            if (HaFrame.TypeOf(message) != "event" || !message.TryGetProperty("event", out var body))
            {
                continue;
            }

            lock (_gate)
            {
                if (HaFrame.IdOf(message) != _subscriptionId)
                {
                    continue;
                }

                if (_snapshotPending)
                {
                    _snapshotPending = false;
                    items.Add(ReplaceCore(body, receivedAt));
                }
                else
                {
                    items.AddRange(ApplyCore(body, receivedAt));
                }
            }
        }

        return items;
    }

    /// <summary>Applies one event body (<c>{"a":…,"c":…,"r":…}</c>): adds, then changes, then removals.</summary>
    public IReadOnlyList<HaStateChanged> Apply(JsonElement eventBody, DateTimeOffset receivedAt)
    {
        lock (_gate)
        {
            return ApplyCore(eventBody, receivedAt);
        }
    }

    /// <summary>Drops everything and loads one event body as the new state: what a fresh snapshot means after a reconnect (03 section 2.5).</summary>
    public HaSnapshot Replace(JsonElement eventBody, DateTimeOffset receivedAt)
    {
        lock (_gate)
        {
            return ReplaceCore(eventBody, receivedAt);
        }
    }

    private HaSnapshot ReplaceCore(JsonElement body, DateTimeOffset receivedAt)
    {
        _entities.Clear();
        ApplyCore(body, receivedAt);
        var entities = _entities.Values.OrderBy(entity => entity.EntityId, StringComparer.Ordinal).ToArray();
        return new HaSnapshot(entities, receivedAt);
    }

    private List<HaStateChanged> ApplyCore(JsonElement body, DateTimeOffset receivedAt)
    {
        List<HaStateChanged> changes = [];
        if (body.ValueKind != JsonValueKind.Object)
        {
            return changes;
        }

        if (body.TryGetProperty("a", out var adds) && adds.ValueKind == JsonValueKind.Object)
        {
            foreach (var added in adds.EnumerateObject())
            {
                var entity = FromCompressed(added.Name, added.Value);
                _entities.TryGetValue(added.Name, out var previous);
                _entities[added.Name] = entity;
                changes.Add(new HaStateChanged(added.Name, entity, previous, receivedAt));
            }
        }

        if (body.TryGetProperty("c", out var diffs) && diffs.ValueKind == JsonValueKind.Object)
        {
            foreach (var diff in diffs.EnumerateObject())
            {
                // A change for an entity this store never saw added cannot be applied; the next snapshot repairs it.
                if (diff.Value.ValueKind == JsonValueKind.Object && _entities.TryGetValue(diff.Name, out var previous))
                {
                    var entity = Merge(previous, diff.Value);
                    _entities[diff.Name] = entity;
                    changes.Add(new HaStateChanged(diff.Name, entity, previous, receivedAt));
                }
            }
        }

        if (body.TryGetProperty("r", out var removals) && removals.ValueKind == JsonValueKind.Array)
        {
            foreach (var removed in removals.EnumerateArray())
            {
                if (removed.ValueKind == JsonValueKind.String && removed.GetString() is { } id && _entities.Remove(id, out var previous))
                {
                    changes.Add(new HaStateChanged(id, null, previous, receivedAt));
                }
            }
        }

        return changes;
    }

    // {"s": state, "a": {attributes}, "c": context, "lc": float, "lu": float}; "lu" is left out when it equals "lc".
    private static HaEntitySnapshot FromCompressed(string entityId, JsonElement value)
    {
        var state = value.TryGetProperty("s", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() ?? Unknown : Unknown;
        var attributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (value.TryGetProperty("a", out var a) && a.ValueKind == JsonValueKind.Object)
        {
            foreach (var attribute in a.EnumerateObject())
            {
                attributes[attribute.Name] = attribute.Value.Clone();
            }
        }

        var changed = value.TryGetProperty("lc", out var lc) ? ToInstant(lc) : null;
        var updated = value.TryGetProperty("lu", out var lu) ? ToInstant(lu) : changed;
        return new HaEntitySnapshot(entityId, state, attributes, changed, updated);
    }

    // {"+": {"s", "a" (partial), "lc" or "lu", "c"}, "-": {"a": [keys to delete]}}
    private static HaEntitySnapshot Merge(HaEntitySnapshot previous, JsonElement diff)
    {
        var state = previous.State;
        var changed = previous.LastChangedUtc;
        var updated = previous.LastUpdatedUtc;
        Dictionary<string, JsonElement>? attributes = null;

        if (diff.TryGetProperty("+", out var plus) && plus.ValueKind == JsonValueKind.Object)
        {
            if (plus.TryGetProperty("s", out var s) && s.ValueKind == JsonValueKind.String)
            {
                state = s.GetString() ?? state;
            }

            if (plus.TryGetProperty("a", out var a) && a.ValueKind == JsonValueKind.Object)
            {
                attributes = new Dictionary<string, JsonElement>(previous.Attributes, StringComparer.Ordinal);
                foreach (var attribute in a.EnumerateObject())
                {
                    attributes[attribute.Name] = attribute.Value.Clone();
                }
            }

            // A changed "lc" is sent instead of "lu": the entity was also updated at that instant.
            if (plus.TryGetProperty("lc", out var lc) && ToInstant(lc) is { } lastChanged)
            {
                changed = lastChanged;
                updated = lastChanged;
            }

            if (plus.TryGetProperty("lu", out var lu) && ToInstant(lu) is { } lastUpdated)
            {
                updated = lastUpdated;
            }
        }

        if (diff.TryGetProperty("-", out var minus) && minus.ValueKind == JsonValueKind.Object
            && minus.TryGetProperty("a", out var gone) && gone.ValueKind == JsonValueKind.Array)
        {
            attributes ??= new Dictionary<string, JsonElement>(previous.Attributes, StringComparer.Ordinal);
            foreach (var key in gone.EnumerateArray())
            {
                if (key.ValueKind == JsonValueKind.String && key.GetString() is { } name)
                {
                    attributes.Remove(name);
                }
            }
        }

        return previous with { State = state, Attributes = attributes ?? previous.Attributes, LastChangedUtc = changed, LastUpdatedUtc = updated };
    }

    // Float epoch seconds to an instant, rounded to the millisecond (02 section 1.3); anything that is not a usable number is unknown.
    private static DateTimeOffset? ToInstant(JsonElement seconds)
    {
        if (seconds.ValueKind != JsonValueKind.Number || !seconds.TryGetDouble(out var value) || !double.IsFinite(value))
        {
            return null;
        }

        var milliseconds = Math.Round(value * 1000.0, MidpointRounding.AwayFromZero);
        return milliseconds >= MinMilliseconds && milliseconds <= MaxMilliseconds ? DateTimeOffset.FromUnixTimeMilliseconds((long)milliseconds) : null;
    }
}
