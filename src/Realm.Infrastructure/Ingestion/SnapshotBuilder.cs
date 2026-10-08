using Realm.Domain;
using Realm.Infrastructure.Ha;
using Realm.Infrastructure.Options;

namespace Realm.Infrastructure.Ingestion;

/// <summary>A zone that is drawn, with the name and kind it has after the options overrides (02 section 1.9).</summary>
internal sealed record PlaceDef(RawPlace Zone, string DisplayName, string Subtitle, PlaceKind Kind);

/// <summary>Everything one snapshot is built from. The members and vehicles are the pipeline's live state; the builder only reads it.</summary>
internal sealed class BuildInput
{
    public required RealmOptions Options { get; init; }

    public required DateTimeOffset Now { get; init; }

    public required string ZoneId { get; init; }

    public required IReadOnlyList<MemberRuntime> Members { get; init; }

    public required IReadOnlyList<VehicleRuntime> Vehicles { get; init; }

    public required IReadOnlyList<PlaceDef> Places { get; init; }

    public required HaConnectionStatus Connection { get; init; }

    public required IReadOnlyDictionary<string, HaEntitySnapshot> Entities { get; init; }

    public required int StatsVersion { get; init; }
}

/// <summary>
/// Builds the immutable <see cref="RealmSnapshot"/> from the pipeline's state (02 sections 1.5, 1.8, 1.9 and 4). Pure: the clock is <see cref="BuildInput.Now"/>,
/// nothing is written, nothing is logged.
/// </summary>
internal static class SnapshotBuilder
{
    private const double VehicleStreetMaxDistanceM = 75;
    private const string AvatarRoute = "avatars/";

    private static readonly TimeSpan VehicleStreetMaxAge = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan Life360UnavailableAfter = TimeSpan.FromMinutes(5);

    /// <summary>The fused position of a live member (02 section 4), or null for a static member and a member without a fix.</summary>
    public static FusedPosition? FuseOf(MemberRuntime member, RealmOptions options, DateTimeOffset now)
    {
        if (member.Plan.Kind == MemberKind.Static)
        {
            return null;
        }

        var fixes = new List<RawFix>(2);
        if (member.Life360 is { } life360)
        {
            fixes.Add(life360);
        }

        if (member.Companion is { } companion)
        {
            fixes.Add(WithSensorBattery(companion, member));
        }

        return Fuse.Position(fixes, now, options.UiOfflineAfterHours);
    }

    /// <summary>The snapshot before anything is known: nobody, nothing, UTC, and the connections as the websocket's first status reads.</summary>
    public static RealmSnapshot Initial(RealmOptions options, DateTimeOffset now, HaConnectionStatus connection) => Build(new BuildInput
    {
        Options = options,
        Now = now,
        ZoneId = "UTC",
        Members = [],
        Vehicles = [],
        Places = [],
        Connection = connection,
        Entities = new Dictionary<string, HaEntitySnapshot>(),
        StatsVersion = 0,
    });

    public static RealmSnapshot Build(BuildInput input)
    {
        var fused = input.Members.ToDictionary(m => m.Plan.Id, m => FuseOf(m, input.Options, input.Now), StringComparer.Ordinal);
        var members = input.Members
            .Select(m => MemberOf(m, fused[m.Plan.Id], input))
            .OrderBy(m => m.SortOrder)
            .ThenBy(m => m.Id, StringComparer.Ordinal)
            .ToList();
        var vehicleStates = input.Vehicles.ToDictionary(v => v.Plan.Id, v => StateOf(v, input.Entities), StringComparer.Ordinal);
        var vehicles = input.Vehicles
            .OrderBy(v => v.Plan.SortOrder)
            .ThenBy(v => v.Plan.Id, StringComparer.Ordinal)
            .Select(v => VehicleOf(v, vehicleStates[v.Plan.Id], input, fused))
            .ToList();

        return new RealmSnapshot(
            ServerNowUtc: input.Now,
            StatsVersion: input.StatsVersion,
            WeekStart: input.Options.DrivingWeekStart,
            Members: members,
            Vehicles: vehicles,
            Places: PlacesOf(input),
            Connections:
            [
                input.Connection.ToConnectionVm(input.Now),
                Life360Connection(input),
                FordPassConnection(input, vehicleStates),
                new ConnectionVm(ConnectionNames.VehiclePlaceholder, ConnectionState.NotConnected, null),
            ],
            Zone: input.ZoneId,
            UnitSystem: UnitSystem.Imperial,
            RetentionFixDays: input.Options.RetentionFixDays);
    }

    // ---- members ------------------------------------------------------------------------------------------------

    // 02 section 4.3: an Android battery sensor has its own timestamp and wins when it is newer than the battery reading the fix carries.
    private static RawFix WithSensorBattery(RawFix fix, MemberRuntime member)
    {
        if (member.SensorBatteryPct is { } percent
            && member.SensorBatteryAsOfUtc is { } asOf
            && (fix.BatteryPct is null || asOf > (fix.BatteryAsOfUtc ?? fix.Ts)))
        {
            return fix with { BatteryPct = percent, Charging = member.SensorCharging, BatteryAsOfUtc = asOf };
        }

        return fix;
    }

