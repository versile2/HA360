using System.Globalization;

namespace Realm.Domain;

/// <summary>The kinds of period the Driving report can show (01 section 6.2): a week chip, or one of the long periods of the split button.</summary>
public enum PeriodKind
{
    /// <summary>A week chip, offset 0 to 3 (honours <c>driving_week_start</c>).</summary>
    Week,

    /// <summary>The previous calendar month.</summary>
    LastMonth,

    /// <summary>The 3 calendar months that end today (rolling).</summary>
    Last3Months,

    /// <summary>The 6 calendar months that end today (rolling).</summary>
    Last6Months,

    /// <summary>The year that ends today (rolling).</summary>
    LastYear,

    /// <summary>A start and an end date chosen by the person, both inclusive.</summary>
    Custom,
}

/// <summary>
/// What the person asked to see: a week chip or a long period. This is the value the page keeps in its address (<c>?week=2</c>, <c>?period=3m</c>,
/// <c>?period=custom&amp;from=2026-08-01&amp;to=2026-08-15</c>); <see cref="PeriodMath.Resolve"/> turns it into the instants of a <see cref="ReportWindow"/>.
/// </summary>
/// <param name="WeekOffset">0 to 3, only for <see cref="PeriodKind.Week"/>.</param>
/// <param name="From">First local day, only for <see cref="PeriodKind.Custom"/>.</param>
/// <param name="To">Last local day (inclusive), only for <see cref="PeriodKind.Custom"/>.</param>
public sealed record ReportPeriod(PeriodKind Kind, int WeekOffset = 0, DateOnly? From = null, DateOnly? To = null)
{
    /// <summary>The long period shown by the split button until the person chooses another.</summary>
    public const PeriodKind DefaultLong = PeriodKind.LastMonth;

    /// <summary>This week, the default.</summary>
    public static ReportPeriod ThisWeek { get; } = new(PeriodKind.Week);

    /// <summary>True for everything but a week chip.</summary>
    public bool IsLong => Kind != PeriodKind.Week;

    /// <summary>The week chip with the given offset.</summary>
    public static ReportPeriod OfWeek(int offset) => new(PeriodKind.Week, offset);

    /// <summary>A custom range of local days, both inclusive.</summary>
    public static ReportPeriod OfRange(DateOnly from, DateOnly to) => new(PeriodKind.Custom, 0, from, to);

    /// <summary>The <c>period=</c> value of a long kind (<c>last-month</c>, <c>3m</c>, <c>6m</c>, <c>1y</c>, <c>custom</c>); null for a week.</summary>
    public static string? QueryValueOf(PeriodKind kind) => kind switch
    {
        PeriodKind.LastMonth => "last-month",
        PeriodKind.Last3Months => "3m",
        PeriodKind.Last6Months => "6m",
        PeriodKind.LastYear => "1y",
        PeriodKind.Custom => "custom",
        _ => null,
    };

    /// <summary>
    /// The query string of this period, without the question mark: <c>week=2</c>, <c>period=3m</c> or <c>period=custom&amp;from=..&amp;to=..</c>. This week is the default and has none (empty).
    /// </summary>
    public string Query() => Kind switch
    {
        PeriodKind.Week => WeekOffset == 0 ? string.Empty : "week=" + WeekOffset.ToString(CultureInfo.InvariantCulture),
        PeriodKind.Custom when From is { } from && To is { } to => "period=custom&from=" + Iso(from) + "&to=" + Iso(to),
        _ => "period=" + QueryValueOf(Kind),
    };

    /// <summary>
    /// Reads the address. A valid <c>period</c> wins; <c>custom</c> needs a <c>from</c> and a <c>to</c> that are dates (a reversed pair is swapped here, the range check is
    /// <see cref="PeriodMath.ValidateCustom"/>'s); otherwise <c>week=0..3</c> (anything else is This week), so old links keep working.
    /// </summary>
    public static ReportPeriod Parse(string? period, string? from, string? to, string? week)
    {
        switch (period?.Trim().ToLowerInvariant())
        {
            case "last-month":
                return new ReportPeriod(PeriodKind.LastMonth);
            case "3m":
                return new ReportPeriod(PeriodKind.Last3Months);
            case "6m":
                return new ReportPeriod(PeriodKind.Last6Months);
            case "1y":
                return new ReportPeriod(PeriodKind.LastYear);
            case "custom" when TryDate(from, out var start) && TryDate(to, out var end):
                return start <= end ? OfRange(start, end) : OfRange(end, start);
        }

        return int.TryParse(week, NumberStyles.None, CultureInfo.InvariantCulture, out var offset) && offset is >= 0 and < WeekMath.ChipCount
            ? OfWeek(offset)
            : ThisWeek;
    }

    /// <summary>"2026-08-01".</summary>
    public static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static bool TryDate(string? value, out DateOnly date) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
}

/// <summary>
/// A <see cref="ReportPeriod"/> resolved against a clock and a zone: the half-open UTC range [<see cref="StartUtc"/>, <see cref="EndUtc"/>) the statistics read, the comparator range
/// the trend is taken against, and the local bounds shown to the person.
/// </summary>
/// <param name="Start">Local start (carries HA's UTC offset).</param>
/// <param name="End">Local display end: the last whole second of the period.</param>
/// <param name="Comparator">The window the trend compares with: the week before (like for like for This week), otherwise the period of the same length just before this one.</param>
/// <param name="IsCurrent">True only for This week: it is still running and reads "so far".</param>
public sealed record ReportWindow(
    ReportPeriod Period,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    TimeWindow Comparator,
    DateTimeOffset Start,
    DateTimeOffset End,
    bool IsCurrent);

/// <summary>Why a custom range is refused; <see cref="None"/> when it is fine.</summary>
public enum RangeError
{
    /// <summary>The range is acceptable.</summary>
    None,

    /// <summary>A date is missing.</summary>
    Missing,

    /// <summary>The start is after the end.</summary>
    EndBeforeStart,

    /// <summary>The end is after today.</summary>
    InFuture,

    /// <summary>The start is before the oldest day that is kept.</summary>
    BeforeHistory,
}

/// <summary>The outcome of <see cref="PeriodMath.ValidateCustom"/>: the error and the oldest day that is kept (for the message).</summary>
public sealed record RangeCheck(RangeError Error, DateOnly OldestKept)
{
    /// <summary>True when the range can be applied.</summary>
    public bool IsValid => Error == RangeError.None;
}
