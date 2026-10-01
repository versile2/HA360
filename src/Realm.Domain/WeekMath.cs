namespace Realm.Domain;

/// <summary>
/// Week bounds in HA's zone. Pure functions of (now, week start day, zone): no clock is read here.
/// A week runs from local 00:00 of its first day to local 00:00 of the next one, so DST weeks are
/// 167 or 169 hours long.
/// </summary>
public static class WeekMath
{
    /// <summary>Number of week chips on the Driving page (offsets 0 to 3).</summary>
    public const int ChipCount = 4;

    /// <summary>Start of week <paramref name="weekOffset"/> as a UTC instant. Offset 0 is the week containing <paramref name="now"/>.</summary>
    public static DateTimeOffset StartUtc(DateTimeOffset now, DayOfWeek weekStart, TimeZoneInfo zone, int weekOffset)
    {
        var localNow = TimeZoneInfo.ConvertTime(now, zone).DateTime;
        var firstDay = FirstDayOfWeek(localNow, weekStart).AddDays(-7 * weekOffset);
        return LocalMidnightToUtc(firstDay, zone);
    }

    /// <summary>Exclusive end of week <paramref name="weekOffset"/>: the start of the following week.</summary>
    public static DateTimeOffset EndUtc(DateTimeOffset now, DayOfWeek weekStart, TimeZoneInfo zone, int weekOffset)
        => StartUtc(now, weekStart, zone, weekOffset - 1);

    /// <summary>The chip for week <paramref name="weekOffset"/>, with local bounds. End is the exclusive end minus one second.</summary>
    public static WeekRef Week(DateTimeOffset now, DayOfWeek weekStart, TimeZoneInfo zone, int weekOffset)
    {
        var start = TimeZoneInfo.ConvertTime(StartUtc(now, weekStart, zone, weekOffset), zone);
        var end = TimeZoneInfo.ConvertTime(EndUtc(now, weekStart, zone, weekOffset).AddSeconds(-1), zone);
        return new WeekRef(weekOffset, start, end);
    }

    /// <summary>The four chips, this week first.</summary>
    public static IReadOnlyList<WeekRef> Chips(DateTimeOffset now, DayOfWeek weekStart, TimeZoneInfo zone)
    {
        var chips = new WeekRef[ChipCount];
        for (var offset = 0; offset < ChipCount; offset++)
        {
            chips[offset] = Week(now, weekStart, zone, offset);
        }

        return chips;
    }

    /// <summary>
    /// The offset of the week that a trip belongs to, decided by the local date of its start (a trip that
    /// crosses midnight into the next week stays in the week it started in). 0 is the current week,
    /// negative values are later weeks and values above 3 are older than the chips.
    /// </summary>
    public static int WeekOffsetOf(DateTimeOffset tripStart, DateTimeOffset now, DayOfWeek weekStart, TimeZoneInfo zone)
    {
        var currentFirstDay = FirstDayOfWeek(TimeZoneInfo.ConvertTime(now, zone).DateTime, weekStart);
        var tripFirstDay = FirstDayOfWeek(TimeZoneInfo.ConvertTime(tripStart, zone).DateTime, weekStart);
        return (currentFirstDay - tripFirstDay).Days / 7;
    }

    private static DateTime FirstDayOfWeek(DateTime local, DayOfWeek weekStart)
    {
        var daysSinceStart = ((int)local.DayOfWeek - (int)weekStart + 7) % 7;
        return local.Date.AddDays(-daysSinceStart);
    }

    // Local midnight of a calendar day as a UTC instant. If midnight does not exist (a DST gap) the day
    // starts at the next valid instant; if it happens twice the day starts at the first occurrence.
    private static DateTimeOffset LocalMidnightToUtc(DateTime day, TimeZoneInfo zone)
    {
        var local = DateTime.SpecifyKind(day, DateTimeKind.Unspecified);
        while (zone.IsInvalidTime(local))
        {
            local = local.AddMinutes(1);
        }

        var offset = zone.IsAmbiguousTime(local)
            ? zone.GetAmbiguousTimeOffsets(local).Max()
            : zone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset).ToUniversalTime();
    }
}
