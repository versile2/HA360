using Realm.Domain;
using Realm.Web.Formatting;

namespace Realm.Web.Components.History;

/// <summary>One row of the timeline list: a day heading, a place visit or a drive, or a note ("No data"). Every row is the same height, which is what lets the list be virtualized.</summary>
public abstract record HistoryRow(string Key)
{
    /// <summary>The height of every row, CSS pixels (realm-history.css reads the same number).</summary>
    public const int HeightPx = 72;

    /// <summary>The rows of one day: its entries, or a note when it has none.</summary>
    public static IEnumerable<HistoryRow> OfDay(HistoryDayVm day, string name)
    {
        ArgumentNullException.ThrowIfNull(day);
        if (day.Entries.Count == 0)
        {
            yield return new NoteRow("note-" + HistoryDayMath.Iso(day.Day), HistoryFormatter.EmptyDay(day, name));
            yield break;
        }

        foreach (var entry in day.Entries)
        {
            yield return new EntryRow(HistoryDayMath.Iso(day.Day) + "-" + entry.Id, entry, day.Day);
        }
    }

    /// <summary>The rows of the range view: a heading for each day (newest first), then its entries or its note.</summary>
    public static List<HistoryRow> OfRange(IReadOnlyList<HistoryDayVm> days, string name, DateOnly today, UnitSystem units)
    {
        ArgumentNullException.ThrowIfNull(days);
        var rows = new List<HistoryRow>();
        foreach (var day in days)
        {
            rows.Add(new DayHeaderRow("day-" + HistoryDayMath.Iso(day.Day), day.Day, HistoryFormatter.DayHeading(day.Day, today), HistoryFormatter.DaySummary(day, units)));
            rows.AddRange(OfDay(day, name));
        }

        return rows;
    }
}

/// <summary>The heading of a day in the range view; a link to that day.</summary>
public sealed record DayHeaderRow(string Key, DateOnly Day, string Heading, string Summary) : HistoryRow(Key);

/// <summary>A visit or a drive.</summary>
public sealed record EntryRow(string Key, HistoryEntry Entry, DateOnly Day) : HistoryRow(Key);

/// <summary>A sentence in the list: the day has nothing to show.</summary>
public sealed record NoteRow(string Key, string Text) : HistoryRow(Key);
