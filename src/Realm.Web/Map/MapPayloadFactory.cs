using Realm.Domain;
using Realm.Web.Formatting;

namespace Realm.Web.Map;

/// <summary>
/// Builds the payloads of 03 section 4.5 from the view models of one <see cref="RealmSnapshot"/> and the thresholds of
/// <see cref="MapPayloadOptions"/>: the ring, badge, draw-order class, strings and avatar URL of every pin, the zones with the
/// 5 km exclusion, the default view, and the layout. Pure and deterministic: the instant comes from the caller (the session's
/// <c>Time</c>, never the wall clock) and the version from the caller's per-circuit counter. The lists are separate parameters so
/// <c>MapView</c> can send only the sections whose input changed.
/// </summary>
public static class MapPayloadFactory
{
    private const double RingWidthPx = 4;
    private const double VehicleRingWidthPx = 3;

    /// <summary>
    /// The viewer's member id: <paramref name="requestedId"/> when it names a member, else the first live member in sort order (the third
    /// step of the chain in 02 section 2.5, which the Viewer resolver of S8 will have already run). Null when there is no live member.
    /// </summary>
    public static string? ResolveMeId(IReadOnlyList<MemberVm> members, string? requestedId)
    {
        if (requestedId is not null && members.Any(member => member.Id == requestedId))
        {
            return requestedId;
        }

        return members.Where(member => member.Kind == MemberKind.Live).OrderBy(member => member.SortOrder).FirstOrDefault()?.Id;
    }

    /// <summary>
    /// The member payload. <paramref name="meId"/> goes through <see cref="ResolveMeId"/>; <paramref name="now"/> sets the chip and its minute. <paramref name="selection"/> decides who
    /// carries a chip (01 section 4.4, <see cref="MemberChip"/>): the selected member, or mine when nothing is selected. <paramref name="zone"/> is the session's zone (the clock part of a
    /// relative time that is a day old or more; the default is UTC) and <paramref name="units"/> the unit system of the speed.
    /// </summary>
    public static MembersPayload Members(
        IReadOnlyList<MemberVm> members,
        IReadOnlyList<PlaceVm> places,
        string? meId,
        DateTimeOffset now,
        MapPayloadOptions options,
        int version,
        EntityRef? selection = null,
        TimeZoneInfo? zone = null,
        UnitSystem units = UnitSystem.Imperial)
    {
        var me = ResolveMeId(members, meId);
        var drawn = DrawnPlaces(places, options);
        var origin = DefaultViewPlanner.Origin(members, places, me);
        var minute = now.ToUnixTimeSeconds() / 60;
        var items = new List<MemberPayloadItem>(members.Count);
        foreach (var member in members)
        {
            items.Add(Member(member, member.Id == me, drawn, origin, now, minute, options, selection, zone ?? TimeZoneInfo.Utc, units));
        }

        return new MembersPayload(version, me ?? string.Empty, items);
    }

    /// <summary>The vehicle payload with no selection, so no chip (see the overload with the selection).</summary>
    public static VehiclesPayload Vehicles(IReadOnlyList<VehicleVm> vehicles, IReadOnlyList<PlaceVm> places, int version) =>
        Vehicles(vehicles, places, version, selection: null, now: default, zone: TimeZoneInfo.Utc);

    /// <summary>
    /// The vehicle payload (01 section 4.7): the ring follows <c>Freshness</c> first and <c>IsMoving</c> second; a vehicle without a position, and the placeholder, have no coordinates and so no pin.
    /// Only the selected vehicle carries a chip (01 section 4.4, <see cref="VehicleChip"/>).
    /// </summary>
    public static VehiclesPayload Vehicles(
        IReadOnlyList<VehicleVm> vehicles,
        IReadOnlyList<PlaceVm> places,
        int version,
        EntityRef? selection,
        DateTimeOffset now,
        TimeZoneInfo zone,
        UnitSystem units = UnitSystem.Imperial)
    {
        ArgumentNullException.ThrowIfNull(zone);
        var items = new List<VehiclePayloadItem>(vehicles.Count);
        foreach (var vehicle in vehicles)
        {
            var selected = selection is { Kind: EntityKind.Vehicle } chosen && string.Equals(chosen.Id, vehicle.Id, StringComparison.Ordinal);
            items.Add(Vehicle(vehicle, places, selected ? VehicleChip(vehicle, now, zone, units) : null));
        }

        return new VehiclesPayload(version, items);
    }

