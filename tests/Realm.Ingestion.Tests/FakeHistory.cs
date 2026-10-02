using System.Text.Json;
using Realm.Domain;

namespace Realm.Ingestion.Tests;

/// <summary>
/// What Home Assistant's history would answer, from rows a test puts in. A request gets the rows of the entity whose update time is in
/// [start, end), oldest first, preceded (as Home Assistant does with <c>include_start_time_state</c>) by the state at the start: the newest earlier row,
/// stamped with the start. A request without attributes gets rows without them.
/// </summary>
internal sealed class FakeHistory
{
    private static readonly IReadOnlyDictionary<string, JsonElement> NoAttributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal);

    private readonly List<HaEntitySnapshot> _rows = [];

    public void Add(IEnumerable<HaEntitySnapshot> rows) => _rows.AddRange(rows);

    public IReadOnlyList<HaEntitySnapshot> Answer(string entityId, DateTimeOffset start, DateTimeOffset end, bool withAttributes)
    {
        var mine = _rows.Where(row => string.Equals(row.EntityId, entityId, StringComparison.Ordinal)).OrderBy(row => row.LastUpdatedUtc).ToList();
        var answer = new List<HaEntitySnapshot>();
        if (mine.LastOrDefault(row => row.LastUpdatedUtc < start) is { } before)
        {
            answer.Add(before with { LastChangedUtc = start, LastUpdatedUtc = start });
        }

        answer.AddRange(mine.Where(row => row.LastUpdatedUtc >= start && row.LastUpdatedUtc < end));
        return withAttributes ? answer : [.. answer.Select(row => row with { Attributes = NoAttributes })];
    }
}
