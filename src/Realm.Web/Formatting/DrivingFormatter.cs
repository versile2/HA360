using System.Globalization;
using Realm.Domain;

namespace Realm.Web.Formatting;

/// <summary>The tone of a driver card's events pill (01 section 6.5): a warning count, the all-clear, or "no figure".</summary>
public enum PillTone
{
    /// <summary>One or more events: the <c>Warning</c> icon.</summary>
    Events,

    /// <summary>Every tracked count is zero: the <c>CheckCircle</c> icon.</summary>
    Clear,

    /// <summary>No type has a count: "Events: —" with the information tooltip.</summary>
    Unknown,
}

/// <summary>The events pill of a driver card (01 section 6.5). <paramref name="Tooltip"/> is null when the pill carries none.</summary>
public sealed record DriverPill(PillTone Tone, string Text, string? Tooltip);

/// <summary>
/// The strings and numbers of the Driving screen that are not about one stat (03 section 3.10; 01 sections 6.1 to 6.5, 8.8 and 10.3): the week chips and the
/// range line, the driver card lines and pills, and the display rounding of distance and speed. Pure: the session's zone is passed in and no clock is read.
/// Every figure arrives already decided by the data layer, so nothing here subtracts or sums counts (01 section 6.3); the one place a number is made is the
/// unit conversion of metres and metres per second (D39). Imperial only in v1 (D35).
/// </summary>
public static class DrivingFormatter
{
    /// <summary>"No figure": a <c>null</c> is never shown as 0 (01 section 6.8).</summary>
    public const string Dash = "—";

    /// <summary>The wordmark of the header row (01 section 8.1).</summary>
    public const string Wordmark = "The King's Court";

    /// <summary>The page title, the H1 (01 section 8.1).</summary>
    public const string Title = "Weekly Driving Report";

    /// <summary>The lore line under the title (01 section 8.1).</summary>
    public const string Subtitle = "The scribes' tally of the Realm's roads";

    /// <summary>The range line, and the empty state, of a week in which no driver was recorded (01 sections 6.2 and 8.7).</summary>
    public const string NoRecordWeek = "The scribes have no record of this week.";

    /// <summary>The banner above the chips when the report cannot be read (01 section 6.9).</summary>
    public const string ReportUnavailableBanner = "The scribes can't reach the records right now. Showing what we have.";

    /// <summary>The second line of a driver card for a driver who was not recorded that week (01 section 6.5).</summary>
    public const string NoRecordDriver = "No record of this week";

    /// <summary>The second line of a driver card for a covered driver with no drives (01 section 6.5).</summary>
    public const string NoDrivesDriver = "No drives this week · resting in the castle";

    /// <summary>The empty state of the current week with nothing driven yet (01 section 8.7).</summary>
    public const string NoDrivesCurrentWeek = "No drives yet this week. The roads are quiet.";

    /// <summary>The tooltip of a pill asterisk: some of the driver's trips were too sparse to measure speed (01 section 6.5, O-9).</summary>
    public const string SparseTripsTooltip = "Some drives were too sparse to measure speed";

    private const double MetersPerMile = 1609.344;
    private const double MetersPerSecondPerMph = 0.44704;
    private const string Separator = " – ";
    private const string Bullet = " • ";
    private const string Middle = " · ";

    // The four types in the order a pill lists them, with the short name a pill uses.
    private static readonly (string Key, string Name)[] PillOrder =
    [
        (EventKeys.Speeding, "speeding"),
        (EventKeys.Phone, "phone"),
        (EventKeys.Accel, "rapid accel."),
        (EventKeys.Braking, "hard braking"),
    ];

