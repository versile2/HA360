using Realm.Domain;
using Realm.Web.Formatting;
using Realm.Web.Map;

namespace Realm.Web.Sheet;

/// <summary>
/// The view-model factory of 03 section 3.10 for the list rows: it derives the UI-only pieces (the status class, "Near", "far away", the low-battery style, the place
/// a member is in, the distance from me) from the records the data layer delivered and the <c>Ui:*</c> thresholds, and hands the formatters the facts they need. It never
/// builds a <see cref="MemberVm"/>, <see cref="VehicleVm"/> or <see cref="PlaceVm"/>, and never decides freshness. Pure: the instant and the zone are in the <see cref="RowFacts"/>.
/// </summary>
public static class VmFactory
{
    /// <summary>How many mini avatars a place row shows before the "+N".</summary>
    public const int MaxMiniAvatars = 3;

    /// <summary>The people, in the order given (the data layer's stable sort order, 01 section 5.1).</summary>
    public static IReadOnlyList<MemberRowVm> MemberRows(IReadOnlyList<MemberVm> members, RowFacts facts) =>
        [.. members.Select(member => Member(member, facts))];

    /// <summary>The vehicles, in the order given.</summary>
    public static IReadOnlyList<VehicleRowVm> VehicleRows(IReadOnlyList<VehicleVm> vehicles, RowFacts facts) =>
        [.. vehicles.Select(vehicle => Vehicle(vehicle, facts))];

