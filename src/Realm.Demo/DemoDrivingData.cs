using System.Globalization;
using Realm.Domain;

namespace Realm.Demo;

/// <summary>
/// The driving half of the Demo fixture (02 section 9.4 and 9.5): the week reports and the driver weeks of weeks 0 to 3
/// under the active variants. It is a frozen history: the numbers are always those of the default instant, whatever
/// clock the session has (02 section 9.0). The default dataset is the base data after the production rules (phone use
/// for the phone-capable driver only, no rapid acceleration or hard braking); the variants then switch single aspects of it.
/// </summary>
internal sealed class DemoDrivingData
{
    private const string SpeedingNote = "Counted from ~42 s samples; short bursts are missed";

    // The fresh-install variant: every driver's recording began on 2026-09-23 at 12:00 local, 4.5 of the 7 days of last week.
    private static readonly DateTimeOffset RecordingStartUtc = new DateTimeOffset(2026, 9, 23, 12, 0, 0, TimeSpan.FromHours(-5)).ToUniversalTime();

    private readonly DemoVariants _variants;
    private readonly TimeZoneInfo _zone;

    public DemoDrivingData(DemoVariants variants, TimeZoneInfo zone)
    {
        _variants = variants;
        _zone = zone;
    }

    /// <summary>The report of week <paramref name="weekOffset"/> (0 to 3), with the bounds of the given week start for the default instant.</summary>
    public WeekReportVm Report(int weekOffset, DayOfWeek weekStart)
    {
        EnsureWeek(weekOffset);
        var week = WeekMath.Week(DemoDataSource.Anchor, weekStart, _zone, weekOffset);

        // Card order: drives descending, miles descending, name.
        var rows = DemoDrivingTables.ReportDrivers
            .Select(driver => Row(weekOffset, driver))
            .OrderByDescending(row => row.Drives ?? -1)
            .ThenByDescending(row => row.Tenths ?? -1)
            .ThenBy(row => row.Driver.Name, StringComparer.Ordinal)
            .ToList();

        var coverage = !rows.Any(row => row.Covered)
            ? WeekCoverage.NoRecord
            : rows.Any(row => row.CoverageStartUtc is not null) ? WeekCoverage.Partial : WeekCoverage.Full;

        return new WeekReportVm(
            Start: week.Start,
            End: week.End,
            IsCurrent: weekOffset == 0,
            Coverage: coverage,
            DistanceBasis: DistanceBasis.Gps,
            Events: DemoEventCounts.Keys.ToDictionary(key => key, key => Stat(key, rows)),
            TopSpeed: TopSpeed(weekOffset, rows),
            Totals: new WeekTotals(rows.Sum(row => row.Drives ?? 0), rows.Sum(row => row.Tenths ?? 0) * DemoDrivingTables.TenthMileMetres),
            Drivers: [.. rows.Select(Summary)]);
    }

    /// <summary>One driver's week; null for the static member, an unknown id or any member that is not in the report.</summary>
    public DriverWeek? GetDriverWeek(string memberId, int weekOffset)
    {
        EnsureWeek(weekOffset);
        var driver = DemoDrivingTables.ReportDrivers.FirstOrDefault(candidate => candidate.Id == memberId);
        return driver is null ? null : new DriverWeek(Summary(Row(weekOffset, driver)), Trips(weekOffset, driver));
    }

    private static void EnsureWeek(int weekOffset)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(weekOffset, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(weekOffset, WeekMath.ChipCount - 1);
    }

    // Each count scaled by 9/14 (4.5 of 7 days) and rounded half up, in integers.
    private static int? Scale(int? value) => value is int count ? (int)(((2L * count * 9) + 14) / 28) : null;

    // The base row of a driver and week with the fresh-install scaling applied, but with no count hidden yet: what the
    // drive generator shares out over the drives.
    private DemoDriverWeekRow Totals(int week, DemoMember driver)
    {
        var row = DemoDrivingTables.Row(week, driver);
        if (!_variants.FreshInstall || week == 0)
        {
            return row;
        }

        if (week == 1)
        {
            return row with
            {
                CoverageStartUtc = RecordingStartUtc,
                Drives = Scale(row.Drives),
                Tenths = Scale(row.Tenths),
                Counts = row.Counts.Map(Scale),
            };
        }

        return row with { Covered = false, Drives = null, Tenths = null, TopMph = null, Counts = DemoEventCounts.NotRecorded };
    }

