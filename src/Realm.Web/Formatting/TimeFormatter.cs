using System.Globalization;

namespace Realm.Web.Formatting;

/// <summary>
/// Time strings (03 section 3.10). S5 adds only the one function the map's "Here for" chip needs; S7 completes the class (the 12-hour
/// clock, "Since", relative strings and time ranges of 01 section 8.3). Pure: the caller passes the elapsed time, no clock is read.
/// </summary>
public static class TimeFormatter
{
    private const int MinutesPerHour = 60;
    private const int MinutesPerDay = 24 * MinutesPerHour;

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

    // Only the two largest non-zero units are kept.
    private static void AddUnit(List<string> parts, long count, string unit)
    {
        if (count > 0 && parts.Count < 2)
        {
            parts.Add(count.ToString(CultureInfo.InvariantCulture) + " " + unit + (count == 1 ? string.Empty : "s"));
        }
    }
}
