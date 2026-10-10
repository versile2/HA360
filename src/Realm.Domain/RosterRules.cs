using System.Globalization;

namespace Realm.Domain;

/// <summary>What discovery found in Home Assistant that can be on the map (a person, or a GPS tracker no person owns); the input of <see cref="RosterRules.Reconcile"/>.</summary>
/// <param name="Name">The name to give a new entry.</param>
/// <param name="Source">The word for where it comes from (see <see cref="RosterEntry.Source"/>).</param>
/// <param name="Active">It reports a usable state now (not unavailable or unknown).</param>
public sealed record RosterCandidate(string EntityId, RosterKind Kind, string Name, string Source, bool Active);

/// <summary>A Home Assistant persistent notification the lifecycle rules ask for. The id is stable per entity, so a repeated event replaces the notification.</summary>
public sealed record RosterNotice(string NotificationId, string Title, string Message);

/// <summary>The outcome of one <see cref="RosterRules.Reconcile"/>.</summary>
/// <param name="Entries">The whole roster after the pass, in group and sort order.</param>
/// <param name="Changed">The entries to store: new ones and those whose fields changed.</param>
/// <param name="Notices">The notifications to send; a first start has one summary, a later pass one per new or retired entry.</param>
/// <param name="VisibleChange">True when something the Settings list shows changed (a new entry, a move, a source word); a pass that only refreshed last-active times is not one.</param>
public sealed record RosterReconciliation(
    IReadOnlyList<RosterEntry> Entries,
    IReadOnlyList<RosterEntry> Changed,
    IReadOnlyList<RosterNotice> Notices,
    bool VisibleChange);

/// <summary>
/// The roster rules (D113, 02 section 2.7), pure: how a discovery pass turns the stored roster and the candidates into the next roster, and how an edit
/// (a move, a rename) changes it. No clock is read and nothing is stored.
/// </summary>
public static class RosterRules
{
    /// <summary>A tracker that reported nothing usable for this long is moved to Not tracked, if it is also gone from Home Assistant or unavailable.</summary>
    public static readonly TimeSpan InactiveAfter = TimeSpan.FromDays(7);

    /// <summary>On the first start a GPS tracker joins People only if it had a position within this long.</summary>
    public static readonly TimeSpan FirstStartWindow = TimeSpan.FromDays(30);

    /// <summary>The notification id of the first-start summary (a person's or tracker's id is <see cref="NotificationIdOf"/>).</summary>
    public const string SummaryNotificationId = "ha_cartographer_summary";

    /// <summary>The longest display name.</summary>
    public const int MaxNameLength = 32;

    /// <summary>The longest title.</summary>
    public const int MaxTitleLength = 48;

    /// <summary>The colours that new entries take in turn (01 section 7.5).</summary>
    public static readonly IReadOnlyList<string> Palette = ["#E8BC4E", "#C792EA", "#5CC8FF", "#FF8FB1", "#7EE0A5", "#FFA657", "#A5B4FC", "#F9A8D4"];

    private const string Where = "HA Cartographer → Settings → Who's on the map";

    /// <summary>The persistent notification id of an entity: <c>ha_cartographer_&lt;entity_id&gt;</c>.</summary>
    public static string NotificationIdOf(string entityId) => "ha_cartographer_" + entityId;

