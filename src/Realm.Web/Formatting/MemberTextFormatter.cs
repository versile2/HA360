using System.Globalization;
using System.Text;
using Realm.Domain;
using Realm.Web.Map;

namespace Realm.Web.Formatting;

/// <summary>
/// The person strings of 01 sections 5.1, 8.4 and 10.3: the status line (L2), the time part of L3, the battery name and the accessible name of a row. The
/// caller decides the facts that need the rest of the snapshot (the status class, the place name, "poor accuracy", "far away", the distance from me) and
/// passes them in, so every function here is pure and testable on one <see cref="MemberVm"/>. Straight apostrophes and a spaced em dash, as the spec prints them.
/// </summary>
public static class MemberTextFormatter
{
    /// <summary>A fresh member with no street and no place (O-2). Never prefixed with "Near".</summary>
    public const string SomewhereInTheRealm = "Somewhere in the Realm";

    /// <summary>L2 of a member with no fix.</summary>
    public const string LocationUnavailable = "Location unavailable";

    /// <summary>L3 of a member with no fix.</summary>
    public const string ScoutsHaveNotReported = "The scouts have not reported";

    /// <summary>L3 of the static prince.</summary>
    public const string LocationNotShared = "Location isn't shared";

    /// <summary>L2 of a static member that has no label (the demo prince has one).</summary>
    public const string FixedPosition = "Fixed position";

    /// <summary>The lead of the stale line: "The raven's late — last seen 42 min ago".</summary>
    public const string StaleLead = "The raven's late";

    /// <summary>The lead of the offline line: "Gone dark — last seen Tue 4:10 pm".</summary>
    public const string OfflineLead = "Gone dark";

    /// <summary>A battery reading older than this adds "(battery as of {relative})" to the accessible name (R-113).</summary>
    public static readonly TimeSpan BatteryAgeThreshold = TimeSpan.FromMinutes(15);

    /// <summary>
    /// L2 of the row (01 section 5.1), first match wins by <paramref name="status"/>: "Driving · 54 mph on Maple Street" (the speed only when one was reported, the street
    /// only when known), "At Hearth Haven", the street ("Near {street}" when <paramref name="poorAccuracy"/>), "{street} · {City}, {ST}" when
    /// <paramref name="far"/>, or "Somewhere in the Realm". Stale and offline members keep their last known line. <paramref name="placeName"/> is the display name of
    /// the zone the member is in, or null.
    /// </summary>
    public static string StatusLine(MemberVm member, MemberStatus status, string? placeName, bool poorAccuracy, bool far, UnitSystem units = UnitSystem.Imperial) =>
        status switch
        {
            MemberStatus.NoFix => LocationUnavailable,
            MemberStatus.Static => string.IsNullOrWhiteSpace(member.StaticLabel) ? FixedPosition : member.StaticLabel,
            MemberStatus.Driving => Driving(member, units),
            _ => Location(member, placeName, poorAccuracy, far),
        };

    /// <summary>
    /// The time part of L3: "Since 9:06 pm" (or "Updated 3 min ago" when there is no arrival time), the warning lines of a stale or offline member, "The scouts have not
    /// reported" with no fix and "Location isn't shared" for the static prince. Empty when nothing is known.
    /// </summary>
    public static string TimePart(MemberVm member, MemberStatus status, DateTimeOffset now, TimeZoneInfo zone) =>
        status switch
        {
            MemberStatus.NoFix => ScoutsHaveNotReported,
            MemberStatus.Static => LocationNotShared,
            MemberStatus.Stale => WarningLine(StaleLead, member, now, zone),
            MemberStatus.Offline => WarningLine(OfflineLead, member, now, zone),
            _ when member.SinceUtc is { } since => TimeFormatter.Since(since, now, zone),
            _ when member.LastUpdateUtc is { } updated => "Updated " + TimeFormatter.Relative(updated, now, zone),
            _ => string.Empty,
        };

    /// <summary>"1.0 mi away" (01 section 5.1): the distance from me, for another member.</summary>
    public static string DistanceAway(double meters, UnitSystem units = UnitSystem.Imperial) => UnitFormatter.Distance(meters, units) + " away";

