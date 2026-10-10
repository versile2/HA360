using System.Globalization;
using Realm.Domain;

namespace Realm.Web.Formatting;

/// <summary>One printed row of the Location History table: its cells in column order.</summary>
public sealed record HistoryPrintRow(string Day, IReadOnlyList<string> Cells);

/// <summary>
/// Every string of the Location History screen (0.3.0, D123): the lines of the timeline ("At Hearth Haven", "8:05 am – 5:42 pm · 9 hrs 37 mins", "Drive · 12.4 mi · 24 mins · top 71 mph"),
/// the day selector, the empty states, the print header and the rows of the printed table. Pure: the caller passes the session's zone and the day, and a figure that is unknown reads
/// "—" and never 0. The words of the Driving screens (miles, mph, the speed text) are used as they are, so the two screens agree.
/// </summary>
public static class HistoryFormatter
{
    /// <summary>The title of the screen and of the printed page.</summary>
    public const string Title = "Location History";

    /// <summary>The label of the entry points in a person's detail.</summary>
    public const string ButtonLabel = "History";

    /// <summary>"Show the location history of {name}" — the accessible name of the entry button.</summary>
    public static string ButtonAccessibleName(string name) => "Show the location history of " + name;

    /// <summary>The Back control of the screen: it returns to the person's detail.</summary>
    public const string BackLabel = "Back to the person's details";

    /// <summary>The toggle that lists the last seven days.</summary>
    public const string RangeLabel = "Last 7 days";

    /// <summary>The accessible name of the range toggle, which says what it does.</summary>
    public const string RangeToggleName = "Show the last 7 days as a list";

    /// <summary>The control that goes back to one day.</summary>
    public const string DayViewLabel = "Day";

    /// <summary>The accessible name of the previous-day arrow.</summary>
    public const string PreviousLabel = "Previous day";

    /// <summary>The accessible name of the next-day arrow.</summary>
    public const string NextLabel = "Next day";

    /// <summary>The Today button.</summary>
    public const string TodayLabel = "Today";

    /// <summary>The label of the print button.</summary>
    public const string PrintLabel = "Print this location history, or save it as PDF";

    /// <summary>What the timeline panel is called for a screen reader.</summary>
    public const string TimelineLabel = "Timeline";

    /// <summary>The handle that makes the bottom sheet taller.</summary>
    public const string ExpandLabel = "Show more of the timeline";

    /// <summary>The handle that makes the bottom sheet shorter.</summary>
    public const string CollapseLabel = "Show more of the map";

    /// <summary>Said while the day is read.</summary>
    public const string Loading = "Loading the day…";

    /// <summary>The day could not be read.</summary>
    public const string Unavailable = "The location history could not be loaded right now. Try again in a moment.";

    /// <summary>The person has no history: not tracked, a tracker, or unknown.</summary>
    public static string NoHistory(string name) => name + " has no location history to show.";

    /// <summary>The note under a person's name when they are not tracked.</summary>
    public const string BackToLocation = "Back to the map";

    /// <summary>The footer of the printed page.</summary>
    public const string PrintFooter = "Visits are places where the person stayed at least 5 minutes. Times are in Home Assistant's time zone.";

    /// <summary>The columns of the printed table.</summary>
    public static IReadOnlyList<string> PrintColumns { get; } = ["Time", "What", "Where", "Duration", "Miles", "Top speed", "Events"];

    // ---- the day selector --------------------------------------------------------------------------------------------------------------------

    /// <summary>"Wed, Sep 30": the date button.</summary>
    public static string DayButton(DateOnly day) => day.ToString("ddd, MMM d", CultureInfo.InvariantCulture);

    /// <summary>The accessible name of the date button: "Wednesday, September 30, 2026. Choose a day".</summary>
    public static string DayButtonName(DateOnly day) => day.ToString("dddd, MMMM d, yyyy", CultureInfo.InvariantCulture) + ". Choose a day";

    /// <summary>"Today · Wed, Sep 30", "Yesterday · Tue, Sep 29", "Mon, Sep 28": the heading of a day in the list and under the name.</summary>
    public static string DayHeading(DateOnly day, DateOnly today) =>
        day == today ? TodayLabel + " · " + DayButton(day) : day == today.AddDays(-1) ? "Yesterday · " + DayButton(day) : DayButton(day);

    /// <summary>"Sep 24 – Sep 30": the range under the name.</summary>
    public static string RangeText(DateOnly from, DateOnly to) => PeriodFormatter.DateRange(from, to);

    /// <summary>The route of a person's history without a day (today): the entry points of the details.</summary>
    public static string MemberHref(string memberId) => "history/" + Uri.EscapeDataString(memberId);

