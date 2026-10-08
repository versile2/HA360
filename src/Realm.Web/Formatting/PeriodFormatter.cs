using System.Globalization;
using Realm.Domain;

namespace Realm.Web.Formatting;

/// <summary>
/// The words of the Driving report's period control (01 section 6.2): the split button and its menu, the custom range messages, and the heading and footer of the printable
/// report. Pure: the zone and the printing date are passed in and no clock is read.
/// </summary>
public static class PeriodFormatter
{
    /// <summary>The menu entry that opens the custom range.</summary>
    public const string CustomLabel = "Custom range…";

    /// <summary>The accessible name of the split button's arrow.</summary>
    public const string MenuLabel = "More periods";

    private const string Middle = " · ";

    /// <summary>"Last month", "Last 3 months", "Last 6 months", "Last year"; "Custom range…" for the custom entry.</summary>
    public static string MenuText(PeriodKind kind) => kind switch
    {
        PeriodKind.LastMonth => "Last month",
        PeriodKind.Last3Months => "Last 3 months",
        PeriodKind.Last6Months => "Last 6 months",
        PeriodKind.LastYear => "Last year",
        PeriodKind.Custom => CustomLabel,
        _ => string.Empty,
    };

    /// <summary>The text of the split button's main part: the last chosen long period; a custom range reads as its dates ("Aug 1 – Aug 15").</summary>
    public static string SplitText(ReportPeriod period) =>
        period.Kind == PeriodKind.Custom && period.From is { } from && period.To is { } to
            ? DateRange(from, to)
            : MenuText(period.Kind is PeriodKind.Week or PeriodKind.Custom ? ReportPeriod.DefaultLong : period.Kind);

    /// <summary>"Aug 1 – Aug 15", with the year on both ends when they differ ("Dec 29, 2025 – Jan 4, 2026"). The en dash with spaces is the same as the week chips'.</summary>
    public static string DateRange(DateOnly from, DateOnly to)
    {
        var format = from.Year == to.Year ? "MMM d" : "MMM d, yyyy";
        return from.ToString(format, CultureInfo.InvariantCulture) + " – " + to.ToString(format, CultureInfo.InvariantCulture);
    }

    /// <summary>The words that name the resolved period in headings: "This week · Sep 28 – Oct 4", "Last month · Aug 1 – Aug 31", "Aug 1 – Aug 15" for a custom range.</summary>
    public static string Heading(ReportWindow window)
    {
        var range = DrivingFormatter.RangeText(window.Start, window.End);
        return window.Period.Kind switch
        {
            PeriodKind.Week => window.Period.WeekOffset switch
            {
                0 => "This week" + Middle + range,
                1 => "Last week" + Middle + range,
                _ => range,
            },
            PeriodKind.Custom => range,
            var kind => MenuText(kind) + Middle + range,
        };
    }

    /// <summary>The page title: the weekly title for a week chip, "Driving Report" for a long period.</summary>
    public static string Title(ReportPeriod period) => period.IsLong ? DrivingFormatter.PeriodTitle : DrivingFormatter.Title;

    /// <summary>
    /// The header line of the printed report: "Driving Report · Last month · Aug 1 – Aug 31 · printed Oct 8, 2026"; a driver's page names the driver after the title
    /// ("Driving Report · Alden · This week · Sep 28 – Oct 4 · printed Oct 8, 2026").
    /// </summary>
    public static string PrintHeader(ReportWindow window, DateOnly printedOn, string? driver = null) =>
        DrivingFormatter.PeriodTitle + Middle + (driver is null ? string.Empty : driver + Middle) + Heading(window) + Middle + "printed " + printedOn.ToString("MMM d, yyyy", CultureInfo.InvariantCulture);

    /// <summary>The footer of the printed report: the thresholds the counts were made with, the sampling note and the distance caption.</summary>
    public static string PrintFooter(DrivingThresholds? thresholds)
    {
        var t = thresholds ?? DrivingThresholds.Default;
        return FormattableString.Invariant($"Speeding: above {t.SpeedingMph:0.#} mph for at least {t.SpeedingMinSeconds} s (sampled about every 42 s, so brief bursts are missed). ")
            + FormattableString.Invariant($"Phone use: screen use of at least {t.PhoneMinSeconds} s while the car moved. ")
            + StatNameFormatter.DistanceCaption + ".";
    }

    /// <summary>The message under the custom range pickers for an error; empty when the range is fine.</summary>
    public static string RangeMessage(RangeCheck check, int retentionDays)
    {
        ArgumentNullException.ThrowIfNull(check);
        return check.Error switch
        {
            RangeError.None => string.Empty,
            RangeError.Missing => "Choose a start date and an end date.",
            RangeError.EndBeforeStart => "The start date must be on or before the end date.",
            RangeError.InFuture => "The end date can't be after today.",
            RangeError.BeforeHistory => FormattableString.Invariant($"The Realm keeps {retentionDays} days of history, back to {check.OldestKept.ToString("MMM d, yyyy", CultureInfo.InvariantCulture)}. Choose a start on or after that day."),
            _ => string.Empty,
        };
    }

    /// <summary>The accessible name of the split button's main part: "Last month, September 1 to September 30, selected" is not needed; it names the period it applies.</summary>
    public static string SplitAccessibleName(ReportPeriod period, bool selected) => SplitText(period) + (selected ? ", selected" : string.Empty);
}
