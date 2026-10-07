using System.Globalization;
using Realm.Domain;

namespace Realm.Web.Formatting;

/// <summary>The direction of a stat chip's trend arrow (01 section 6.3): the sign of <see cref="EventStat.TrendDelta"/>, or no arrow.</summary>
public enum TrendDirection
{
    /// <summary>No arrow: no comparator, or no value to compare.</summary>
    None,

    /// <summary>More events than the comparator: worse, <c>TrendingUp</c> in the error colour.</summary>
    Up,

    /// <summary>Fewer events than the comparator: better, <c>TrendingDown</c> in the success colour.</summary>
    Down,

    /// <summary>The same as the comparator: <c>TrendingFlat</c> in the stale colour.</summary>
    Flat,
}

/// <summary>
/// One row of the bars of a popup (01 section 6.7.1). <paramref name="Pill"/> is the value as the pill shows it ("22", "366.0 mi", "96 mph") or "—" for an
/// unavailable one. <paramref name="Fraction"/> is the value divided by the largest value of the popup (0 to 1, so the largest bar is 100 %); it is <c>null</c> for
/// an unavailable row, which sits at the floor. <paramref name="Text"/> is the accessible text of the row ("Alden: 22 drives", "Briar: not recorded").
/// </summary>
public sealed record PopupBar(string MemberId, string Name, string Pill, double? Fraction, string Text);

/// <summary>
/// The names and sentences of the six headline items of the Driving screen (01 sections 6.3, 6.7, 8.8 and 10.3): the stat chips (speeding, phone use, rapid
/// acceleration, hard braking), the Top Speed card and the Drives card. Pure, with no clock and no zone read except the one passed in. Every number is the data
/// layer's: a chip renders one <see cref="EventStat"/> and this class never subtracts, sums or compares totals (01 section 6.3); counting the drivers that
/// shared a value is the one thing it reads from <see cref="EventStat.Drivers"/>. A <c>null</c> total is "—", never 0 (01 section 6.8, D26).
/// </summary>
public static class StatNameFormatter
{
    /// <summary>The key of the Top Speed card, as in <c>popup-topspeed</c> (01 Appendix B).</summary>
    public const string TopSpeedKey = "topspeed";

    /// <summary>The key of the Drives card, as in <c>popup-drives</c> (01 Appendix B).</summary>
    public const string DrivesKey = "drives";

    /// <summary>The tooltip of an unavailable value (01 sections 6.3 and 8.8, D26); the same words without the full stop are the popup sentence.</summary>
    public const string UnavailableTooltip = "The Realm hasn't recorded this yet";

    /// <summary>The popup summary of an unavailable value (01 section 6.7).</summary>
    public const string UnavailableSentence = UnavailableTooltip + ".";

    /// <summary>
    /// The tooltip of the information button in the phone popup's footnote (01 sections 6.7 and 8.8, D36). Exposed here for the popup of S11b; the chip itself
    /// carries the partial sentence, not this one.
    /// </summary>
    public const string AndroidAutoTooltip = "Screen time while Android Auto is connected isn't counted, so navigation doesn't count as phone use.";

    /// <summary>The distance caption (01 section 8.8, R-111): shown while the basis is GPS, which is always in v1.</summary>
    public const string DistanceCaption = "Distances are GPS-estimated (about 2–5 % low on winding roads)";

    /// <summary>The start of every popup footnote (01 section 6.7, D26): <c>Source = Derived</c>, so the Life360 name never appears.</summary>
    public const string SourceFootnote = "Source: The Realm's own records";

    /// <summary>The caption of a sampled count (01 section 6.6, D39): speeding is counted from ~42 s samples, so brief bursts are missed.</summary>
    public const string SampledCaption = "sampled";

    /// <summary>The popup summary of a Top Speed card with no speed (01 section 6.7).</summary>
    public const string NoSpeedSentence = "No speed data for this week yet.";

    /// <summary>The four stat chips in the order the screen shows them (01 section 6.1).</summary>
    public static IReadOnlyList<string> ChipKeys { get; } = [EventKeys.Speeding, EventKeys.Phone, EventKeys.Accel, EventKeys.Braking];