    /// <summary>
    /// The places, occupied first (by people descending, then name), then the empty ones A to Z (01 section 5.3): one ordering, since an empty place has zero people. Names
    /// compare ordinally, ignoring case, so the order is the same on every machine.
    /// </summary>
    public static IReadOnlyList<PlaceRowVm> PlaceRows(IReadOnlyList<PlaceVm> places, RowFacts facts) =>
        [.. places
            .OrderByDescending(place => place.MemberIdsInside.Count)
            .ThenBy(place => place.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(place => Place(place, facts))];

    /// <summary>One person's row.</summary>
    public static MemberRowVm Member(MemberVm member, RowFacts facts)
    {
        var place = facts.PlaceOf(member.PlaceId);
        var status = StatusOf(member, place is not null);
        var hasFix = status != MemberStatus.NoFix;
        var isMe = member.Id == facts.MeId;
        var poorAccuracy = hasFix && member.AccuracyM > facts.Options.PoorAccuracyMeters;

        // Distance from me, for the other people: the origin is mine, or the home zone when I have no fix (the map's origin, so "far" agrees with the pin).
        double? meters = null;
        if (!isMe && hasFix && facts.Origin is { } origin && member.Lat is { } lat && member.Lon is { } lon)
        {
            meters = Geo.DistanceM(origin.Lat, origin.Lon, lat, lon);
        }

        var far = meters > facts.Options.FarAwayKm * 1000;
        var statusLine = MemberTextFormatter.StatusLine(member, status, place?.DisplayName, poorAccuracy, far, facts.Units);
        var timePart = MemberTextFormatter.TimePart(member, status, facts.Now, facts.Zone);

        // 01 section 5.1: the distance is for another member's fresh row; a driving row shows the drive's start only (AC-26), and stale and offline rows use L3 for their warning.
        string? distanceAway = null;
        string? distanceWords = null;
        if (meters is { } away && status is (MemberStatus.AtPlace or MemberStatus.Out))
        {
            distanceAway = MemberTextFormatter.DistanceAway(away, facts.Units);
            distanceWords = UnitFormatter.DistanceWords(away, facts.Units);
        }

        var battery = BatteryOf(member, status, facts);
        return new MemberRowVm(
            Id: member.Id,
            Name: member.DisplayName,
            Lore: string.IsNullOrWhiteSpace(member.LoreTitle) ? null : member.LoreTitle,
            Initial: MapText.Initial(member.DisplayName),
            Color: SafeColor(member.Color),
            AvatarUrl: string.IsNullOrEmpty(member.AvatarUrl) ? null : member.AvatarUrl,
            Status: status,
            StatusLine: statusLine,
            DetailLine: MemberTextFormatter.DetailLine(timePart, distanceAway),
            DetailTone: status switch
            {
                MemberStatus.Stale => LineTone.Warning,
                MemberStatus.Offline => LineTone.Stale,
                _ => LineTone.Normal,
            },
            Battery: battery,
            AccessibleName: MemberTextFormatter.AccessibleName(member.DisplayName, member.LoreTitle, status, statusLine, timePart, battery?.AccessibleName, distanceWords));
    }

    /// <summary>One vehicle's row.</summary>
    public static VehicleRowVm Vehicle(VehicleVm vehicle, RowFacts facts)
    {
        var lore = string.IsNullOrWhiteSpace(vehicle.LoreTitle) ? null : vehicle.LoreTitle;
        if (vehicle.IsPlaceholder)
        {
            var note = string.IsNullOrWhiteSpace(vehicle.PlaceholderNote) ? VehicleTextFormatter.LocationUnavailable : vehicle.PlaceholderNote;
            return new VehicleRowVm(vehicle.Id, vehicle.Name, lore, vehicle.Glyph, true, note, null, false, null, false, string.Empty, LineTone.Normal, VehicleTextFormatter.AccessibleName(vehicle, note, null, string.Empty));
        }

        var location = VehicleTextFormatter.Location(vehicle, facts.PlaceOf(vehicle.PlaceId)?.DisplayName, facts.Units);
        var engine = VehicleTextFormatter.Engine(vehicle);
        var updated = VehicleTextFormatter.Updated(vehicle, facts.Now, facts.Zone);
        return new VehicleRowVm(
            Id: vehicle.Id,
            Name: vehicle.Name,
            Lore: lore,
            Glyph: vehicle.Glyph,
            IsPlaceholder: false,
            LocationLine: location,
            Engine: engine,
            RemoteStart: vehicle.Ignition == IgnitionState.RemoteStart,
            Fuel: vehicle.FuelPct is { } fuel ? VehicleTextFormatter.Fuel(fuel) : null,
            LowFuel: vehicle.FuelPct is { } level && VehicleTextFormatter.IsLowFuel(level),
            Updated: updated,
            UpdatedTone: vehicle.Freshness == Freshness.Stale ? LineTone.Warning : LineTone.Normal,
            AccessibleName: VehicleTextFormatter.AccessibleName(vehicle, location, engine, updated));
    }

    /// <summary>One place's row.</summary>
    public static PlaceRowVm Place(PlaceVm place, RowFacts facts)
    {
        var people = place.MemberIdsInside.Select(facts.MemberOf).OfType<MemberVm>().ToList();
        var count = place.MemberIdsInside.Count;
        return new PlaceRowVm(
            Id: place.Id,
            Name: place.DisplayName,
            Subtitle: place.Subtitle,
            Kind: place.Kind,
            People: count,
            CountText: PlaceTextFormatter.Count(count),
            Avatars: [.. people.Take(MaxMiniAvatars).Select(person => new MiniAvatarVm(person.DisplayName, MapText.Initial(person.DisplayName), SafeColor(person.Color), string.IsNullOrEmpty(person.AvatarUrl) ? null : person.AvatarUrl))],
            More: Math.Max(0, count - MaxMiniAvatars),
            AccessibleName: PlaceTextFormatter.AccessibleName(place.DisplayName, place.Subtitle, count, [.. people.Select(person => person.DisplayName)]));
    }

    /// <summary>
    /// The status class of 01 section 4.3, the first matching row (the same order as the map's pin): no fix, static, offline, stale, driving (fresh only), at a drawn place, out.
    /// </summary>
    public static MemberStatus StatusOf(MemberVm member, bool inDrawnPlace)
    {
        if (member.Freshness == Freshness.NoFix || member.Lat is null || member.Lon is null)
        {
            return MemberStatus.NoFix;
        }

        if (member.Kind == MemberKind.Static || member.Freshness == Freshness.Static)
        {
            return MemberStatus.Static;
        }

        return member.Freshness switch
        {
            Freshness.Offline => MemberStatus.Offline,
            Freshness.Stale => MemberStatus.Stale,
            _ when member.IsDriving => MemberStatus.Driving,
            _ when inDrawnPlace => MemberStatus.AtPlace,
            _ => MemberStatus.Out,
        };
    }

    /// <summary>
    /// The member colour if it is a plain <c>#RGB</c>, <c>#RRGGBB</c> or <c>#RRGGBBAA</c> hex value, else null: the colour comes from the add-on options and goes into a
    /// <c>style</c> attribute, so nothing else (no <c>url()</c>, no extra declaration) may pass.
    /// </summary>
    public static string? SafeColor(string? color)
    {
        if (color is null || color.Length is not (4 or 7 or 9) || color[0] != '#')
        {
            return null;
        }

        for (var index = 1; index < color.Length; index++)
        {
            if (!char.IsAsciiHexDigit(color[index]))
            {
                return null;
            }
        }

        return color;
    }

    private static BatteryBadgeVm? BatteryOf(MemberVm member, MemberStatus status, RowFacts facts)
    {
        if (member.BatteryPct is not { } percent || status == MemberStatus.Static)
        {
            return null;
        }

        var low = percent < facts.Options.LowBatteryPercent;
        var charging = member.Charging == true;
        return new BatteryBadgeVm(
            percent,
            UnitFormatter.Percent(percent),
            charging,
            low,
            MemberTextFormatter.BatteryName(percent, member.Charging, low, member.BatteryAsOfUtc, facts.Now, facts.Zone));
    }
}