    // The row as the report shows it: the dataset's counts, then the variants that hide or zero them.
    private DemoDriverWeekRow Row(int week, DemoMember driver)
    {
        var row = Totals(week, driver);
        var comparators = _variants.FreshInstall ? DemoEventCounts.NotRecorded : row.Comparators;
        row = row with { Counts = Visible(row.Counts, driver), Comparators = Visible(comparators, driver) };
        return _variants.EmptyWeek && week == 0
            ? row with { Drives = 0, Tenths = 0, TopMph = null, Counts = row.Counts.Map(count => count is null ? null : 0) }
            : row;
    }

    // Which of a driver's counts the dataset and the variants leave visible; the rest are null, never 0.
    private DemoEventCounts Visible(DemoEventCounts counts, DemoMember driver)
    {
        if (_variants.Life360Down)
        {
            return DemoEventCounts.NotRecorded;
        }

        var phone = (_variants.AllSources || driver.PhoneCapable) && !_variants.PhoneUnavailable ? counts.Phone : null;
        return new DemoEventCounts(
            counts.Speeding,
            phone,
            _variants.AllSources ? counts.Accel : null,
            _variants.AllSources ? counts.Braking : null);
    }

    private EventAvailability Availability(string key) => key switch
    {
        EventKeys.Speeding => EventAvailability.All,
        EventKeys.Phone => _variants.AllSources ? EventAvailability.All : EventAvailability.Some,
        _ => _variants.AllSources ? EventAvailability.All : EventAvailability.None,
    };

    // One event type across the drivers (02 section 6.4): the total of the drivers that have a count, the comparator total
    // when all of those have a comparator, and the trend over the drivers that have both.
    private EventStat Stat(string key, IReadOnlyList<DemoDriverWeekRow> rows)
    {
        var availability = Availability(key);
        var drivers = rows.Select(row => new EventDriverCount(row.Driver.Id, row.Counts.Get(key), row.Comparators.Get(key))).ToList();
        var counted = drivers.Where(driver => driver.Count is not null).ToList();
        var both = counted.Where(driver => driver.ComparatorCount is not null).ToList();

        int? total = counted.Count == 0 ? null : counted.Sum(driver => driver.Count.GetValueOrDefault());
        int? comparatorTotal = counted.Count > 0 && both.Count == counted.Count ? counted.Sum(driver => driver.ComparatorCount.GetValueOrDefault()) : null;
        int? trend = both.Count == 0 ? null : both.Sum(driver => driver.Count.GetValueOrDefault()) - both.Sum(driver => driver.ComparatorCount.GetValueOrDefault());

        // The chip asterisk: a type that some drivers can have, with a total, where a covered driver has no count.
        var partial = availability != EventAvailability.None && total is not null && rows.Any(row => row.Covered && row.Counts.Get(key) is null);

        return new EventStat(
            Total: total,
            ComparatorTotal: comparatorTotal,
            TrendDelta: trend,
            Source: StatSource.Derived,
            Availability: availability,
            Partial: partial,
            Note: key == EventKeys.Speeding ? SpeedingNote : null,
            Drivers: drivers);
    }

    // The week's fastest driver; a tie goes to the earlier time. Null when no driver has a speed record.
    private TopSpeedStat? TopSpeed(int week, IReadOnlyList<DemoDriverWeekRow> rows)
    {
        var candidates = rows
            .Where(row => row.Covered && row.TopMph is not null)
            .Select(row => (Row: row, At: TopSpeedAt(week, row.Driver)))
            .ToList();
        if (candidates.Count == 0)
        {
            return null;
        }

        var best = candidates.OrderByDescending(candidate => candidate.Row.TopMph).ThenBy(candidate => candidate.At).First();
        return new TopSpeedStat(
            MemberId: best.Row.Driver.Id,
            SpeedMps: best.Row.TopMph.GetValueOrDefault() * FixParser.MphToMps,
            AtUtc: best.At,
            Street: DemoDrivingTables.TopSpeedStreetOf(week, best.Row.Driver),
            Drivers: [.. rows.Select(row => new DriverTopSpeed(row.Driver.Id, row.TopMph is int mph ? mph * FixParser.MphToMps : null))]);
    }