    /// <summary>L3: the time part and, when there is one, " · {distance} away". Either part may be empty.</summary>
    public static string DetailLine(string timePart, string? distanceAway) =>
        string.IsNullOrEmpty(distanceAway) ? timePart : string.IsNullOrEmpty(timePart) ? distanceAway : timePart + " · " + distanceAway;

    /// <summary>
    /// The battery clause of the accessible name (01 sections 5.1 and 10.3): "Battery 12 percent, charging, low", with " (battery as of 42 min ago)" when the reading is
    /// older than <see cref="BatteryAgeThreshold"/>. The visual pill never shows that part.
    /// </summary>
    public static string BatteryName(int percent, bool? charging, bool low, DateTimeOffset? asOf, DateTimeOffset now, TimeZoneInfo zone)
    {
        var name = new StringBuilder("Battery ").Append(percent.ToString(CultureInfo.InvariantCulture)).Append(" percent");
        if (charging == true)
        {
            name.Append(", charging");
        }

        if (low)
        {
            name.Append(", low");
        }

        if (asOf is { } reading && now - reading > BatteryAgeThreshold)
        {
            name.Append(" (battery as of ").Append(TimeFormatter.Relative(reading, now, zone)).Append(')');
        }

        return name.ToString();
    }

    /// <summary>
    /// The pin's accessible name (01 section 10.3): "Cass, The Royal Jester. At The Jester's Hall since 9:06 pm. Battery 12 percent, low. 1.0 mile away." The status line and
    /// the time part are read as one sentence for a fresh member and as two for the rest; the middle dots and dashes become commas for the screen reader.
    /// </summary>
    /// <param name="name">The display name.</param>
    /// <param name="lore">The lore title, or null.</param>
    /// <param name="status">The status class.</param>
    /// <param name="statusLine">L2.</param>
    /// <param name="timePart">The time part of L3 (see <see cref="TimePart"/>).</param>
    /// <param name="batteryName">The clause of <see cref="BatteryName"/>, or null when there is no battery.</param>
    /// <param name="distanceWords">The spoken distance ("1.0 mile") or null.</param>
    public static string AccessibleName(string name, string? lore, MemberStatus status, string statusLine, string timePart, string? batteryName, string? distanceWords)
    {
        var text = new StringBuilder(string.IsNullOrWhiteSpace(lore) ? name : name + ", " + lore).Append(". ").Append(Spoken(statusLine));
        var fresh = status is not (MemberStatus.NoFix or MemberStatus.Static or MemberStatus.Stale or MemberStatus.Offline);
        if (timePart.Length > 0)
        {
            text.Append(fresh ? " " + char.ToLowerInvariant(timePart[0]) + Spoken(timePart[1..]) : ". " + Spoken(timePart));
        }

        text.Append('.');
        if (batteryName is not null)
        {
            text.Append(' ').Append(batteryName).Append('.');
        }

        if (distanceWords is not null)
        {
            text.Append(' ').Append(distanceWords).Append(" away.");
        }

        return text.ToString();
    }

    private static string Spoken(string text) => text.Replace(" · ", ", ", StringComparison.Ordinal).Replace(" — ", ", ", StringComparison.Ordinal);

    private static string Driving(MemberVm member, UnitSystem units)
    {
        var line = new StringBuilder("Driving");
        if (member.SpeedMps is { } speed)
        {
            line.Append(" · ").Append(UnitFormatter.Speed(speed, units));
        }

        if (!string.IsNullOrWhiteSpace(member.Street))
        {
            line.Append(" on ").Append(member.Street);
        }

        return line.ToString();
    }

    private static string Location(MemberVm member, string? placeName, bool poorAccuracy, bool far)
    {
        if (placeName is not null)
        {
            return "At " + placeName;
        }

        if (string.IsNullOrWhiteSpace(member.Street))
        {
            return SomewhereInTheRealm;
        }

        if (far)
        {
            return string.IsNullOrWhiteSpace(member.City) || string.IsNullOrWhiteSpace(member.Region)
                ? member.Street
                : member.Street + " · " + member.City + ", " + member.Region;
        }

        return poorAccuracy ? "Near " + member.Street : member.Street;
    }

    private static string WarningLine(string lead, MemberVm member, DateTimeOffset now, TimeZoneInfo zone) =>
        member.LastUpdateUtc is { } seen ? lead + " — " + TimeFormatter.LastSeen(seen, now, zone) : lead;
}
