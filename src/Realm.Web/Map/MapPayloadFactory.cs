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

    /// <summary>The member payload. <paramref name="meId"/> goes through <see cref="ResolveMeId"/>; <paramref name="now"/> sets the chip and its minute.</summary>
    public static MembersPayload Members(
        IReadOnlyList<MemberVm> members,
        IReadOnlyList<PlaceVm> places,
        string? meId,
        DateTimeOffset now,
        MapPayloadOptions options,
        int version)
    {
        var me = ResolveMeId(members, meId);
        var drawn = DrawnPlaces(places, options);
        var origin = DefaultViewPlanner.Origin(members, places, me);
        var minute = now.ToUnixTimeSeconds() / 60;
        var items = new List<MemberPayloadItem>(members.Count);
        foreach (var member in members)
        {
            items.Add(Member(member, member.Id == me, drawn, origin, now, minute, options));
        }

        return new MembersPayload(version, me ?? string.Empty, items);
    }

    /// <summary>The vehicle payload (01 section 4.7): the ring follows <c>Freshness</c> first and <c>IsMoving</c> second; a vehicle without a position, and the placeholder, have no coordinates and so no pin.</summary>
    public static VehiclesPayload Vehicles(IReadOnlyList<VehicleVm> vehicles, IReadOnlyList<PlaceVm> places, int version)
    {
        var items = new List<VehiclePayloadItem>(vehicles.Count);
        foreach (var vehicle in vehicles)
        {
            items.Add(Vehicle(vehicle, places));
        }

        return new VehiclesPayload(version, items);
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

    private static MemberPayloadItem Member(
        MemberVm member,
        bool isMe,
        Dictionary<string, PlaceVm> drawn,
        (double Lat, double Lon)? origin,
        DateTimeOffset now,
        long minute,
        MapPayloadOptions options)
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

        // 01 section 4.4: nothing is selected in this slice, so the only chip is mine, at a place, with a known arrival.
        var chip = isMe && status == MemberStatus.AtPlace && member.SinceUtc is { } since ? TimeFormatter.HereForChip(now - since) : null;

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
            AriaLabel: MapText.MemberPinName(member, status, place, poorAccuracy, lowBattery),
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

    private static VehiclePayloadItem Vehicle(VehicleVm vehicle, IReadOnlyList<PlaceVm> places)
    {
        var hasPin = !vehicle.IsPlaceholder && vehicle.Freshness != Freshness.NoFix && vehicle.Lat is not null && vehicle.Lon is not null;
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
            Chip: null,
            AriaLabel: MapText.VehiclePinName(vehicle, place),
            Tooltip: MapText.Title(vehicle.Name, vehicle.LoreTitle));
    }
}
