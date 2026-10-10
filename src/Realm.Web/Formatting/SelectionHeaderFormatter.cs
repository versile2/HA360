using System.Globalization;
using Realm.Domain;
using Realm.Web.Map;
using Realm.Web.Sheet;

namespace Realm.Web.Formatting;

/// <summary>
/// The strings of the Peek selection header (01 sections 3.4.2 and 8.4, D45) and of the place detail's heading. It composes what exists and adds only the join: the person's status line, battery
/// pill and accessible name are the row's (<see cref="VmFactory.Member"/>), the vehicle's location, engine and update line are the row's (<see cref="VmFactory.Vehicle"/>), and the time parts
/// come from <see cref="TimeFormatter"/>, so a header and its row cannot disagree. Pure: the instant and the zone are in the <see cref="RowFacts"/> (the session's clock, never the wall clock).
/// </summary>
public static class SelectionHeaderFormatter
{
    /// <summary>The joiner of line 1 and line 2: a middle dot with a space on each side.</summary>
    public const string Separator = " · ";

    /// <summary>The ✕'s accessible name (01 section 10.3).</summary>
    public const string ClearLabel = "Clear selection";

    // The map chip's string of 01 section 4.4 (capital L: it starts the tail, where TimeFormatter.LastSeen is the lower-case tail of the row's warning line).
    private const string LastSeenLead = "Last seen ";

    private const string UpdatedLead = "Updated ";