    /// <summary>
    /// The chip text of a member (01 section 4.4), decided here and never in JavaScript. The selected member has one whatever its status: at a place "Here for 3 hrs, 33 mins" (or "Just arrived"),
    /// driving "Driving · 54 mph" (the speed only when one was reported, R-112) or "Driving", stale or offline "Last seen 42 min ago", the static pin its label ("Home · Highmeadow"); out and
    /// not driving, and a member with no fix, have none. The viewer's own chip, when nothing is selected, is the at-a-place one and no other (matches screenshot 7784); once anything else is
    /// selected (a person, a vehicle or a place) it goes, so that exactly one pin carries a chip. The text is blank (null) when the fact it needs, the arrival or the last update, is unknown.
    /// </summary>
    public static string? MemberChip(
        MemberVm member,
        MemberStatus status,
        bool isMe,
        EntityRef? selection,
        DateTimeOffset now,
        TimeZoneInfo zone,
        UnitSystem units = UnitSystem.Imperial)
    {
        ArgumentNullException.ThrowIfNull(member);
        ArgumentNullException.ThrowIfNull(zone);
        var selected = selection is { Kind: EntityKind.Member } chosen && string.Equals(chosen.Id, member.Id, StringComparison.Ordinal);
        var mine = isMe && selection is null && status == MemberStatus.AtPlace;
        if (!selected && !mine)
        {
            return null;
        }

        return status switch
        {
            MemberStatus.AtPlace => member.SinceUtc is { } since ? TimeFormatter.HereForChip(now - since) : null,
            MemberStatus.Driving => member.SpeedMps is { } speed ? "Driving · " + UnitFormatter.Speed(speed, units) : "Driving",
            MemberStatus.Stale or MemberStatus.Offline => member.LastUpdateUtc is { } seen ? "Last seen " + TimeFormatter.Relative(seen, now, zone) : null,
            MemberStatus.Static => string.IsNullOrWhiteSpace(member.StaticLabel) ? MemberTextFormatter.FixedPosition : member.StaticLabel,
            _ => null,
        };
    }

    /// <summary>
    /// The chip text of the selected vehicle (01 section 4.4). A stale vehicle reads "Last heard 1 hr ago" and never "Driving", whatever the engine says; a fresh moving one "Driving · 62 mph" (the
    /// speed only when <see cref="VehicleVm.SpeedMps"/> is known, else "Driving"); a fresh one that is not moving "Parked · Engine off" or "Parked · Accessory on", and "Engine on" alone with the engine
    /// running (a vehicle that idles is not parked; the remote start reads the same way, with its minutes left). Without an ignition reading the chip is "Parked". A vehicle with no fix or no position has
    /// no pin and the placeholder none, so no chip; a stale one with no update time has no chip either.
    /// </summary>
    public static string? VehicleChip(VehicleVm vehicle, DateTimeOffset now, TimeZoneInfo zone, UnitSystem units = UnitSystem.Imperial)
    {
        ArgumentNullException.ThrowIfNull(vehicle);
        ArgumentNullException.ThrowIfNull(zone);
        if (!HasPin(vehicle))
        {
            return null;
        }

        if (vehicle.Freshness == Freshness.Stale)
        {
            return vehicle.LastUpdateUtc is { } heard ? "Last heard " + TimeFormatter.Relative(heard, now, zone) : null;
        }

        if (vehicle.IsMoving)
        {
            return vehicle.SpeedMps is { } speed ? "Driving · " + UnitFormatter.Speed(speed, units) : "Driving";
        }

        var engine = VehicleTextFormatter.Engine(vehicle);
        return vehicle.Ignition switch
        {
            IgnitionState.On or IgnitionState.RemoteStart => engine,
            _ => engine is null ? "Parked" : "Parked · " + engine,
        };
    }

    /// <summary>The zones: every place whose radius is above 0 and at most <see cref="MapPayloadOptions.MaxZoneRadiusKm"/> (the arrival zone is never sent), with the occupied flag (people only) and the three appearances.</summary>
    public static ZonesPayload Zones(IReadOnlyList<PlaceVm> places, bool show, MapPayloadOptions options, int version)
    {
        // 01 section 5.3 (R2-009): "A place is occupied when at least one person is inside it ... a vehicle never makes a place occupied", and the zone's occupied fill (4.6) counts people only.
        var zones = DrawnPlaces(places, options).Values
            .Select(place => new ZoneItem(place.Id, place.DisplayName, place.Lat, place.Lon, place.RadiusM, place.MemberIdsInside.Count > 0))
            .ToList();
        return new ZonesPayload(version, show, zones, new ZoneAppearances(MapPalette.ZoneDark, MapPalette.ZoneLight, MapPalette.ZoneImagery));
    }

