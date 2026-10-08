namespace Realm.Domain;

/// <summary>
/// The arithmetic of the Driving report's periods (01 section 6.2): which long periods the retention covers, the instants of a week, a calendar month, a rolling window or a
/// custom range in HA's zone, and the check of a custom range. Pure functions of (period, now, week start, zone, retention): no clock is read here. Every window runs from local
/// midnight of its first day to local midnight after its last, so DST days are 23 or 25 hours long.
/// </summary>
public static class PeriodMath
{
    /// <summary>The longest span of 3 calendar months ending today (92 days).</summary>
    public const int Days3Months = 92;

    /// <summary>The longest span of 6 calendar months ending today (184 days) plus one day of margin.</summary>
    public const int Days6Months = 185;

    /// <summary>The longest span of a year ending today (366 days).</summary>
    public const int DaysYear = 366;

    /// <summary>The smallest <c>retention_fix_days</c> that covers the kind; 0 for the kinds that are always available.</summary>
    public static int RequiredDays(PeriodKind kind) => kind switch
    {
        PeriodKind.Last3Months => Days3Months,
        PeriodKind.Last6Months => Days6Months,
        PeriodKind.LastYear => DaysYear,
        _ => 0,
    };

    /// <summary>True when <paramref name="retentionDays"/> covers the kind (6 months and a year are hidden otherwise).</summary>
    public static bool IsAvailable(PeriodKind kind, int retentionDays) => retentionDays >= RequiredDays(kind);

    /// <summary>The menu items of the split button in order: Last month, Last 3 months, Last 6 months, Last year, as far as the retention covers them. Custom range is always offered after them.</summary>
    public static IReadOnlyList<PeriodKind> MenuKinds(int retentionDays) =>
        [.. new[] { PeriodKind.LastMonth, PeriodKind.Last3Months, PeriodKind.Last6Months, PeriodKind.LastYear }.Where(kind => IsAvailable(kind, retentionDays))];

    /// <summary>Today's date in the zone.</summary>
    public static DateOnly Today(DateTimeOffset now, TimeZoneInfo zone) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);

    /// <summary>The oldest day whose data is still kept: today minus the retention.</summary>
    public static DateOnly OldestKept(DateTimeOffset now, TimeZoneInfo zone, int retentionDays) => Today(now, zone).AddDays(-retentionDays);

    /// <summary>Checks a custom range: both dates present, start not after end, end not in the future, start not before the oldest kept day.</summary>
    public static RangeCheck ValidateCustom(DateOnly? from, DateOnly? to, DateTimeOffset now, TimeZoneInfo zone, int retentionDays) =>
        ValidateCustom(from, to, Today(now, zone), retentionDays);

    /// <summary>The same check against a given local <paramref name="today"/>.</summary>
    public static RangeCheck ValidateCustom(DateOnly? from, DateOnly? to, DateOnly today, int retentionDays)
    {
        var oldest = today.AddDays(-retentionDays);
        if (from is not { } start || to is not { } end)
        {
            return new RangeCheck(RangeError.Missing, oldest);
        }

        var error = start > end ? RangeError.EndBeforeStart
            : end > today ? RangeError.InFuture
            : start < oldest ? RangeError.BeforeHistory
            : RangeError.None;
        return new RangeCheck(error, oldest);
    }

    /// <summary>
    /// The window of <paramref name="period"/>. A week uses <see cref="WeekMath"/> and the comparator of <see cref="StatsRules.ComparatorWindow"/>; the previous calendar month, the rolling
    /// 3 months, 6 months and year (the days after today minus N months, up to and including today) and a custom range compare with the period of the same length just before (the previous calendar month with the month before it).
    /// </summary>
    public static ReportWindow Resolve(ReportPeriod period, DateTimeOffset now, DayOfWeek weekStart, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(period);
        if (period.Kind == PeriodKind.Week)
        {
            var offset = period.WeekOffset;
            var week = WeekMath.Week(now, weekStart, zone, offset);
            return new ReportWindow(
                period,
                WeekMath.StartUtc(now, weekStart, zone, offset),
                WeekMath.EndUtc(now, weekStart, zone, offset),
                StatsRules.ComparatorWindow(now, weekStart, zone, offset),
                week.Start,
                week.End,
                IsCurrent: offset == 0);
        }

        var today = Today(now, zone);
        var (first, last) = Days(period, today);
        if (period.Kind == PeriodKind.LastMonth)
        {
            // The month before is the month before, whatever its length (a February is compared with its January).
            var before = first.AddMonths(-1);
            return Window(period, first, last, zone, Midnight(before, zone));
        }

        return Window(period, first, last, zone, null);
    }

    /// <summary>The first and last local day (both inclusive) of a long period as of <paramref name="today"/>.</summary>
    public static (DateOnly First, DateOnly Last) Days(ReportPeriod period, DateOnly today)
    {
        switch (period.Kind)
        {
            case PeriodKind.LastMonth:
                var thisMonth = new DateOnly(today.Year, today.Month, 1);
                return (thisMonth.AddMonths(-1), thisMonth.AddDays(-1));
            case PeriodKind.Last3Months:
                return (today.AddMonths(-3).AddDays(1), today);
            case PeriodKind.Last6Months:
                return (today.AddMonths(-6).AddDays(1), today);
            case PeriodKind.LastYear:
                return (today.AddYears(-1).AddDays(1), today);
            case PeriodKind.Custom when period.From is { } from && period.To is { } to:
                return from <= to ? (from, to) : (to, from);
            default:
                return (today, today);
        }
    }

    private static ReportWindow Window(ReportPeriod period, DateOnly first, DateOnly last, TimeZoneInfo zone, DateTimeOffset? comparatorStart)
    {
        var start = Midnight(first, zone);
        var end = Midnight(last.AddDays(1), zone);
        var length = last.DayNumber - first.DayNumber + 1;
        var comparator = new TimeWindow(comparatorStart ?? Midnight(first.AddDays(-length), zone), start);
        return new ReportWindow(
            period,
            start,
            end,
            comparator,
            TimeZoneInfo.ConvertTime(start, zone),
            TimeZoneInfo.ConvertTime(end.AddSeconds(-1), zone),
            IsCurrent: false);
    }

    private static DateTimeOffset Midnight(DateOnly day, TimeZoneInfo zone) => WeekMath.LocalMidnightToUtc(day.ToDateTime(TimeOnly.MinValue), zone);
}