    // ---- names (01 sections 6.3 and 6.7) -----------------------------------------------------------------------------------------------------

    /// <summary>The visible label of a stat chip (01 section 8.8): "Speeding", "Phone use", "Rapid accel.", "Hard braking".</summary>
    public static string Label(string key) =>
        key switch
        {
            EventKeys.Speeding => "Speeding",
            EventKeys.Phone => "Phone use",
            EventKeys.Accel => "Rapid accel.",
            EventKeys.Braking => "Hard braking",
            TopSpeedKey => "Top Speed",
            DrivesKey => "Drives",
            _ => key,
        };

    /// <summary>The popup title, and the name an accessible name starts with (01 sections 6.7 and 10.3): "Rapid acceleration" where the chip says "Rapid accel.".</summary>
    public static string Title(string key) =>
        key switch
        {
            EventKeys.Accel => "Rapid acceleration",
            TopSpeedKey => "Top Speed",
            DrivesKey => "Total Drives",
            _ => Label(key),
        };

    /// <summary>The lore line under a popup title (01 section 6.7).</summary>
    public static string Lore(string key) =>
        key switch
        {
            EventKeys.Speeding => "Heralds of haste",
            EventKeys.Phone => "Eyes on the road, good sirs",
            EventKeys.Accel => "The sudden gallop",
            EventKeys.Braking => "Whoa, steed!",
            TopSpeedKey => "The fastest charge",
            DrivesKey => "Leagues travelled",
            _ => string.Empty,
        };

    /// <summary>The noun of a summary sentence (01 section 6.7), singular at 1: "speeding event", "phone-use event", "rapid acceleration", "hard-braking event".</summary>
    public static string Noun(string key, int count) =>
        key switch
        {
            EventKeys.Speeding => count == 1 ? "speeding event" : "speeding events",
            EventKeys.Phone => count == 1 ? "phone-use event" : "phone-use events",
            EventKeys.Accel => count == 1 ? "rapid acceleration" : "rapid accelerations",
            EventKeys.Braking => count == 1 ? "hard-braking event" : "hard-braking events",
            _ => count == 1 ? "event" : "events",
        };

    /// <summary>"this week" for the current week, "last week" for the one before, "that week" for the two date-range weeks (01 section 6.7).</summary>
    public static string Period(int weekOffset) =>
        weekOffset switch
        {
            0 => "this week",
            1 => "last week",
            _ => "that week",
        };

    /// <summary>The comparator's name (01 section 6.7): "last week" for This week, otherwise "the week before".</summary>
    public static string Previous(int weekOffset) => weekOffset == 0 ? "last week" : "the week before";

    // ---- the chip (01 section 6.3) -----------------------------------------------------------------------------------------------------------

    /// <summary>True when the stat has no value to show: no <see cref="EventStat"/> at all, or a <c>null</c> total (01 section 6.9).</summary>
    public static bool IsUnavailable(EventStat? stat) => stat?.Total is null;

    /// <summary>
    /// True when the value carries an asterisk: the data layer's <see cref="EventStat.Partial"/>, and only where there is a value and the type can have one. A
    /// permanent gap (<see cref="EventAvailability.None"/>) never reads as a glitch (R-110).
    /// </summary>
    public static bool IsPartial(EventStat? stat) => stat is { Partial: true, Total: not null, Availability: not EventAvailability.None };

    /// <summary>The number of a chip (01 section 6.3): "56", "1,250", "60*", or "—" when there is no value. Never "0" for a <c>null</c>.</summary>
    public static string ChipNumber(EventStat? stat) =>
        stat?.Total is { } total ? DrivingFormatter.Count(total) + (IsPartial(stat) ? "*" : string.Empty) : DrivingFormatter.Dash;

    /// <summary>The arrow of a chip (01 section 6.3): the sign of <see cref="EventStat.TrendDelta"/>; none when there is no value or no comparator.</summary>
    public static TrendDirection Trend(EventStat? stat) =>
        stat is { Total: not null, TrendDelta: { } delta } ? (delta > 0 ? TrendDirection.Up : delta < 0 ? TrendDirection.Down : TrendDirection.Flat) : TrendDirection.None;