    /// <summary>The default view and the "me alone" target of 01 section 4.9; null when no position stands for me (no fix and no home zone), in which case nothing should be sent.</summary>
    public static DefaultTargets? Targets(
        IReadOnlyList<MemberVm> members,
        IReadOnlyList<VehicleVm> vehicles,
        IReadOnlyList<PlaceVm> places,
        string? meId,
        MapPayloadOptions options,
        int version) =>
        DefaultViewPlanner.Plan(members, vehicles, places, ResolveMeId(members, meId), options, version);

    /// <summary>The layout payload; the panel is 16 px from the edge and 400 px wide in Expanded (0 when hidden), and absent in Compact.</summary>
    public static LayoutPayload Layout(MapLayout layout)
    {
        var expanded = layout.Mode == MapLayoutMode.Expanded;
        return new LayoutPayload(
            layout.Mode,
            MapLayout.PanelLeftPx,
            expanded && !layout.PanelHidden ? MapLayout.PanelWidthPx : 0,
            layout.PanelHidden,
            layout.StackVisible,
            layout.Safe,
            MapLayout.NavHeightPx);
    }

    // Zones the map draws, by id: a radius above the maximum (the 32,187 m arrival zone) is excluded, as is a degenerate zero radius.
    private static Dictionary<string, PlaceVm> DrawnPlaces(IReadOnlyList<PlaceVm> places, MapPayloadOptions options)
    {
        var maxRadiusM = options.MaxZoneRadiusKm * 1000;
        var drawn = new Dictionary<string, PlaceVm>(places.Count);
        foreach (var place in places)
        {
            if (place.RadiusM > 0 && place.RadiusM <= maxRadiusM)
            {
                drawn[place.Id] = place;
            }
        }

        return drawn;
    }

    /// <summary>
    /// The pin's accessible name of 01 section 10.3, the same words as the list row (<see cref="MemberTextFormatter.AccessibleName"/>): the status, the time part, the battery and, for a fresh
    /// member other than me, the distance ("Cass, The Royal Jester. At The Jester's Hall since 9:06 pm. Battery 12 percent, low. 1.0 mile away.", AC-46).
    /// </summary>
    private static string PinName(MemberVm member, MemberStatus status, PlaceVm? place, bool poorAccuracy, bool lowBattery, bool far, double? meters, DateTimeOffset now, TimeZoneInfo zone, UnitSystem units)
    {
        var statusLine = MemberTextFormatter.StatusLine(member, status, place?.DisplayName, poorAccuracy, far, units);
        var timePart = MemberTextFormatter.TimePart(member, status, now, zone);
        string? battery = member.BatteryPct is { } percent && status != MemberStatus.Static
            ? MemberTextFormatter.BatteryName(percent, member.Charging, lowBattery, member.BatteryAsOfUtc, now, zone)
            : null;
        string? distanceWords = meters is { } away && status is (MemberStatus.AtPlace or MemberStatus.Out) ? UnitFormatter.DistanceWords(away, units) : null;
        return MemberTextFormatter.AccessibleName(member.DisplayName, member.LoreTitle, status, statusLine, timePart, battery, distanceWords);
    }

    private static MemberPayloadItem Member(
        MemberVm member,
        bool isMe,
        Dictionary<string, PlaceVm> drawn,
        (double Lat, double Lon)? origin,
        DateTimeOffset now,
        long minute,
        MapPayloadOptions options,
        EntityRef? selection,
        TimeZoneInfo zone,
        UnitSystem units)
    {
        var status = StatusOf(member, drawn);
        var hasFix = status != MemberStatus.NoFix;
        (double Lat, double Lon)? position = hasFix && member.Lat is { } memberLat && member.Lon is { } memberLon ? (memberLat, memberLon) : null;
        var poorAccuracy = hasFix && member.AccuracyM > options.PoorAccuracyMeters;
        var lowBattery = status != MemberStatus.Static && member.BatteryPct < options.LowBatteryPercent;
        var place = member.PlaceId is { } placeId && drawn.TryGetValue(placeId, out var found) ? found : null;

        var far = false;
        (double Meters, double BearingDeg)? fromMe = null;
        if (position is { } at && !isMe && origin is { } start)
        {
            fromMe = MapText.Leg(start.Lat, start.Lon, at.Lat, at.Lon);
            far = fromMe.Value.Meters > options.FarAwayKm * 1000;
        }

        // 01 section 4.4: the chip follows the selection (the selected member's, mine when nothing is selected).
        var chip = MemberChip(member, status, isMe, selection, now, zone, units);

        return new MemberPayloadItem(
            Id: member.Id,
            Name: member.DisplayName,
            Initial: MapText.Initial(member.DisplayName),
            Color: member.Color,
            Lat: position?.Lat,
            Lon: position?.Lon,
            AccuracyM: hasFix ? member.AccuracyM : null,
            PoorAccuracy: poorAccuracy,
            Status: status,
            Ring: RingOf(status),
            Badge: BadgeOf(status),
            LowBattery: lowBattery,
            DrivingFresh: member.IsDriving && member.Freshness == Freshness.Fresh,
            Far: far,
            IsStatic: member.Kind == MemberKind.Static,
            IsMe: isMe,
            ZClass: ZClassOf(status),
            AvatarUrl: string.IsNullOrEmpty(member.AvatarUrl) ? null : member.AvatarUrl,
            Chip: chip,
            ChipMinute: minute,
            AriaLabel: PinName(member, status, place, poorAccuracy, lowBattery, far, fromMe?.Meters, now, zone, units),
            Tooltip: MapText.Title(member.DisplayName, member.LoreTitle),
            BubbleLabel: BubbleTextFormatter.Label(member.DisplayName, fromMe),
            BubbleTooltip: BubbleTextFormatter.Tooltip(member.DisplayName, fromMe));
    }