    /// <summary>
    /// One discovery pass. The first start (an empty roster) puts every person, and every tracker named in <paramref name="recentTrackers"/> (it had a position
    /// in the last <see cref="FirstStartWindow"/>), into People, files the other trackers under Not tracked, and asks for one summary notification. Later, an
    /// entity that is not in the roster joins People with a notification of its own. An entry in People or Vehicles that is unavailable or gone from Home
    /// Assistant keeps its place until it has been so for <see cref="InactiveAfter"/>, then moves to Not tracked with a notification. An entry the owner moved
    /// to Not tracked stays there, whatever Home Assistant says.
    /// </summary>
    /// <param name="recentTrackers">Entity ids of the trackers that had a position in the last 30 days; read only on a first start.</param>
    public static RosterReconciliation Reconcile(
        IReadOnlyList<RosterEntry> current,
        IReadOnlyList<RosterCandidate> candidates,
        IReadOnlySet<string> recentTrackers,
        DateTimeOffset now)
    {
        var entries = current.ToDictionary(e => e.EntityId, StringComparer.Ordinal);
        var found = candidates.GroupBy(c => c.EntityId, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var changed = new Dictionary<string, RosterEntry>(StringComparer.Ordinal);
        var notices = new List<RosterNotice>();
        var visible = false;
        var firstStart = current.Count == 0;
        var colour = current.Count;

        // 1. What is already in the roster.
        foreach (var entry in current)
        {
            var updated = entry;
            var candidate = found.GetValueOrDefault(entry.EntityId);
            if (candidate is not null && candidate.Source != entry.Source)
            {
                updated = updated with { Source = candidate.Source };
                visible = true;
            }

            if (candidate is not null && (entry.SourceName is not null || entry.NameOverride is not null))
            {
                // 0.2.1 (D117): the source's name follows Home Assistant and Life360; the owner's own name, if any, wins and is kept. An override equal to the source
                // (a 0.2.0 row, whose name could not be told apart) is dropped, so "customised" means what it says. An entry that knows neither (built in memory, never stored) is left alone.
                var sourceName = CleanName(candidate.Name, entry.EntityId);
                var nameOverride = string.Equals(entry.NameOverride, sourceName, StringComparison.Ordinal) ? null : entry.NameOverride;
                var name = nameOverride ?? sourceName;
                if (entry.SourceName != sourceName || entry.NameOverride != nameOverride || entry.DisplayName != name)
                {
                    updated = updated with { SourceName = sourceName, NameOverride = nameOverride, DisplayName = name };
                    visible |= entry.DisplayName != name;
                }
            }

            if (entry.Group != RosterGroup.NotTracked)
            {
                if (candidate is { Active: true })
                {
                    updated = updated with { LastActiveUtc = now };
                }
                else if (now - entry.LastActiveUtc >= InactiveAfter)
                {
                    updated = updated with
                    {
                        Group = RosterGroup.NotTracked,
                        SortOrder = NextOrder(entries.Values, RosterGroup.NotTracked),
                        AutoMovedUtc = now,
                    };
                    notices.Add(new RosterNotice(
                        NotificationIdOf(entry.EntityId),
                        $"HA Cartographer: {entry.DisplayName} moved to Not tracked",
                        $"{entry.DisplayName} ({entry.EntityId}) has been removed, disabled or unavailable for 7 days, so it is no longer on the map. Move it back in {Where}."));
                    visible = true;
                }
            }

            if (updated != entry)
            {
                entries[entry.EntityId] = updated;
                changed[entry.EntityId] = updated;
            }
        }

        // 2. What is new: persons first, then trackers, each by entity id.
        var added = new List<RosterEntry>();
        var skipped = 0;
        foreach (var candidate in found.Values.Where(c => !entries.ContainsKey(c.EntityId)).OrderBy(c => c.Kind).ThenBy(c => c.EntityId, StringComparer.Ordinal))
        {
            var dormant = firstStart && candidate.Kind == RosterKind.Tracker && !recentTrackers.Contains(candidate.EntityId);
            var group = dormant ? RosterGroup.NotTracked : RosterGroup.People;
            var entry = new RosterEntry(
                candidate.EntityId,
                candidate.Kind,
                group,
                CleanName(candidate.Name, candidate.EntityId),
                null,
                Palette[colour++ % Palette.Count],
                NextOrder(entries.Values, group),
                candidate.Source,
                now,
                now,
                null)
            {
                SourceName = CleanName(candidate.Name, candidate.EntityId),
            };
            entry = entry with { SourceColor = entry.Color };
            entries[entry.EntityId] = entry;
            changed[entry.EntityId] = entry;
            visible = true;
            if (dormant)
            {
                skipped++;
                continue;
            }

            added.Add(entry);
            if (!firstStart)
            {
                var what = entry.Kind == RosterKind.Person ? "person" : "tracker";
                notices.Add(new RosterNotice(
                    NotificationIdOf(entry.EntityId),
                    $"HA Cartographer: New {what} found",
                    $"{entry.DisplayName} ({entry.EntityId}) was added to People. Open {Where} to move it to Trackers or Not tracked."));
            }
        }

        if (firstStart && (added.Count > 0 || skipped > 0))
        {
            var people = added.Count(e => e.Kind == RosterKind.Person);
            var trackers = added.Count - people;
            var message = $"HA Cartographer found {Count(people, "person", "people")} and {Count(trackers, "GPS tracker", "GPS trackers")} and put them on the map.";
            if (skipped > 0)
            {
                message += $" {Count(skipped, "tracker", "trackers")} with no position in the last 30 days {(skipped == 1 ? "was" : "were")} left in Not tracked.";
            }

            notices.Add(new RosterNotice(SummaryNotificationId, "HA Cartographer: Who's on the map", message + $" Change this any time in {Where}."));
        }

        return new RosterReconciliation(Sorted(entries.Values), [.. changed.Values.OrderBy(e => e.EntityId, StringComparer.Ordinal)], notices, visible);
    }

    /// <summary>The roster after the entry was put into <paramref name="group"/> at <paramref name="index"/>; every group is renumbered 0, 1, 2. Unknown id: the same roster.</summary>
    public static IReadOnlyList<RosterEntry> Move(IReadOnlyList<RosterEntry> entries, string entityId, RosterGroup group, int? index)
    {
        var moving = entries.FirstOrDefault(e => e.EntityId == entityId);
        if (moving is null)
        {
            return entries;
        }

        var target = Sorted(entries).Where(e => e.Group == group && e.EntityId != entityId).ToList();
        var at = index is { } wanted ? Math.Clamp(wanted, 0, target.Count) : target.Count;
        var moved = moving with
        {
            Group = group,
            AutoMovedUtc = group == RosterGroup.NotTracked && moving.Group == RosterGroup.NotTracked ? moving.AutoMovedUtc : null,
        };
        target.Insert(at, moved);

        var result = new List<RosterEntry>(entries.Count);
        foreach (var other in entries.Where(e => e.Group != group && e.EntityId != entityId).GroupBy(e => e.Group))
        {
            result.AddRange(Renumber(Sorted(other)));
        }

        result.AddRange(Renumber(target));
        return Sorted(result);
    }

    /// <summary>The roster after <see cref="IRosterEditor.UpdateAsync"/>'s rules were applied to one entry (its picture is left as it is). Unknown id: the same roster.</summary>
    public static IReadOnlyList<RosterEntry> Update(IReadOnlyList<RosterEntry> entries, string entityId, string displayName, string? loreTitle, string? color) =>
        Edit(entries, entityId, displayName, loreTitle, color, entries.FirstOrDefault(e => e.EntityId == entityId)?.Icon);

    /// <summary>
    /// The roster after <see cref="IRosterEditor.EditAsync"/>'s rules were applied to one entry: the name, title, colour and picture are stored as the owner's own values
    /// (an override), but only where they differ from the source's, and the values in effect follow. Unknown id: the same roster.
    /// </summary>
    public static IReadOnlyList<RosterEntry> Edit(IReadOnlyList<RosterEntry> entries, string entityId, string displayName, string? loreTitle, string? color, string? icon)
    {
        var result = new List<RosterEntry>(entries.Count);
        foreach (var entry in entries)
        {
            if (entry.EntityId != entityId)
            {
                result.Add(entry);
                continue;
            }

            var sourceName = entry.SourceName ?? entry.DisplayName;
            var sourceColor = entry.SourceColor ?? entry.Color;
            var name = CleanName(displayName, entry.DisplayName);
            var title = CleanTitle(loreTitle);
            var chosen = IsColor(color) ? color!.ToUpperInvariant() : entry.Color;
            result.Add(entry with
            {
                DisplayName = name,
                LoreTitle = title,
                Color = chosen,
                NameOverride = string.Equals(name, sourceName, StringComparison.Ordinal) ? null : name,
                TitleOverride = string.Equals(title, entry.SourceTitle, StringComparison.Ordinal) ? null : title ?? string.Empty,
                ColorOverride = string.Equals(chosen, sourceColor, StringComparison.OrdinalIgnoreCase) ? null : chosen,
                Icon = RosterIcons.IsValid(icon) ? icon : null,
            });
        }

        return result;
    }

    /// <summary>The roster after "Reset to Home Assistant / Life360": the owner's name, title, colour and picture are cleared and the source's values are in effect. Unknown id: the same roster.</summary>
    public static IReadOnlyList<RosterEntry> Reset(IReadOnlyList<RosterEntry> entries, string entityId)
    {
        var result = new List<RosterEntry>(entries.Count);
        foreach (var entry in entries)
        {
            result.Add(entry.EntityId != entityId
                ? entry
                : entry with
                {
                    DisplayName = entry.SourceName ?? entry.DisplayName,
                    LoreTitle = entry.SourceTitle,
                    Color = entry.SourceColor ?? entry.Color,
                    NameOverride = null,
                    TitleOverride = null,
                    ColorOverride = null,
                    Icon = null,
                });
        }

        return result;
    }

    /// <summary>All entries, People first, then Trackers, then Not tracked, each by sort order and then entity id.</summary>
    public static List<RosterEntry> Sorted(IEnumerable<RosterEntry> entries) =>
        entries.OrderBy(e => e.Group).ThenBy(e => e.SortOrder).ThenBy(e => e.EntityId, StringComparer.Ordinal).ToList();

    /// <summary>True for a colour written <c>#RRGGBB</c>.</summary>
    public static bool IsColor(string? color) =>
        color is { Length: 7 } && color[0] == '#' && color.Skip(1).All(Uri.IsHexDigit);

    /// <summary>The name trimmed to <see cref="MaxNameLength"/> characters; <paramref name="fallback"/> (cleaned the same way) when it is blank.</summary>
    public static string CleanName(string? name, string fallback)
    {
        var text = name?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            text = fallback.Trim();
        }

        return text.Length > MaxNameLength ? text[..MaxNameLength].TrimEnd() : text;
    }

    /// <summary>The title trimmed to <see cref="MaxTitleLength"/> characters; null when it is blank.</summary>
    public static string? CleanTitle(string? title)
    {
        var text = title?.Trim() ?? string.Empty;
        return text.Length == 0 ? null : text.Length > MaxTitleLength ? text[..MaxTitleLength].TrimEnd() : text;
    }

    private static int NextOrder(IEnumerable<RosterEntry> entries, RosterGroup group)
    {
        var last = -1;
        foreach (var entry in entries)
        {
            if (entry.Group == group && entry.SortOrder > last)
            {
                last = entry.SortOrder;
            }
        }

        return last + 1;
    }

    private static List<RosterEntry> Renumber(List<RosterEntry> group)
    {
        for (var i = 0; i < group.Count; i++)
        {
            group[i] = group[i] with { SortOrder = i };
        }

        return group;
    }

    private static string Count(int number, string one, string many) =>
        string.Create(CultureInfo.InvariantCulture, $"{number} {(number == 1 ? one : many)}");
}