    private static MemberVm MemberOf(MemberRuntime runtime, FusedPosition? fused, BuildInput input)
    {
        var plan = runtime.Plan;
        var avatar = plan.AvatarUpstream is null ? null : AvatarRoute + plan.Id;
        if (plan.Kind == MemberKind.Static)
        {
            var hasPin = plan.StaticLat is not null && plan.StaticLon is not null;
            return new MemberVm(
                Id: plan.Id,
                DisplayName: plan.DisplayName,
                LoreTitle: plan.LoreTitle,
                AvatarUrl: avatar,
                Color: plan.Color,
                Kind: MemberKind.Static,
                Lat: plan.StaticLat,
                Lon: plan.StaticLon,
                AccuracyM: null,
                BatteryPct: null,
                Charging: null,
                BatteryAsOfUtc: null,
                IsDriving: false,
                SpeedMps: null,
                Street: null,
                City: null,
                Region: null,
                FullAddress: plan.StaticShowAddress ? plan.StaticAddress : null,
                PlaceId: runtime.PlaceId,
                SinceUtc: null,
                LastUpdateUtc: null,
                SortOrder: plan.SortOrder,
                Freshness: hasPin ? Freshness.Static : Freshness.NoFix,
                StaticLabel: plan.StaticLabel);
        }

        var staleAfter = FreshnessRules.StaleAfter(input.Options.UiStaleAfterMinutes, runtime.Heartbeat, input.Options.FusionStaleGraceMinutes);
        var freshness = FreshnessRules.ForMember(MemberKind.Live, input.Now, fused?.Ts, staleAfter, input.Options.UiOfflineAfterHours);
        var address = fused is null ? null : AddressParser.Parse(fused.Address);
        return new MemberVm(
            Id: plan.Id,
            DisplayName: plan.DisplayName,
            LoreTitle: plan.LoreTitle,
            AvatarUrl: avatar,
            Color: plan.Color,
            Kind: MemberKind.Live,
            Lat: fused?.Lat,
            Lon: fused?.Lon,
            AccuracyM: fused?.AccuracyM,
            BatteryPct: fused?.BatteryPct,
            Charging: fused?.Charging,
            BatteryAsOfUtc: fused?.BatteryAsOfUtc,
            IsDriving: fused is not null && runtime.Detector?.IsDriving(input.Now) == true,
            SpeedMps: fused?.SpeedMps,
            Street: address?.Street,
            City: address?.City,
            Region: address?.Region,
            FullAddress: address?.FullAddress,
            PlaceId: fused is null ? null : runtime.PlaceId,
            SinceUtc: fused is null ? null : runtime.RunStartUtc,
            LastUpdateUtc: fused?.Ts,
            SortOrder: plan.SortOrder,
            Freshness: freshness);
    }

    // ---- vehicles -----------------------------------------------------------------------------------------------

    internal static RawVehicleState StateOf(VehicleRuntime vehicle, IReadOnlyDictionary<string, HaEntitySnapshot> entities)
    {
        var sensors = vehicle.Plan.SensorIds.Select(id => entities.GetValueOrDefault(id)).OfType<HaEntitySnapshot>().ToList();
        return vehicle.Plan.Prefix is { } prefix && sensors.Count > 0
            ? FixParser.ParseVehicleState(prefix, sensors)
            : new RawVehicleState(null, null, null, null, null, null);
    }

    private static VehicleVm VehicleOf(
        VehicleRuntime runtime,
        RawVehicleState state,
        BuildInput input,
        IReadOnlyDictionary<string, FusedPosition?> fused)
    {
        var plan = runtime.Plan;
        if (plan.IsPlaceholder)
        {
            return new VehicleVm(
                Id: plan.Id,
                Name: plan.Name,
                LoreTitle: plan.LoreTitle,
                Glyph: plan.Glyph,
                Lat: null,
                Lon: null,
                Street: null,
                PlaceId: null,
                Ignition: null,
                RemoteStartSecondsLeft: null,
                FuelPct: null,
                OdometerM: null,
                LastUpdateUtc: null,
                SpeedMps: null,
                IsMoving: false,
                Freshness: Freshness.NoFix,
                IsPlaceholder: true,
                PlaceholderNote: plan.PlaceholderNote);
        }

        var lastUpdate = state.LastUpdateUtc;
        var speed = VehicleRules.FreshSpeedMps(state.SpeedMps, lastUpdate, input.Now);
        return new VehicleVm(
            Id: plan.Id,
            Name: plan.Name,
            LoreTitle: plan.LoreTitle,
            Glyph: plan.Glyph,
            Lat: runtime.Fix?.Lat,
            Lon: runtime.Fix?.Lon,
            Street: BorrowedStreet(runtime, input, fused),
            PlaceId: runtime.Fix is null ? null : runtime.PlaceId,
            Ignition: state.Ignition,
            RemoteStartSecondsLeft: state.RemoteStartSecondsLeft,
            FuelPct: state.FuelPct,
            OdometerM: state.OdometerM,
            LastUpdateUtc: lastUpdate,
            SpeedMps: speed,
            IsMoving: VehicleRules.IsMoving(state.Ignition, state.SpeedMps, lastUpdate, input.Now),
            Freshness: FreshnessRules.ForVehicle(input.Now, lastUpdate, input.Options.UiVehicleStaleAfterMinutes),
            IsPlaceholder: false,
            PlaceholderNote: null);
    }