    // The start of the driver's top-speed drive.
    private DateTimeOffset TopSpeedAt(int week, DemoMember driver) =>
        Trips(week, driver).MaxBy(trip => trip.TopSpeedMps)?.StartUtc
        ?? throw new InvalidOperationException($"{driver.Id} has a top speed in week {week} but no drive.");

    private DriverSummary Summary(DemoDriverWeekRow row)
    {
        // Only types that have a source count towards the sum; null when none has a count.
        var counted = DemoEventCounts.Keys
            .Where(key => Availability(key) != EventAvailability.None)
            .Select(row.Counts.Get)
            .Where(count => count is not null)
            .ToList();

        return new DriverSummary(
            MemberId: row.Driver.Id,
            Drives: row.Drives,
            Meters: row.Tenths is int tenths ? tenths * DemoDrivingTables.TenthMileMetres : null,
            DistanceBasis: DistanceBasis.Gps,
            CoarseTrips: 0,
            PhoneCapable: _variants.AllSources || row.Driver.PhoneCapable,
            Events: row.Counts.ToDictionary(),
            EventsTotal: counted.Count == 0 ? null : counted.Sum(count => count.GetValueOrDefault()),
            EventsPartial: false,
            Covered: row.Covered,
            CoverageStartUtc: row.CoverageStartUtc);
    }

    // The driver's drives of the week, newest first. The list is generated from the dataset's full counts and then shown
    // through the same variants as the report, so its sums are the report's figures.
    private List<DriveVm> Trips(int week, DemoMember driver)
    {
        var totals = Totals(week, driver);
        if (!totals.Covered || (_variants.EmptyWeek && week == 0))
        {
            return [];
        }

        var monday = WeekMath.Week(DemoDataSource.Anchor, DayOfWeek.Monday, _zone, week).Start.DateTime;
        var windowStart = _variants.FreshInstall && week == 1
            ? (int)(RecordingStartUtc - WeekMath.StartUtc(DemoDataSource.Anchor, DayOfWeek.Monday, _zone, week)).TotalMinutes
            : 0;
        var spec = new DemoDriveSpec(
            MemberId: driver.Id,
            WeekStartIso: monday.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Count: totals.Drives.GetValueOrDefault(),
            TotalTenths: totals.Tenths.GetValueOrDefault(),
            TopMph: totals.TopMph.GetValueOrDefault(),
            Counts: totals.Counts,
            WindowStartMin: windowStart,
            WindowEndMin: DemoDrivingTables.LatestEndMin(week, driver),
            HomeLabel: driver.Id == DemoCast.Jester.Id ? DemoPlaces.JesterHall.Name : DemoPlaces.Home.Name,
            Fixed: DemoDrivingTables.FixedDrives(week, driver));

        return [.. DemoDriveGenerator.Generate(spec).Select(drive => new DriveVm(
            StartUtc: Instant(monday, drive.StartMin),
            EndUtc: Instant(monday, drive.EndMin),
            FromLabel: drive.From,
            ToLabel: drive.To,
            Meters: drive.Tenths * DemoDrivingTables.TenthMileMetres,
            TopSpeedMps: drive.TopMph * FixParser.MphToMps,
            Events: Visible(drive.Events, driver).ToDictionary()))];
    }

    // A time of the week, given as minutes after Monday 00:00 local, as a UTC instant.
    private DateTimeOffset Instant(DateTime monday, int minutes)
    {
        var local = monday.AddMinutes(minutes);
        return new DateTimeOffset(local, _zone.GetUtcOffset(local)).ToUniversalTime();
    }
}
