using System.Globalization;

namespace Realm.Web.Formatting;

/// <summary>
/// Time strings (03 section 3.10, 01 section 8.3): the 12-hour clock in the session's zone (never the browser's), the "Since" and relative strings
/// of the lists and the time range. Pure: the caller passes the instant, "now" and the zone (the session's own clock and <c>Zone</c>), so a UTC browser
/// or a frozen Demo clock changes nothing and no wall clock is read.
/// </summary>
public static class TimeFormatter
{
    private const int MinutesPerHour = 60;
    private const int MinutesPerDay = 24 * MinutesPerHour;

    /// <summary>Under this a relative time reads "just now" (01 section 8.3).</summary>
    private static readonly TimeSpan JustNow = TimeSpan.FromSeconds(45);

    /// <summary>"Since Tue 4:10 pm" is used up to this many days back; older is a date.</summary>
    private const int WeekdayDays = 6;

    /// <summary>"9:24 pm", "12:05 am": 12-hour, lowercase am/pm, no leading zero, in <paramref name="zone"/>.</summary>
    public static string Clock(DateTimeOffset instant, TimeZoneInfo zone) => Clock(TimeZoneInfo.ConvertTime(instant, zone));

    /// <summary>
    /// The instant as a "since" string without its prefix (01 section 8.3): today "9:24 pm", yesterday "yesterday 4:10 pm", within six days
    /// "Tue 4:10 pm", older "Sep 12". "Today" is the date of <paramref name="now"/> in <paramref name="zone"/>; an instant in the future (clock skew) reads as today.
    /// </summary>
    public static string When(DateTimeOffset instant, DateTimeOffset now, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTime(instant, zone);
        var days = (TimeZoneInfo.ConvertTime(now, zone).Date - local.Date).Days;
        return days switch
        {
            <= 0 => Clock(local),
            1 => "yesterday " + Clock(local),
            <= WeekdayDays => local.ToString("ddd", CultureInfo.InvariantCulture) + " " + Clock(local),
            _ => local.ToString("MMM d", CultureInfo.InvariantCulture),
        };
    }

    /// <summary>"Since 9:24 pm", "Since yesterday 4:10 pm", "Since Tue 4:10 pm", "Since Sep 12" (01 sections 5.1 and 8.3).</summary>
    public static string Since(DateTimeOffset instant, DateTimeOffset now, TimeZoneInfo zone) => "Since " + When(instant, now, zone);

    /// <summary>
    /// How long ago <paramref name="instant"/> was (01 section 8.3): under 45 s "just now", 1 to 59 min "42 min ago", 1 to 23 hrs "3 hr ago", then
    /// <see cref="When"/> (a weekday and time, or a date). Whole units, rounded down; the minute and hour counts follow the elapsed time, not the calendar.
    /// </summary>
    public static string Relative(DateTimeOffset instant, DateTimeOffset now, TimeZoneInfo zone)
    {
        var elapsed = now - instant;
        if (elapsed < JustNow)
        {
            return "just now";
        }

        var minutes = Math.Max(1L, (long)Math.Floor(elapsed.TotalMinutes));
        if (minutes < MinutesPerHour)
        {
            return minutes.ToString(CultureInfo.InvariantCulture) + " min ago";
        }

        var hours = (long)Math.Floor(elapsed.TotalHours);
        return hours < 24 ? hours.ToString(CultureInfo.InvariantCulture) + " hr ago" : When(instant, now, zone);
    }

    /// <summary>"last seen 42 min ago", "last seen 3 hr ago", "last seen Tue 4:10 pm": the tail of the stale and offline lines (01 section 8.4).</summary>
    public static string LastSeen(DateTimeOffset instant, DateTimeOffset now, TimeZoneInfo zone) => "last seen " + Relative(instant, now, zone);

    /// <summary>
    /// "5:31 – 5:48 pm" with an en dash: am/pm once when both ends are in the same half of the day, on both otherwise ("11:50 am – 12:10 pm"),
    /// in <paramref name="zone"/> (01 section 8.3).
    /// </summary>
    public static string Range(DateTimeOffset start, DateTimeOffset end, TimeZoneInfo zone)
    {
        var begin = TimeZoneInfo.ConvertTime(start, zone);
        var finish = TimeZoneInfo.ConvertTime(end, zone);
        var sameHalf = (begin.Hour < 12) == (finish.Hour < 12);
        return (sameHalf ? Digits(begin) : Clock(begin)) + " – " + Clock(finish);
    }

    /// <summary>
    /// The chip text for a member who has been at a place for <paramref name="elapsed"/> (01 sections 4.4 and 8.3): "Here for" and the two
    /// largest non-zero units of days, hours and minutes, singular at 1: "Here for 3 hrs, 33 mins", "Here for 1 hr, 1 min",
    /// "Here for 45 mins", "Here for 2 days, 3 hrs". Whole minutes only (the remainder is dropped); under a minute, and a negative
    /// span from clock skew, the chip reads "Just arrived".
    /// </summary>
    public static string HereForChip(TimeSpan elapsed)
    {
        var totalMinutes = (long)Math.Floor(elapsed.TotalMinutes);
        if (totalMinutes < 1)
        {
            return "Just arrived";
        }

        var days = totalMinutes / MinutesPerDay;
        var hours = totalMinutes % MinutesPerDay / MinutesPerHour;
        var minutes = totalMinutes % MinutesPerHour;

        var parts = new List<string>(2);
        AddUnit(parts, days, "day");
        AddUnit(parts, hours, "hr");
        AddUnit(parts, minutes, "min");
        return "Here for " + string.Join(", ", parts);
    }

    private static string Digits(DateTimeOffset local) => local.ToString("h:mm", CultureInfo.InvariantCulture);

    private static string Clock(DateTimeOffset local) => Digits(local) + (local.Hour < 12 ? " am" : " pm");

    // Only the two largest non-zero units are kept.
    private static void AddUnit(List<string> parts, long count, string unit)
    {
        if (count > 0 && parts.Count < 2)
        {
            parts.Add(count.ToString(CultureInfo.InvariantCulture) + " " + unit + (count == 1 ? string.Empty : "s"));
        }
    }
}