    // 02 section 1.9: the street of a member whose fused fix is within 75 m of the vehicle and within 10 minutes of the vehicle's fix; else none (no geocoder).
    private static string? BorrowedStreet(VehicleRuntime vehicle, BuildInput input, IReadOnlyDictionary<string, FusedPosition?> fused)
    {
        if (vehicle.Fix is not { } fix)
        {
            return null;
        }

        foreach (var member in input.Members)
        {
            if (fused.GetValueOrDefault(member.Plan.Id) is { } position
                && (position.Ts - fix.Ts).Duration() <= VehicleStreetMaxAge
                && Geo.DistanceM(position.Lat, position.Lon, fix.Lat, fix.Lon) <= VehicleStreetMaxDistanceM
                && AddressParser.Parse(position.Address)?.Street is { } street)
            {
                return street;
            }
        }

        return null;
    }

    // ---- places -------------------------------------------------------------------------------------------------

    private static List<PlaceVm> PlacesOf(BuildInput input)
    {
        return input.Places.Select(place => new PlaceVm(
                Id: place.Zone.Id,
                DisplayName: place.DisplayName,
                Subtitle: place.Subtitle,
                Kind: place.Kind,
                Lat: place.Zone.Lat,
                Lon: place.Zone.Lon,
                RadiusM: place.Zone.RadiusM,
                MemberIdsInside: input.Members.Where(m => m.PlaceId == place.Zone.Id).Select(m => m.Plan.Id).Order(StringComparer.Ordinal).ToArray(),
                VehicleIdsInside: input.Vehicles.Where(v => v.PlaceId == place.Zone.Id).Select(v => v.Plan.Id).Order(StringComparer.Ordinal).ToArray()))
            .ToList();
    }

    // ---- connections (02 section 1.8) ---------------------------------------------------------------------------

    private static ConnectionVm Life360Connection(BuildInput input)
    {
        var tracked = input.Members.Where(m => m.Plan.Life360TrackerId is not null).ToList();
        if (tracked.Count == 0)
        {
            return new ConnectionVm(ConnectionNames.Life360Trackers, ConnectionState.NotConnected, null);
        }

        DateTimeOffset? lastSync = tracked.Select(m => m.Life360?.Ts).Max();
        var seen = tracked.Where(m => m.Life360Seen).ToList();
        if (seen.Count == 0)
        {
            return new ConnectionVm(ConnectionNames.Life360Trackers, ConnectionState.Reconnecting, lastSync);
        }

        if (seen.Any(m => m.Life360UnavailableSinceUtc is null))
        {
            return new ConnectionVm(ConnectionNames.Life360Trackers, ConnectionState.Connected, lastSync);
        }

        // Every tracker reports unavailable or unknown: Unavailable once the last of them has been for 5 minutes (D29), Reconnecting before that.
        var since = seen.Max(m => m.Life360UnavailableSinceUtc!.Value);
        var state = input.Now - since >= Life360UnavailableAfter ? ConnectionState.Unavailable : ConnectionState.Reconnecting;
        return new ConnectionVm(ConnectionNames.Life360Trackers, state, lastSync);
    }

    private static ConnectionVm FordPassConnection(BuildInput input, Dictionary<string, RawVehicleState> states)
    {
        var fords = input.Vehicles.Where(v => !v.Plan.IsPlaceholder).ToList();
        if (fords.Count == 0)
        {
            return new ConnectionVm(ConnectionNames.FordPass, ConnectionState.NotConnected, null);
        }

        DateTimeOffset? lastSync = fords.Select(v => states[v.Plan.Id].LastUpdateUtc).Max();
        var limit = TimeSpan.FromMinutes(input.Options.UiVehicleStaleAfterMinutes);
        if (fords.Any(v => states[v.Plan.Id].LastUpdateUtc is { } at && input.Now - at <= limit))
        {
            return new ConnectionVm(ConnectionNames.FordPass, ConnectionState.Connected, lastSync);
        }

        // Nothing fresh: Unavailable when Home Assistant has told us about the vehicle, Reconnecting while nothing has been heard yet.
        var known = fords.Any(v => v.Plan.SensorIds.Concat(v.Plan.TrackerId is null ? [] : [v.Plan.TrackerId]).Any(input.Entities.ContainsKey));
        return new ConnectionVm(ConnectionNames.FordPass, known ? ConnectionState.Unavailable : ConnectionState.Reconnecting, lastSync);
    }
}
