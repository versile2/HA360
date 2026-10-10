using System.Globalization;

namespace Realm.Domain;

/// <summary>
/// The days of Location History as pure rules (0.3.0, D123): a day is a local calendar day in HA's zone, never the browser's, and its bounds are half open
/// [local midnight, next local midnight) as instants, so a day that has 23 or 25 hours (the clocks change) is measured right. Nothing here reads a clock.
/// </summary>
public static class HistoryDayMath
{
    /// <summary>The ISO text of a day, as the route spells it: <c>2026-09-30</c>.</summary>
    public static string Iso(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>The day of an instant in <paramref name="zone"/>.</summary>
    public static DateOnly DayOf(DateTimeOffset instant, TimeZoneInfo zone) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, zone).DateTime);

    /// <summary>
    /// The bounds of a local day: from local midnight (inclusive) to the next local midnight (exclusive). A midnight that does not exist (the clocks jump over it) is the first
    /// instant after the gap, and an ambiguous one (the clocks repeat it) is its first occurrence.
    /// </summary>
    public static TimeWindow Bounds(DateOnly day, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        return new TimeWindow(StartOf(day, zone), StartOf(day.AddDays(1), zone));
    }

    /// <summary>
    /// The oldest and the newest day that can be shown: today back to <paramref name="retentionDays"/> days before it (the same rule as the Driving report's custom range,
    /// <see cref="PeriodMath.OldestKept"/>: "100 days" is today and the 100 days before). A retention of 0 or less keeps everything, which the screen cannot bound, so it reads as 400, the
    /// largest the add-on allows.
    /// </summary>
    public static (DateOnly Oldest, DateOnly Newest) Range(DateTimeOffset now, TimeZoneInfo zone, int retentionDays)
    {
        var today = DayOf(now, zone);
        var days = retentionDays > 0 ? retentionDays : 400;
        return (today.AddDays(-days), today);
    }

    /// <summary>The day inside the retained range: a later day is today, an earlier one the oldest day.</summary>
    public static DateOnly Clamp(DateOnly day, DateOnly oldest, DateOnly newest) => day > newest ? newest : day < oldest ? oldest : day;

    /// <summary>The day of a <c>?date=YYYY-MM-DD</c> value; null for anything that is not exactly that.</summary>
    public static DateOnly? Parse(string? value) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) ? day : null;

    private static DateTimeOffset StartOf(DateOnly day, TimeZoneInfo zone)
    {
        var local = DateTime.SpecifyKind(day.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        while (zone.IsInvalidTime(local))
        {
            local = local.AddMinutes(1);
        }

        var offset = zone.IsAmbiguousTime(local) ? zone.GetAmbiguousTimeOffsets(local).Max() : zone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset).ToUniversalTime();
    }
}