    /// <summary>The route of a day: <c>history/{member}?date=2026-09-30</c> (relative, like every link).</summary>
    public static string DayHref(string memberId, DateOnly day) => "history/" + Uri.EscapeDataString(memberId) + "?date=" + HistoryDayMath.Iso(day);

    /// <summary>The route of the last-seven-days view.</summary>
    public static string RangeHref(string memberId) => "history/" + Uri.EscapeDataString(memberId) + "?range=7d";

    // ---- the timeline ------------------------------------------------------------------------------------------------------------------------

    /// <summary>"At Hearth Haven".</summary>
    public static string StayTitle(HistoryStay stay) => "At " + stay.Label;

    /// <summary>"8:05 am – 5:42 pm": the span of a visit. A visit that began before the day or goes on after it says "midnight"; one that is still going on says "now".</summary>
    public static string StaySpan(HistoryStay stay, TimeZoneInfo zone)
    {
        var start = stay.ContinuesFromPreviousDay ? "midnight" : TimeFormatter.Clock(stay.StartUtc, zone);
        var end = stay.IsOngoing ? "now" : stay.ContinuesIntoNextDay ? "midnight" : TimeFormatter.Clock(stay.EndUtc, zone);
        if (stay.ContinuesFromPreviousDay || stay.IsOngoing || stay.ContinuesIntoNextDay)
        {
            return start + " – " + end;
        }

        return TimeFormatter.Range(stay.StartUtc, stay.EndUtc, zone);
    }

    /// <summary>"8:05 am – 5:42 pm · 9 hrs 37 mins".</summary>
    public static string StayDetail(HistoryStay stay, TimeZoneInfo zone) => StaySpan(stay, zone) + " · " + Duration(stay.Duration);

    /// <summary>"Drive · 12.4 mi · 24 mins · top 71 mph" (the top speed is left out when it is unknown).</summary>
    public static string DriveTitle(HistoryDrive drive, UnitSystem units = UnitSystem.Imperial)
    {
        var text = "Drive · " + UnitFormatter.Distance(drive.Meters, units) + " · " + Duration(drive.Duration);
        return drive.TopSpeedMps is { } top ? text + " · top " + UnitFormatter.Speed(top, units) : text;
    }

    /// <summary>"7:35 – 7:45 am": the span of a drive.</summary>
    public static string DriveSpan(HistoryDrive drive, TimeZoneInfo zone) => TimeFormatter.Range(drive.StartUtc, drive.EndUtc, zone);

    /// <summary>"Hearth Haven → Work".</summary>
    public static string Route(HistoryDrive drive) => drive.FromLabel + " → " + drive.ToLabel;

    /// <summary>"Speeding ×2" for a drive with speeding events; null when there are none or they are unknown.</summary>
    public static string? SpeedingChip(HistoryDrive drive) => drive.SpeedingCount is > 0 and var count ? "Speeding ×" + count.ToString(CultureInfo.InvariantCulture) : null;

    /// <summary>"Phone use ×1" for a drive with phone-use events; null when there are none or they are unknown.</summary>
    public static string? PhoneChip(HistoryDrive drive) => drive.PhoneCount is > 0 and var count ? "Phone use ×" + count.ToString(CultureInfo.InvariantCulture) : null;

    /// <summary>The chip for a drive recorded from sparse fixes: its path is a rough line.</summary>
    public const string RoughPathChip = "Rough path";

    /// <summary>The accessible name of a timeline button: the line, the span and what it does.</summary>
    public static string EntryAccessibleName(HistoryEntry entry, TimeZoneInfo zone, UnitSystem units = UnitSystem.Imperial) =>
        entry switch
        {
            HistoryStay stay => StayTitle(stay) + ", " + StayDetail(stay, zone) + ". Show on the map",
            HistoryDrive drive => DriveTitle(drive, units) + ", " + Route(drive) + ", " + DriveSpan(drive, zone) + ". Show the path on the map",
            _ => string.Empty,
        };

    /// <summary>The label of a marker on the map: "At Hearth Haven, 8:05 am – 5:42 pm".</summary>
    public static string StopLabel(HistoryStay stay, TimeZoneInfo zone) => StayTitle(stay) + ", " + StaySpan(stay, zone);

    /// <summary>"Day started here" / "Day ended here": the markers of the two ends.</summary>
    public static string StartLabel(DateTimeOffset time, TimeZoneInfo zone) => "Day started here, " + TimeFormatter.Clock(time, zone);

    /// <summary>The marker of where the day ended; for today it is where the person is now.</summary>
    public static string EndLabel(DateTimeOffset time, TimeZoneInfo zone, bool isToday) =>
        (isToday ? "Latest position" : "Day ended here") + ", " + TimeFormatter.Clock(time, zone);