    // ---- the week ----------------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The week offset a <c>?week=</c> value asks for: 0 to 3 written in plain digits, anything else (absent, empty, a sign, a fraction, 4 or more) is 0, This week
    /// (01 Appendix B). Read from the route query on every navigation, in every mode (D69).
    /// </summary>
    public static int ParseWeek(string? value) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var week) && week is >= 0 and < WeekMath.ChipCount ? week : 0;

    /// <summary>
    /// The text of a week chip (01 section 6.2): "This week", "Last week", then the two earlier weeks as date ranges ("Sep 14 – Sep 20"). The separator is an
    /// en dash with spaces by design (R-005).
    /// </summary>
    public static string ChipLabel(WeekRef week) =>
        week.Offset switch
        {
            0 => "This week",
            1 => "Last week",
            _ => RangeText(week.Start, week.End),
        };

    /// <summary>
    /// "Sep 14 – Sep 20" (<c>MMM d</c>); when the range spans two years both ends carry the year ("Dec 29, 2025 – Jan 4, 2026"). Both instants are read in the
    /// offset they carry, which is the local one (<see cref="WeekRef"/>, <see cref="WeekReportVm.Start"/>).
    /// </summary>
    public static string RangeText(DateTimeOffset start, DateTimeOffset end)
    {
        var format = start.Year == end.Year ? "MMM d" : "MMM d, yyyy";
        return start.ToString(format, CultureInfo.InvariantCulture) + Separator + end.ToString(format, CultureInfo.InvariantCulture);
    }

    /// <summary>The accessible name of a week chip (01 section 10.3): "This week, September 28 to October 4."</summary>
    public static string ChipAccessibleName(WeekRef week)
    {
        var format = week.Start.Year == week.End.Year ? "MMMM d" : "MMMM d, yyyy";
        var range = week.Start.ToString(format, CultureInfo.InvariantCulture) + " to " + week.End.ToString(format, CultureInfo.InvariantCulture);
        return ChipLabel(week) is { } label && week.Offset <= 1 ? label + ", " + range + "." : range + ".";
    }

    /// <summary>
    /// The range line under the title (01 sections 6.2 and 8.1): the range, " · so far" for the current week, " · recorded from {weekday}" for a partly recorded
    /// week (the weekday of the latest recording start among the covered drivers), and for a week that was not recorded at all the empty-state sentence instead.
    /// </summary>
    public static string RangeLine(WeekReportVm report, TimeZoneInfo zone)
    {
        if (report.Coverage == WeekCoverage.NoRecord)
        {
            return NoRecordWeek;
        }

        var line = RangeText(report.Start, report.End);
        if (report.IsCurrent)
        {
            line += Middle + "so far";
        }

        if (report.Coverage == WeekCoverage.Partial && RecordedFrom(report, zone) is { } weekday)
        {
            line += Middle + "recorded from " + weekday;
        }

        return line;
    }

    // The weekday ("Wed") on which recording began, taking the latest start among the covered drivers; null when none of them has a start.
    private static string? RecordedFrom(WeekReportVm report, TimeZoneInfo zone)
    {
        DateTimeOffset? latest = null;
        foreach (var driver in report.Drivers)
        {
            if (driver.Covered && driver.CoverageStartUtc is { } started && (latest is null || started > latest))
            {
                latest = started;
            }
        }

        return latest is { } instant ? TimeZoneInfo.ConvertTime(instant, zone).ToString("ddd", CultureInfo.InvariantCulture) : null;
    }

    /// <summary>"Tue, Sep 29": a day in the session's zone (01 section 8.3).</summary>
    public static string DayText(DateTimeOffset instant, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTime(instant, zone).ToString("ddd, MMM d", CultureInfo.InvariantCulture);

    // ---- numbers -----------------------------------------------------------------------------------------------------------------------------

    /// <summary>A count with a thousands separator ("1,250"); a count is never shown as a float.</summary>
    public static string Count(int value) => value.ToString("N0", CultureInfo.InvariantCulture);

    /// <summary>Metres per second as whole miles per hour (D39: HA's speeds are mph, converted with the exact factor of 0.44704).</summary>
    public static int Mph(double speedMps) => (int)Math.Round(speedMps / MetersPerSecondPerMph, MidpointRounding.AwayFromZero);

    /// <summary>"96 mph", or "—" when there is no speed.</summary>
    public static string SpeedText(double? speedMps) => speedMps is { } speed ? Mph(speed).ToString(CultureInfo.InvariantCulture) + " mph" : Dash;

    /// <summary>A driver's distance in miles to one decimal ("202.6", "366.0"); display precision, not accuracy (D39, R-111).</summary>
    public static string DriverMiles(double meters) =>
        (Math.Round(meters / MetersPerMile * 10, MidpointRounding.AwayFromZero) / 10).ToString("N1", CultureInfo.InvariantCulture);

    /// <summary>The total distance in whole miles with a thousands separator ("781", "1,257"); the exact metres are summed first and rounded once (02 section 6.2).</summary>
    public static string TotalMiles(double meters) =>
        Math.Round(meters / MetersPerMile, MidpointRounding.AwayFromZero).ToString("N0", CultureInfo.InvariantCulture);

    /// <summary>"drive" or "drives": singular at 1 (01 section 6.7).</summary>
    public static string Drives(int count) => count == 1 ? "drive" : "drives";

    // ---- the cards ---------------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The first letter of a display name in capitals, shown on the member colour when there is no photo (01 section 7.5); "?" for a blank name. The same rule as
    /// the map pins.
    /// </summary>
    public static string Initial(string? name)
    {
        var trimmed = (name ?? string.Empty).Trim();
        return trimmed.Length == 0 ? "?" : StringInfo.GetNextTextElement(trimmed).ToUpperInvariant();
    }

    /// <summary>The Top Speed card's value (01 section 6.4): "96 mph", or "—" when no driver has a speed that week.</summary>
    public static string TopSpeedValue(TopSpeedStat? topSpeed) => SpeedText(topSpeed?.SpeedMps);

    /// <summary>The accessible name of the Top Speed card (01 section 10.3): "Top speed: 96 miles per hour, Alden. Double tap for details."</summary>
    public static string TopSpeedAccessibleName(TopSpeedStat? topSpeed, string? driverName) =>
        topSpeed is null
            ? "Top speed: not recorded yet. Double tap for details."
            : FormattableString.Invariant($"Top speed: {Mph(topSpeed.SpeedMps)} miles per hour, {driverName ?? topSpeed.MemberId}. Double tap for details.");

    /// <summary>The accessible name of the Drives card (01 section 10.3): "Drives: 64. Total miles: 781. Double tap for details."</summary>
    public static string DrivesAccessibleName(WeekTotals? totals) =>
        totals is null
            ? "Drives: not recorded yet. Double tap for details."
            : $"Drives: {Count(totals.Drives)}. Total miles: {TotalMiles(totals.Meters)}. Double tap for details.";

    /// <summary>
    /// The second line of a driver card (01 section 6.5): "18 drives • 202.6 miles" (the bullet is U+2022, miles to one decimal), "No drives this week · resting
    /// in the castle" for a covered driver with no drives, "No record of this week" for a driver who was not covered.
    /// </summary>
    public static string DriverLine(DriverSummary driver)
    {
        if (!driver.Covered || driver.Drives is not { } drives)
        {
            return NoRecordDriver;
        }

        return drives == 0
            ? NoDrivesDriver
            : Count(drives) + " " + Drives(drives) + Bullet + DriverMiles(driver.Meters ?? 0) + " miles";
    }

    /// <summary>
    /// The events pill of a driver card (01 section 6.5), or null when the card has none (a driver with no drives, or not covered). The text lists the types that
    /// are tracked for the driver that week, that is those whose count is not <c>null</c> (a <c>null</c> is absence, never partial): one type reads
    /// "38 speeding events", two read "6 speeding · 60 phone", three or more read "{EventsTotal} events" with the data layer's sum, all zero reads "No events",
    /// and no count at all reads "Events: —". The asterisk follows the count when <see cref="DriverSummary.CoarseTrips"/> is above zero, and only then.
    /// </summary>
    public static DriverPill? Pill(DriverSummary driver)
    {
        if (!driver.Covered || driver.Drives is not { } drives || drives == 0)
        {
            return null;
        }

        if (driver.EventsTotal is not { } total)
        {
            return new DriverPill(PillTone.Unknown, "Events: " + Dash, StatNameFormatter.UnavailableTooltip);
        }

        if (total == 0)
        {
            return new DriverPill(PillTone.Clear, "No events", null);
        }

        var star = driver.CoarseTrips > 0 ? "*" : string.Empty;
        var tracked = new List<(string Name, int Count)>(PillOrder.Length);
        foreach (var (key, name) in PillOrder)
        {
            if (driver.Events.TryGetValue(key, out var count) && count is { } value)
            {
                tracked.Add((name, value));
            }
        }

        var text = tracked.Count switch
        {
            0 or >= 3 => Count(total) + star + " events",
            2 => Count(tracked[0].Count) + star + " " + tracked[0].Name + Middle + Count(tracked[1].Count) + " " + tracked[1].Name,
            _ => Count(tracked[0].Count) + star + " " + tracked[0].Name + (tracked[0].Count == 1 ? " event" : " events"),
        };
        return new DriverPill(PillTone.Events, text, star.Length > 0 ? SparseTripsTooltip : null);
    }

    /// <summary>
    /// The accessible name of a driver card (01 section 10.3): "Cass: 18 drives, 202.6 miles, 38 speeding events. Double tap for weekly details." The asterisk is
    /// replaced by the sentence of its tooltip, so assistive technology hears what a sighted user can read.
    /// </summary>
    public static string DriverAccessibleName(string name, DriverSummary driver)
    {
        const string Tail = ". Double tap for weekly details.";
        if (!driver.Covered || driver.Drives is not { } drives)
        {
            return name + ": no record of this week" + Tail;
        }

        if (drives == 0)
        {
            return name + ": no drives this week" + Tail;
        }

        var parts = Count(drives) + " " + Drives(drives) + ", " + DriverMiles(driver.Meters ?? 0) + " miles";
        if (Pill(driver) is { } pill)
        {
            parts += ", " + pill.Text.Replace("*", string.Empty, StringComparison.Ordinal);
            if (pill.Tooltip is { } tooltip && pill.Tone == PillTone.Events)
            {
                parts += ", " + tooltip.ToLowerInvariant();
            }
        }

        return name + ": " + parts + Tail;
    }

    /// <summary>The link of a driver card to the driver's week (01 section 6.5, relative as every link, 03 section 3.3): <c>driving/jester?week=0</c>.</summary>
    public static string DriverHref(string memberId, int week) =>
        "driving/" + Uri.EscapeDataString(memberId) + "?week=" + week.ToString(CultureInfo.InvariantCulture);
}