    // 01 section 4.3: the first matching row wins. "At a place" means inside a zone the map draws.
    private static MemberStatus StatusOf(MemberVm member, Dictionary<string, PlaceVm> drawn)
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
            _ when member.PlaceId is { } placeId && drawn.ContainsKey(placeId) => MemberStatus.AtPlace,
            _ => MemberStatus.Out,
        };
    }

    private static Ring RingOf(MemberStatus status) => status switch
    {
        MemberStatus.Static => new Ring(MapPalette.RingStatic, Dashed: true, RingWidthPx),
        MemberStatus.Offline or MemberStatus.Stale or MemberStatus.NoFix => new Ring(MapPalette.RingStale, Dashed: true, RingWidthPx),
        MemberStatus.Driving => new Ring(MapPalette.RingDriving, Dashed: false, RingWidthPx),
        MemberStatus.AtPlace => new Ring(MapPalette.RingAtPlace, Dashed: false, RingWidthPx),
        _ => new Ring(MapPalette.RingOut, Dashed: false, RingWidthPx),
    };

    private static PinBadge? BadgeOf(MemberStatus status) => status switch
    {
        MemberStatus.Static => PinBadge.Home,
        MemberStatus.Offline => PinBadge.Offline,
        MemberStatus.Stale => PinBadge.Stale,
        MemberStatus.Driving => PinBadge.Driving,
        _ => null,
    };

    // Draw order, low to high: stale, at a place, out, driving; the selected pin (4) is raised in JavaScript.
    private static int ZClassOf(MemberStatus status) => status switch
    {
        MemberStatus.AtPlace => 1,
        MemberStatus.Out => 2,
        MemberStatus.Driving => 3,
        _ => 0,
    };

    // A vehicle has a pin when it is not the placeholder and has a fix and a position.
    private static bool HasPin(VehicleVm vehicle) =>
        !vehicle.IsPlaceholder && vehicle.Freshness != Freshness.NoFix && vehicle.Lat is not null && vehicle.Lon is not null;

    private static VehiclePayloadItem Vehicle(VehicleVm vehicle, IReadOnlyList<PlaceVm> places, string? chip)
    {
        var hasPin = HasPin(vehicle);
        var stale = vehicle.Freshness == Freshness.Stale;
        var ring = vehicle.Freshness switch
        {
            Freshness.Fresh when vehicle.IsMoving => new Ring(MapPalette.RingDriving, Dashed: false, VehicleRingWidthPx),
            Freshness.Fresh => new Ring(MapPalette.RingParked, Dashed: false, VehicleRingWidthPx),
            _ => new Ring(MapPalette.RingStale, Dashed: true, VehicleRingWidthPx),
        };
        var place = vehicle.PlaceId is { } placeId ? places.FirstOrDefault(p => p.Id == placeId) : null;

        return new VehiclePayloadItem(
            Id: vehicle.Id,
            Name: vehicle.Name,
            Glyph: vehicle.Glyph == VehicleGlyph.Pickup ? MapGlyph.Pickup : MapGlyph.Car,
            Lat: hasPin ? vehicle.Lat : null,
            Lon: hasPin ? vehicle.Lon : null,
            Ring: ring,
            Stale: stale,
            Chip: chip,
            AriaLabel: MapText.VehiclePinName(vehicle, place),
            Tooltip: MapText.Title(vehicle.Name, vehicle.LoreTitle));
    }
}