    /// <summary>
    /// The tooltip of a chip (01 sections 6.3 and 6.9), or null when it has none: "The Realm hasn't recorded this yet" for an unavailable value; for a partial
    /// value "Only {k} of {n} drivers shared this" (first), then the <see cref="EventStat.Note"/> when there is one; otherwise the Note alone.
    /// </summary>
    public static string? ChipTooltip(EventStat? stat)
    {
        if (IsUnavailable(stat))
        {
            return UnavailableTooltip;
        }

        var note = string.IsNullOrWhiteSpace(stat!.Note) ? null : stat.Note;
        if (!IsPartial(stat))
        {
            return note;
        }

        var partial = PartialSentence(stat);
        return note is null ? partial : partial + ". " + note;
    }

    /// <summary>"Only 1 of 4 drivers shared this" (01 section 8.8): k is the number of report drivers that have a count, n the number of covered report drivers in the stat.</summary>
    public static string PartialSentence(EventStat stat) =>
        string.Create(CultureInfo.InvariantCulture, $"Only {SharedCount(stat)} of {DriverCount(stat)} drivers shared this");

    /// <summary>The n of "k of n": the drivers the stat speaks for (covered ones, R3-09); a stat without the figure counts every listed driver.</summary>
    public static int DriverCount(EventStat stat) => stat.CoveredCount ?? stat.Drivers.Count;

    /// <summary>The number of report drivers that have a (non-null) count of the type.</summary>
    public static int SharedCount(EventStat stat)
    {
        var shared = 0;
        foreach (var driver in stat.Drivers)
        {
            if (driver.Count is not null)
            {
                shared++;
            }
        }

        return shared;
    }

    /// <summary>
    /// The accessible name of a chip (01 section 10.3), built only from <see cref="EventStat.Total"/>, <see cref="EventStat.TrendDelta"/> and the driver counts.
    /// "Speeding: 56 events this week, up 7 from last week, which is worse. Double tap for details."; down ends "which is better", flat reads "the same as last
    /// week"; a partial value adds ", from 1 of 4 drivers"; an unavailable one reads "Rapid acceleration: not recorded yet. Double tap for details.". The
    /// period words follow the week (<see cref="Period"/>, <see cref="Previous"/>).
    /// </summary>
    public static string ChipAccessibleName(string key, EventStat? stat, int weekOffset)
    {
        const string Tail = " Double tap for details.";
        var title = Title(key);
        if (stat?.Total is not { } total)
        {
            return title + ": not recorded yet." + Tail;
        }

        var text = title + ": " + DrivingFormatter.Count(total) + (total == 1 ? " event " : " events ") + Period(weekOffset);
        if (IsPartial(stat))
        {
            text += ", from " + SharedCount(stat).ToString(CultureInfo.InvariantCulture) + " of " + DriverCount(stat).ToString(CultureInfo.InvariantCulture) + " drivers";
        }

        var previous = Previous(weekOffset);
        text += stat.TrendDelta switch
        {
            null => string.Empty,
            > 0 and var more => ", up " + DrivingFormatter.Count(more) + " from " + previous + ", which is worse",
            < 0 and var fewer => ", down " + DrivingFormatter.Count(-fewer) + " from " + previous + ", which is better",
            _ => ", the same as " + previous,
        };
        return text + "." + Tail;
    }

    // ---- the popups (01 section 6.7) ---------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The summary sentence of an event popup (01 section 6.7), exactly one sentence: "56 speeding events this week, 7 more than last week." The rules in order:
    /// unavailable reads the D26 sentence; zero reads "No speeding events this week."; partial adds "from the {k} driver(s) tracked"; the difference phrase is
    /// the data layer's <see cref="EventStat.TrendDelta"/> and is omitted when it is <c>null</c>.
    /// </summary>
    public static string Summary(string key, EventStat? stat, int weekOffset)
    {
        if (stat?.Total is not { } total)
        {
            return UnavailableSentence;
        }

        var period = Period(weekOffset);
        if (total == 0)
        {
            return "No " + Noun(key, 0) + " " + period + ".";
        }

        var text = DrivingFormatter.Count(total) + " " + Noun(key, total) + " " + period;
        if (IsPartial(stat))
        {
            var tracked = SharedCount(stat);
            text += " from the " + tracked.ToString(CultureInfo.InvariantCulture) + (tracked == 1 ? " driver" : " drivers") + " tracked";
        }

        if (Difference(stat.TrendDelta, weekOffset) is { } difference)
        {
            text += ", " + difference;
        }

        return text + ".";
    }

