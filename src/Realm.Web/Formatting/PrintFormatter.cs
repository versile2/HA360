using System.Globalization;
using Realm.Domain;

namespace Realm.Web.Formatting;

/// <summary>One row of a printed table: its cells in column order.</summary>
public sealed record PrintRow(IReadOnlyList<string> Cells);

/// <summary>
/// The rows of the printable report's tables (01 section 6.10): the totals, the drivers and the events (drives) of a driver. Every cell is built from the figures the data
/// layer decided, with the same rounding as the screen; a figure that is unknown reads "—" and never 0.
/// </summary>
public static class PrintFormatter
{
    /// <summary>The columns of the totals table.</summary>
    public static IReadOnlyList<string> TotalsColumns { get; } = ["Total", "Value", "Compared with the period before"];

    /// <summary>The columns of the drivers table.</summary>
    public static IReadOnlyList<string> DriverColumns { get; } = ["Driver", "Drives", "Miles", "Top speed", "Speeding", "Phone use", "Events"];

    /// <summary>The columns of the events (drives) table.</summary>
    public static IReadOnlyList<string> EventColumns { get; } = ["Date", "Time", "From → To", "Miles", "Top speed", "Speeding", "Phone use"];

    /// <summary>The totals: drives, total miles, top speed (with who and when), and the speeding and phone-use events with their trend.</summary>
    public static IReadOnlyList<PrintRow> Totals(WeekReportVm report, Func<string, string> nameOf, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(nameOf);
        var noRecord = report.Coverage == WeekCoverage.NoRecord;
        var rows = new List<PrintRow>
        {
            new(["Drives", noRecord ? DrivingFormatter.Dash : DrivingFormatter.Count(report.Totals.Drives), string.Empty]),
            new(["Total miles", noRecord ? DrivingFormatter.Dash : DrivingFormatter.TotalMiles(report.Totals.Meters), string.Empty]),
            new(["Top speed", TopSpeedCell(report.TopSpeed, nameOf, zone), string.Empty]),
        };
        foreach (var key in StatNameFormatter.ChipKeys)
        {
            if (key is EventKeys.Accel or EventKeys.Braking)
            {
                continue;
            }

            report.Events.TryGetValue(key, out var stat);
            rows.Add(new PrintRow([StatNameFormatter.Label(key), stat?.Total is { } total ? DrivingFormatter.Count(total) : DrivingFormatter.Dash, Trend(stat)]));
        }

        return rows;
    }

    /// <summary>One row for each driver, in the order of the report.</summary>
    public static IReadOnlyList<PrintRow> Drivers(WeekReportVm report, Func<string, string> nameOf)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(nameOf);
        return
        [
            .. report.Drivers.Select(driver => new PrintRow(
            [
                nameOf(driver.MemberId),
                DrivingFormatter.DrivesTile(driver),
                DrivingFormatter.MilesTile(driver),
                DrivingFormatter.TopSpeedTile(driver, DrivingFormatter.TopSpeedOf(report, driver.MemberId)),
                DrivingFormatter.EventCount(driver, EventKeys.Speeding),
                DrivingFormatter.EventCount(driver, EventKeys.Phone),
                DrivingFormatter.EventsTile(driver),
            ])),
        ];
    }

    /// <summary>Every drive of the driver in the period, newest first, one row each (the printed list ignores the pager).</summary>
    public static IReadOnlyList<PrintRow> Events(IReadOnlyList<DriveVm> trips, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(trips);
        return
        [
            .. trips.Select(trip => new PrintRow(
            [
                TimeZoneInfo.ConvertTime(trip.StartUtc, zone).ToString("ddd, MMM d, yyyy", CultureInfo.InvariantCulture),
                TimeFormatter.Range(trip.StartUtc, trip.EndUtc, zone),
                (trip.FromLabel ?? DrivingFormatter.UnnamedPlace) + " → " + (trip.ToLabel ?? DrivingFormatter.UnnamedPlace),
                UnitFormatter.Distance(trip.Meters),
                DrivingFormatter.SpeedText(trip.TopSpeedMps),
                Count(trip, EventKeys.Speeding),
                Count(trip, EventKeys.Phone),
            ])),
        ];
    }

    private static string Count(DriveVm trip, string key) =>
        trip.Events.TryGetValue(key, out var count) && count is { } value ? DrivingFormatter.Count(value) : DrivingFormatter.Dash;

    private static string TopSpeedCell(TopSpeedStat? top, Func<string, string> nameOf, TimeZoneInfo zone) =>
        top is null
            ? DrivingFormatter.Dash
            : DrivingFormatter.SpeedText(top.SpeedMps) + " (" + nameOf(top.MemberId) + ", " + DrivingFormatter.DayText(top.AtUtc, zone) + ")";

    private static string Trend(EventStat? stat) => stat?.TrendDelta switch
    {
        null => string.Empty,
        > 0 and var more => "+" + DrivingFormatter.Count(more),
        < 0 and var fewer => "−" + DrivingFormatter.Count(-fewer),
        _ => "no change",
    };
}