    /// <summary>
    /// A length of time in whole minutes, the two largest units: "9 hrs 37 mins", "2 hrs", "1 hr 5 mins", "24 mins", "1 min". Under a minute reads "1 min" (a visit is never shorter than five).
    /// </summary>
    public static string Duration(TimeSpan span)
    {
        var minutes = (long)Math.Round(span.TotalMinutes, MidpointRounding.AwayFromZero);
        if (minutes < 1)
        {
            minutes = 1;
        }

        var hours = minutes / 60;
        var rest = minutes % 60;
        if (hours == 0)
        {
            return Unit(rest, "min");
        }

        return rest == 0 ? Unit(hours, "hr") : Unit(hours, "hr") + " " + Unit(rest, "min");
    }

    private static string Unit(long value, string name) => value.ToString(CultureInfo.InvariantCulture) + " " + name + (value == 1 ? string.Empty : "s");

    // ---- the day and the range ---------------------------------------------------------------------------------------------------------------

    /// <summary>"3 places · 4 drives · 41.2 mi": the summary of a day.</summary>
    public static string DaySummary(HistoryDayVm day, UnitSystem units = UnitSystem.Imperial)
    {
        ArgumentNullException.ThrowIfNull(day);
        if (day.Entries.Count == 0)
        {
            return day.Recorded ? "Stayed in one place" : "No data";
        }

        var places = Plural(day.StayCount, "place");
        var drives = Plural(day.DriveCount, "drive");
        return day.DriveCount == 0 ? places + " · " + drives : places + " · " + drives + " · " + UnitFormatter.Distance(day.TotalMeters, units);
    }

    private static string Plural(int count, string noun) => count.ToString(CultureInfo.InvariantCulture) + " " + noun + (count == 1 ? string.Empty : "s");

    /// <summary>What to say when the day has nothing to list: no data at all, or a day without a stop or a drive.</summary>
    public static string EmptyDay(HistoryDayVm day, string name)
    {
        ArgumentNullException.ThrowIfNull(day);
        return day.Recorded
            ? name + " did not stop anywhere for long or drive on this day."
            : "No location data was recorded for " + name + " on this day.";
    }

    /// <summary>The line the screen reader hears when a day has been loaded: "Wednesday, September 30. 3 places, 4 drives."</summary>
    public static string DayAnnouncement(HistoryDayVm day, UnitSystem units = UnitSystem.Imperial)
    {
        ArgumentNullException.ThrowIfNull(day);
        return day.Day.ToString("dddd, MMMM d", CultureInfo.InvariantCulture) + ". " + DaySummary(day, units).Replace(" · ", ", ", StringComparison.Ordinal) + ".";
    }

    // ---- print -------------------------------------------------------------------------------------------------------------------------------

    /// <summary>"Location History · Alden · Wed, Sep 30, 2026" (a day) or "Location History · Alden · Sep 24 – Sep 30, 2026" (a range).</summary>
    public static string PrintHeader(string name, DateOnly from, DateOnly to) =>
        from == to
            ? Title + " · " + name + " · " + from.ToString("ddd, MMM d, yyyy", CultureInfo.InvariantCulture)
            : Title + " · " + name + " · " + RangeText(from, to) + ", " + to.Year.ToString(CultureInfo.InvariantCulture);

    /// <summary>The rows of the printed table for one day, oldest first: the span, what it is, where, how long, how far, how fast and the events.</summary>
    public static IReadOnlyList<HistoryPrintRow> PrintRows(HistoryDayVm day, TimeZoneInfo zone, UnitSystem units = UnitSystem.Imperial)
    {
        ArgumentNullException.ThrowIfNull(day);
        var heading = day.Day.ToString("ddd, MMM d, yyyy", CultureInfo.InvariantCulture);
        return
        [
            .. day.Entries.Select(entry => entry switch
            {
                HistoryStay stay => new HistoryPrintRow(heading, [StaySpan(stay, zone), "Stay", stay.Label, Duration(stay.Duration), DrivingFormatter.Dash, DrivingFormatter.Dash, string.Empty]),
                HistoryDrive drive => new HistoryPrintRow(
                    heading,
                    [
                        DriveSpan(drive, zone),
                        "Drive",
                        Route(drive),
                        Duration(drive.Duration),
                        UnitFormatter.Distance(drive.Meters, units),
                        drive.TopSpeedMps is { } top ? UnitFormatter.Speed(top, units) : DrivingFormatter.Dash,
                        string.Join(", ", new[] { SpeedingChip(drive), PhoneChip(drive) }.Where(text => text is not null)),
                    ]),
                _ => new HistoryPrintRow(heading, [string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty]),
            }),
        ];
    }
}
