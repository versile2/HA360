using System.Text.Json;

namespace Realm.Domain;

/// <summary>
/// One Home Assistant entity state, in the one shape every source is mapped to before parsing
/// (websocket store, REST bootstrap, history rows).
/// </summary>
public record HaEntitySnapshot(
    string EntityId,
    string State,
    IReadOnlyDictionary<string, JsonElement> Attributes,
    DateTimeOffset? LastChangedUtc,
    DateTimeOffset? LastUpdatedUtc);