    /// <summary>"7 more than last week", "11 fewer than last week", "the same as the week before"; null when there is no comparator (01 section 6.7).</summary>
    public static string? Difference(int? trendDelta, int weekOffset)
    {
        var previous = Previous(weekOffset);
        return trendDelta switch
        {
            null => null,
            > 0 and var more => DrivingFormatter.Count(more) + " more than " + previous,
            < 0 and var fewer => DrivingFormatter.Count(-fewer) + " fewer than " + previous,
            _ => "the same as " + previous,
        };
    }

    /// <summary>The Top Speed popup sentence (01 section 6.7): "Alden hit 96 mph on Tue, Sep 29."; "No speed data for this week yet." without a speed.</summary>
    public static string TopSpeedSummary(TopSpeedStat? topSpeed, string? driverName, TimeZoneInfo zone) =>
        topSpeed is null
            ? NoSpeedSentence
            : (driverName ?? topSpeed.MemberId) + " hit " + DrivingFormatter.SpeedText(topSpeed.SpeedMps) + " on " + DrivingFormatter.DayText(topSpeed.AtUtc, zone) + ".";

    /// <summary>The Drives popup sentence (01 section 6.7): "64 drives, 781 miles on the road."</summary>
    public static string DrivesSummary(WeekTotals? totals) =>
        totals is null
            ? UnavailableSentence
            : DrivingFormatter.Count(totals.Drives) + " " + DrivingFormatter.Drives(totals.Drives) + ", " + DrivingFormatter.TotalMiles(totals.Meters) + " miles on the road.";

    /// <summary>
    /// The footnote of a popup (01 section 6.7): always "Source: The Realm's own records" (D26), then the text of its row. The Android Auto sentence is not part of
    /// it: it is the tooltip of the footnote's information button (<see cref="AndroidAutoTooltip"/>).
    /// </summary>
    public static string Footnote(string key) =>
        SourceFootnote
        + key switch
        {
            EventKeys.Speeding => " (sampled every ~42 s, so brief bursts are missed). Counts stretches above the speeding threshold (default 80 mph).",
            EventKeys.Phone => ". Counts screen use while the car moved, only for drivers whose phone shares it.",
            TopSpeedKey => ". Highest sampled speed on any drive this week.",
            DrivesKey => ". " + DistanceCaption + ".",
            _ => ".",
        };

    /// <summary>
    /// The summary sentence of any of the six popups (01 section 6.7): the Top Speed and Drives sentences, or <see cref="Summary"/> of the chip's stat. A week that
    /// was not recorded, or whose report could not be read, says <see cref="UnavailableSentence"/> for all six (01 section 6.9).
    /// </summary>
    public static string PopupSummary(string key, WeekReportVm? report, IReadOnlyDictionary<string, MemberVm> members, int weekOffset, TimeZoneInfo zone)
    {
        if (report is null || report.Coverage == WeekCoverage.NoRecord)
        {
            return UnavailableSentence;
        }

        return key switch
        {
            TopSpeedKey => TopSpeedSummary(report.TopSpeed, report.TopSpeed is { } top ? NameOf(members, top.MemberId) : null, zone),
            DrivesKey => DrivesSummary(report.Totals),
            _ => Summary(key, report.Events.TryGetValue(key, out var stat) ? stat : null, weekOffset),
        };
    }

    /// <summary>True for the popup that has the Drives and Miles toggle (01 section 6.7): the Drives popup only.</summary>
    public static bool HasToggle(string key) => key == DrivesKey;

    /// <summary>True when the pills of the bars are the wide ones (01 section 6.7.1): miles and mph values, 72 px against 56.</summary>
    public static bool IsWide(string key, bool miles) => key == TopSpeedKey || (key == DrivesKey && miles);

