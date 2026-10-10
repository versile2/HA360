using Realm.Domain;
using Realm.Web.Formatting;
using Realm.Web.Theme;

namespace Realm.Web.Map;

// The payloads that C# sends to historyMap.js (0.3.0, D123). Property names become camelCase on the wire, like every map payload; coordinates are degrees and a trail point is [lon, lat].

/// <summary>One piece of the trail: the path of a drive. <c>Dashed</c> is a straight guess between fixes that were far apart.</summary>
public sealed record HistoryTrailPayload(string Id, bool Dashed, IReadOnlyList<double[]> Points);

/// <summary>A marker: a place visit (<c>stay</c>, with the id of its timeline entry and its number) or an end of the day (<c>start</c>, <c>end</c>).</summary>
public sealed record HistoryStopPayload(string Id, string Kind, double Lat, double Lon, string Label, int? Number);

/// <summary>What the map draws: the day's trail and markers. <c>Key</c> names the day shown: a new key fits the camera to the day, the same key does not.</summary>
public sealed record HistoryDayPayload(string Key, string Color, string Casing, IReadOnlyList<HistoryTrailPayload> Trail, IReadOnlyList<HistoryStopPayload> Stops, bool Pickable);

/// <summary>The entry the timeline selected: <c>Kind</c> is <c>drive</c> or <c>stay</c>; a null <c>Id</c> clears it.</summary>
public sealed record HistoryFocusPayload(string? Id, string? Kind);

/// <summary>Builds the payloads from the day view models.</summary>
public static class HistoryMapPayloads
{
    /// <summary>The drive's kind name.</summary>
    public const string DriveKind = "drive";

    /// <summary>The stay's kind name.</summary>
    public const string StayKind = "stay";

    /// <summary>One day: its trail, a numbered marker for each visit and the two ends of the day (not drawn where a visit already marks the place).</summary>
    public static HistoryDayPayload ForDay(HistoryDayVm day, string? color, TimeZoneInfo zone, bool isToday)
    {
        ArgumentNullException.ThrowIfNull(day);
        var stops = new List<HistoryStopPayload>();
        var number = 0;
        foreach (var stay in day.Stays)
        {
            number++;
            stops.Add(new HistoryStopPayload(stay.Id, "stay", stay.Lat, stay.Lon, HistoryFormatter.StopLabel(stay, zone), number));
        }

        if (day.Start is { } start && !Covered(start, stops))
        {
            stops.Add(new HistoryStopPayload(string.Empty, "start", start.Lat, start.Lon, HistoryFormatter.StartLabel(start.Time, zone), null));
        }

        if (day.End is { } end && !Covered(end, stops))
        {
            stops.Add(new HistoryStopPayload(string.Empty, "end", end.Lat, end.Lon, HistoryFormatter.EndLabel(end.Time, zone, isToday), null));
        }

        var trail = day.Trail.Select(segment => new HistoryTrailPayload(segment.EntryId, segment.Dashed, segment.Points)).ToList();
        return new HistoryDayPayload($"{day.MemberId}:{HistoryDayMath.Iso(day.Day)}", color ?? RealmPalette.Primary, RealmPalette.PinOutline, trail, stops, Pickable: true);
    }

    /// <summary>The range view: a marker for each place visited in the days (a place once), no trail, nothing to pick.</summary>
    public static HistoryDayPayload ForRange(string memberId, DateOnly from, DateOnly to, IReadOnlyList<HistoryDayVm> days, string? color, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(days);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var stops = new List<HistoryStopPayload>();
        foreach (var stay in days.SelectMany(day => day.Stays))
        {
            var key = stay.PlaceId ?? $"{Math.Round(stay.Lat, 3)}:{Math.Round(stay.Lon, 3)}";
            if (seen.Add(key))
            {
                stops.Add(new HistoryStopPayload(stay.Id, "stay", stay.Lat, stay.Lon, HistoryFormatter.StayTitle(stay), null));
            }
        }

        return new HistoryDayPayload($"{memberId}:{HistoryDayMath.Iso(from)}..{HistoryDayMath.Iso(to)}", color ?? RealmPalette.Primary, RealmPalette.PinOutline, [], stops, Pickable: false);
    }

    /// <summary>The highlight of an entry of the timeline; null clears it.</summary>
    public static HistoryFocusPayload ForFocus(HistoryEntry? entry) =>
        entry switch
        {
            HistoryDrive drive => new HistoryFocusPayload(drive.Id, DriveKind),
            HistoryStay stay => new HistoryFocusPayload(stay.Id, StayKind),
            _ => new HistoryFocusPayload(null, null),
        };

    // An end of the day that lies within 40 m of a visit's marker is that marker.
    private static bool Covered(HistoryEndPoint point, IReadOnlyList<HistoryStopPayload> stops) =>
        stops.Any(stop => Geo.DistanceM(stop.Lat, stop.Lon, point.Lat, point.Lon) < 40);
}