    /// <summary>
    /// The header of the selected entity, or null when it is not in the lists (a selection is by id and survives data updates, but a person, vehicle or place that has left the lists has no
    /// header; the sheet then shows the list).
    /// </summary>
    public static SelectionHeaderVm? For(
        EntityRef selection,
        IReadOnlyList<MemberVm> members,
        IReadOnlyList<VehicleVm> vehicles,
        IReadOnlyList<PlaceVm> places,
        RowFacts facts)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(members);
        ArgumentNullException.ThrowIfNull(vehicles);
        ArgumentNullException.ThrowIfNull(places);
        ArgumentNullException.ThrowIfNull(facts);
        return selection.Kind switch
        {
            EntityKind.Member => members.FirstOrDefault(member => IsId(member.Id, selection.Id)) is { } person ? Member(person, facts) : null,
            EntityKind.Vehicle => vehicles.FirstOrDefault(vehicle => IsId(vehicle.Id, selection.Id)) is { } car ? Vehicle(car, facts) : null,
            EntityKind.Place => places.FirstOrDefault(place => IsId(place.Id, selection.Id)) is { } zone ? Place(zone) : null,
            _ => null,
        };
    }

    /// <summary>
    /// A person's header: "{name} · {lore}", then "{status line} · {Since 9:06 pm | Updated 3 min ago | Last seen 42 min ago}" (see <see cref="MemberTail"/>), the row's battery pill and the pin's
    /// accessible name. The distance ("1.0 mi away") is not repeated here.
    /// </summary>
    public static SelectionHeaderVm Member(MemberVm member, RowFacts facts)
    {
        ArgumentNullException.ThrowIfNull(member);
        ArgumentNullException.ThrowIfNull(facts);
        var row = VmFactory.Member(member, facts);
        return new SelectionHeaderVm(
            Entity: new EntityRef(EntityKind.Member, member.Id),
            Line1: row.Name,
            Line1Lore: row.Lore,
            Line2Lead: row.StatusLine,
            Line2Tail: MemberTail(member, row.Status, facts),
            Initial: row.Initial,
            Color: row.Color,
            AvatarUrl: row.AvatarUrl,
            Glyph: row.Glyph,
            ZoneKind: null,
            Battery: row.Battery,
            AccessibleName: row.AccessibleName);
    }

    /// <summary>
    /// The time part of a person's line 2 (01 section 3.4.2): "Since {time}" for a fresh member with an arrival time, "Updated {relative}" for a fresh one without, "Last seen {relative}" for a stale or
    /// offline one (the map chip's string, 4.4), and none for a member with no fix and for the static prince. Null when there is none.
    /// </summary>
    public static string? MemberTail(MemberVm member, MemberStatus status, RowFacts facts)
    {
        ArgumentNullException.ThrowIfNull(member);
        ArgumentNullException.ThrowIfNull(facts);
        switch (status)
        {
            case MemberStatus.NoFix:
            case MemberStatus.Static:
                return null;
            case MemberStatus.Stale:
            case MemberStatus.Offline:
                return member.LastUpdateUtc is { } seen ? LastSeenLead + TimeFormatter.Relative(seen, facts.Now, facts.Zone) : null;
            default:
                if (member.SinceUtc is { } since)
                {
                    return TimeFormatter.Since(since, facts.Now, facts.Zone);
                }

                return member.LastUpdateUtc is { } updated ? UpdatedLead + TimeFormatter.Relative(updated, facts.Now, facts.Zone) : null;
        }
    }

    /// <summary>
    /// A vehicle's header: "{name} · {lore}", then "{location} · {Updated 20 min ago | Last heard 1 hr ago}", both parts the row's (no battery). A vehicle with no update time has no tail.
    /// </summary>
    public static SelectionHeaderVm Vehicle(VehicleVm vehicle, RowFacts facts)
    {
        ArgumentNullException.ThrowIfNull(vehicle);
        ArgumentNullException.ThrowIfNull(facts);
        var row = VmFactory.Vehicle(vehicle, facts);
        return new SelectionHeaderVm(
            Entity: new EntityRef(EntityKind.Vehicle, vehicle.Id),
            Line1: row.Name,
            Line1Lore: row.Lore,
            Line2Lead: row.LocationLine,
            Line2Tail: row.Updated.Length > 0 ? row.Updated : null,
            Initial: string.Empty,
            Color: null,
            AvatarUrl: null,
            Glyph: row.Glyph,
            ZoneKind: null,
            Battery: null,
            AccessibleName: row.AccessibleName);
    }

    /// <summary>A place's header: its display name, then "Here now (2)" or "Empty" (people plus vehicles inside, the count of the place detail's heading).</summary>
    public static SelectionHeaderVm Place(PlaceVm place)
    {
        ArgumentNullException.ThrowIfNull(place);
        var inside = Occupants(place);
        var line2 = inside == 0 ? PlaceTextFormatter.Empty : HereNow(inside);
        return new SelectionHeaderVm(
            Entity: new EntityRef(EntityKind.Place, place.Id),
            Line1: place.DisplayName,
            Line1Lore: null,
            Line2Lead: line2,
            Line2Tail: null,
            Initial: string.Empty,
            Color: null,
            AvatarUrl: null,
            Glyph: null,
            ZoneKind: place.Kind,
            Battery: null,
            AccessibleName: place.DisplayName + ". " + line2 + ".");
    }

    /// <summary>People plus vehicles inside the place (01 sections 3.4.2, 5.3 and 5.6); the list row's "n here" counts people only.</summary>
    public static int Occupants(PlaceVm place)
    {
        ArgumentNullException.ThrowIfNull(place);
        return place.MemberIdsInside.Count + place.VehicleIdsInside.Count;
    }

    /// <summary>"Here now (2)": line 2 of an occupied place's header and the heading of the place detail's list.</summary>
    public static string HereNow(int count) => "Here now (" + count.ToString(CultureInfo.InvariantCulture) + ")";

    // ---- the detail views (01 sections 5.4 to 5.6): the strings that are not the header's or the row's -----------------------------------------------------------

    /// <summary>The detail's back button (01 sections 5.4 and 10.3).</summary>
    public const string BackLabel = "Back";

    /// <summary>The heading of the person detail's week block (drawn in capitals by the style).</summary>
    public const string ThisWeek = "This week";

    /// <summary>The link of the week block to the driver's report; the arrow is drawn apart, hidden from screen readers.</summary>
    public const string FullReport = "Full report";

    /// <summary>The place detail's list when nobody and nothing is inside (01 section 5.6).</summary>
    public const string NobodyHere = "Nobody's here. The hall stands empty.";

    /// <summary>The caption at the bottom of the place detail (zones are managed in Home Assistant).</summary>
    public const string PlacesCaption = "Places are set up in Home Assistant.";

    /// <summary>
    /// The full address line of the person detail (01 section 5.4): the data layer's address, or null when there is none or when the status line above it (the card's title) already says the
    /// same place. Compared with the middle dot, commas, full stops, spacing and case ignored, and as whole words, so "Eastgate Avenue, Pinebrook, AL" is not repeated under
    /// "Eastgate Avenue · Pinebrook, AL" and "I-65" is not repeated under "Driving · 54 mph on I-65".
    /// </summary>
    public static string? MemberAddress(MemberVm member, string statusLine)
    {
        ArgumentNullException.ThrowIfNull(member);
        if (string.IsNullOrWhiteSpace(member.FullAddress))
        {
            return null;
        }

        var address = NormalisePlaceText(member.FullAddress);
        var status = NormalisePlaceText(statusLine ?? string.Empty);
        return address.Length > 0 && (" " + status + " ").Contains(" " + address + " ", StringComparison.Ordinal) ? null : member.FullAddress;
    }

    /// <summary>The two text lines of the person detail's status card (01 section 5.4): the title and the line under it.</summary>
    public readonly record struct MemberCardLines(string Title, string Detail);

    /// <summary>
    /// The status card's lines for a person (01 section 5.4, D108). A fresh live member (at a place, out, driving) says how fresh the fix is, once: "Since 9:06 pm · updated 3 min ago · 1.0 mi away" under
    /// the title, or on the title itself while driving ("Driving · 54 mph on I-65 · updated 1 min ago"). A member with no arrival time already reads "Updated 3 min ago" as the time line, and stale,
    /// offline, no-fix and static members say theirs in their own line, so those rows are the list row's, unchanged.
    /// </summary>
    public static MemberCardLines MemberCard(MemberVm member, MemberRowVm row, RowFacts facts)
    {
        ArgumentNullException.ThrowIfNull(member);
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(facts);
        if (row.Status is not (MemberStatus.AtPlace or MemberStatus.Out or MemberStatus.Driving) || member.LastUpdateUtc is not { } at)
        {
            return new MemberCardLines(row.StatusLine, row.DetailLine);
        }

        var updated = "updated " + TimeFormatter.Relative(at, facts.Now, facts.Zone);
        var timePart = MemberTextFormatter.TimePart(member, row.Status, facts.Now, facts.Zone);
        var rest = row.DetailLine.StartsWith(timePart, StringComparison.Ordinal) ? row.DetailLine[timePart.Length..] : string.Empty;
        if (row.Status == MemberStatus.Driving)
        {
            // The drive's start stays on its own line; without one that line would only repeat the freshness.
            return new MemberCardLines(row.StatusLine + " · " + updated, member.SinceUtc is null ? rest.TrimStart(' ', '·') : row.DetailLine);
        }

        return member.SinceUtc is null
            ? new MemberCardLines(row.StatusLine, row.DetailLine)
            : new MemberCardLines(row.StatusLine, timePart + " · " + updated + rest);
    }

    private static string NormalisePlaceText(string text) =>
        string.Join(' ', text.Split(['·', ',', '.', ' ', '\t', '\u00A0'], StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();

    /// <summary>The battery chip of the person detail (01 section 5.4): "19% · Charging", "12% · Low battery", both parts when both apply. The chip's accessible name is the badge's.</summary>
    public static string BatteryChip(BatteryBadgeVm battery)
    {
        ArgumentNullException.ThrowIfNull(battery);
        return battery.Text + (battery.Charging ? " · Charging" : string.Empty) + (battery.Low ? " · Low battery" : string.Empty);
    }

    /// <summary>
    /// The accuracy chip of the person detail (01 section 5.4): "Approximate · ± 0.5 mi", only when the fix has an accuracy above the poor-accuracy threshold (500 m) and the member has a fix to be
    /// approximate about. Null otherwise.
    /// </summary>
    public static string? AccuracyChip(MemberVm member, MemberStatus status, RowFacts facts)
    {
        ArgumentNullException.ThrowIfNull(member);
        ArgumentNullException.ThrowIfNull(facts);
        return status is not (MemberStatus.NoFix or MemberStatus.Static) && member.AccuracyM is { } meters && meters > facts.Options.PoorAccuracyMeters
            ? "Approximate · ± " + UnitFormatter.Distance(meters, facts.Units)
            : null;
    }

    /// <summary>
    /// The static member's distance row (01 section 5.4, "681 mi away"): measured from the viewer's origin like the list rows', which leave it out for a static member. Null when either end has no position
    /// or the member is the viewer.
    /// </summary>
    public static string? StaticDistance(MemberVm member, RowFacts facts)
    {
        ArgumentNullException.ThrowIfNull(member);
        ArgumentNullException.ThrowIfNull(facts);
        return member.Id != facts.MeId && facts.Origin is { } origin && member.Lat is { } lat && member.Lon is { } lon
            ? MemberTextFormatter.DistanceAway(Geo.DistanceM(origin.Lat, origin.Lon, lat, lon), facts.Units)
            : null;
    }

    /// <summary>
    /// The static member's lore sentence (01 sections 5.4 and 8.4): "The Prince of the Peaks keeps his own counsel." built from the lore title, which is an add-on option and so not a constant. A title
    /// that already starts with "The" takes no second one; no title, no sentence.
    /// </summary>
    public static string? StaticSentence(MemberVm member)
    {
        ArgumentNullException.ThrowIfNull(member);
        if (string.IsNullOrWhiteSpace(member.LoreTitle))
        {
            return null;
        }

        var title = member.LoreTitle.Trim();
        return (title.StartsWith("The ", StringComparison.OrdinalIgnoreCase) ? title : "The " + title) + " keeps his own counsel.";
    }

    /// <summary>
    /// The three tiles of the person detail's week block (01 section 5.4): drives, miles and top speed, each a value and a label ("18" "drives", "202.6" "mi", "88 mph" "top"). A tile with unknown data
    /// (no week yet, a null value, a driver who was not covered) has the value "—" (01 section 6.6).
    /// </summary>
    public static IReadOnlyList<(string Value, string Label)> WeekTiles(DriverSummary? week, double? topSpeedMps)
    {
        if (week is null)
        {
            return [(DrivingFormatter.Dash, "drives"), (DrivingFormatter.Dash, "mi"), (DrivingFormatter.Dash, "top")];
        }

        var drives = week.Covered ? week.Drives ?? 0 : 0;
        return
        [
            (DrivingFormatter.DrivesTile(week), DrivingFormatter.Drives(drives)),
            (DrivingFormatter.MilesTile(week), "mi"),
            (DrivingFormatter.TopSpeedTile(week, topSpeedMps), "top"),
        ];
    }

    /// <summary>
    /// The vehicle detail's Location row (01 section 5.5): "At Hearth Haven", the street, "Somewhere in the Realm", "Location unavailable". Unlike the row's line it never reads "Driving": the speed
    /// has a row of its own, and the place or street shows while the vehicle moves.
    /// </summary>
    public static string VehicleLocation(VehicleVm vehicle, RowFacts facts)
    {
        ArgumentNullException.ThrowIfNull(vehicle);
        ArgumentNullException.ThrowIfNull(facts);
        return VehicleTextFormatter.Location(vehicle with { IsMoving = false }, facts.PlaceOf(vehicle.PlaceId)?.DisplayName, facts.Units);
    }

    /// <summary>The vehicle detail's Speed row (01 section 5.5, "62 mph"): only while the vehicle is moving and its speed is known; null omits the row.</summary>
    public static string? VehicleSpeed(VehicleVm vehicle, RowFacts facts)
    {
        ArgumentNullException.ThrowIfNull(vehicle);
        ArgumentNullException.ThrowIfNull(facts);
        return vehicle.IsMoving && vehicle.SpeedMps is { } speed ? UnitFormatter.Speed(speed, facts.Units) : null;
    }

    /// <summary>
    /// The vehicle detail's Last update row (01 section 5.5): the clock time with the relative age in parentheses, "9:05 pm (20 min ago)"; a time that is not today carries its day, and a relative age that
    /// is the same words is not repeated. Null when no update time is known.
    /// </summary>
    public static string? VehicleLastUpdate(VehicleVm vehicle, RowFacts facts)
    {
        ArgumentNullException.ThrowIfNull(vehicle);
        ArgumentNullException.ThrowIfNull(facts);
        if (vehicle.LastUpdateUtc is not { } at)
        {
            return null;
        }

        var when = TimeFormatter.When(at, facts.Now, facts.Zone);
        var relative = TimeFormatter.Relative(at, facts.Now, facts.Zone);
        return string.Equals(when, relative, StringComparison.Ordinal) ? when : when + " (" + relative + ")";
    }

    /// <summary>The warning line at the top of a stale vehicle's detail (01 section 5.5): the row's update line with a full stop, "Last heard 1 hr ago."; null for a vehicle that is not stale.</summary>
    public static string? VehicleStaleWarning(VehicleVm vehicle, RowFacts facts)
    {
        ArgumentNullException.ThrowIfNull(vehicle);
        ArgumentNullException.ThrowIfNull(facts);
        return vehicle.Freshness == Freshness.Stale && vehicle.LastUpdateUtc is not null ? VehicleTextFormatter.Updated(vehicle, facts.Now, facts.Zone) + "." : null;
    }

    /// <summary>The "Since 5:52 pm" line of a Here-now row in the place detail (01 section 5.6); empty when the arrival time is unknown.</summary>
    public static string HereSince(MemberVm member, RowFacts facts)
    {
        ArgumentNullException.ThrowIfNull(member);
        ArgumentNullException.ThrowIfNull(facts);
        return member.SinceUtc is { } since ? TimeFormatter.Since(since, facts.Now, facts.Zone) : string.Empty;
    }

    private static bool IsId(string left, string right) => string.Equals(left, right, StringComparison.Ordinal);
}