    /// <summary>
    /// The bars of a popup (01 section 6.7.1), one per report driver. The value is the driver's own count of the stat (<see cref="EventStat.Drivers"/>), top speed
    /// (<see cref="TopSpeedStat.Drivers"/>), drives, or with <paramref name="miles"/> the distance; a driver with no value, or who was not covered, reads "—" and
    /// sorts last. The order is value descending, then drives descending, then name; the fraction is the value over the largest value, never computed from totals.
    /// </summary>
    public static IReadOnlyList<PopupBar> PopupBars(string key, WeekReportVm? report, IReadOnlyDictionary<string, MemberVm> members, bool miles = false)
    {
        if (report is null)
        {
            return [];
        }

        var rows = new List<BarRow>(report.Drivers.Count);
        var maximum = 0.0;
        foreach (var driver in report.Drivers)
        {
            var value = BarValue(key, report, driver, miles);
            if (value is { } known && known > maximum)
            {
                maximum = known;
            }

            rows.Add(new BarRow(driver, NameOf(members, driver.MemberId), value));
        }

        return rows
            .OrderBy(row => row.Value is null)
            .ThenByDescending(row => row.Value ?? 0)
            .ThenByDescending(row => row.Driver.Drives ?? 0)
            .ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
            .Select(row => ToBar(key, row, maximum, miles))
            .ToList();
    }

    /// <summary>The display name of a member, or the id when the member is not known.</summary>
    public static string NameOf(IReadOnlyDictionary<string, MemberVm> members, string memberId) =>
        members.TryGetValue(memberId, out var member) ? member.DisplayName : memberId;

    private sealed record BarRow(DriverSummary Driver, string Name, double? Value);

    // The driver's own figure for the popup, or null when there is none: not covered, not recorded that week, or a type that no driver has a count of.
    private static double? BarValue(string key, WeekReportVm report, DriverSummary driver, bool miles)
    {
        if (!driver.Covered || report.Coverage == WeekCoverage.NoRecord)
        {
            return null;
        }

        switch (key)
        {
            case TopSpeedKey:
                return DrivingFormatter.TopSpeedOf(report, driver.MemberId);
            case DrivesKey:
                return miles ? driver.Meters : driver.Drives;
            default:
                if (!report.Events.TryGetValue(key, out var stat) || IsUnavailable(stat))
                {
                    return null;
                }

                foreach (var entry in stat.Drivers)
                {
                    if (string.Equals(entry.MemberId, driver.MemberId, StringComparison.Ordinal))
                    {
                        return entry.Count;
                    }
                }

                return null;
        }
    }

    private static PopupBar ToBar(string key, BarRow row, double maximum, bool miles)
    {
        var id = row.Driver.MemberId;
        if (row.Value is not { } value)
        {
            return new PopupBar(id, row.Name, DrivingFormatter.Dash, null, row.Name + ": not recorded");
        }

        // A maximum of 0 (every driver has a real zero) leaves every bar at the floor.
        var fraction = maximum > 0 ? Math.Clamp(value / maximum, 0, 1) : 0;
        var whole = (int)Math.Round(value, MidpointRounding.AwayFromZero);
        var (pill, text) = key switch
        {
            TopSpeedKey => (
                DrivingFormatter.SpeedText(value),
                row.Name + ": " + DrivingFormatter.Mph(value).ToString(CultureInfo.InvariantCulture) + " miles per hour"),
            DrivesKey when miles => (
                DrivingFormatter.DriverMiles(value) + " mi",
                row.Name + ": " + DrivingFormatter.DriverMiles(value) + " miles"),
            DrivesKey => (
                DrivingFormatter.Count(whole),
                row.Name + ": " + DrivingFormatter.Count(whole) + " " + DrivingFormatter.Drives(whole)),
            _ => (
                DrivingFormatter.Count(whole),
                row.Name + ": " + DrivingFormatter.Count(whole) + " " + Noun(key, whole)),
        };
        return new PopupBar(id, row.Name, pill, fraction, text);
    }
}
