using Realm.Domain;
using Realm.Infrastructure.Ha;
using Realm.Infrastructure.Options;

namespace Realm.Infrastructure.Ingestion;

/// <summary>A zone that is drawn, with the name and kind it has (02 section 1.9).</summary>
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
        var vehicles = input.Vehicles
            .OrderBy(v => v.Plan.SortOrder)
            .ThenBy(v => v.Plan.Id, StringComparer.Ordinal)
            .Select(v => VehicleOf(v, input, fused))
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
            ],
            Zone: input.ZoneId,
            UnitSystem: UnitSystem.Imperial,
            RetentionFixDays: input.Options.RetentionFixDays,
            Thresholds: new DrivingThresholds(
                Math.Round(input.Options.DrivingSpeedingMps / FixParser.MphToMps, 1),
                input.Options.DrivingSpeedingMinSeconds,
                input.Options.DrivingPhoneMinSeconds));
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
        var face = AvatarFace.Resolve(plan.Icon, asTracker: false, hasPhoto: plan.AvatarUpstream is not null);
        var avatar = face.Mode == FaceMode.Photo ? AvatarRoute + plan.Id : null;
        VehicleGlyph? glyph = face.Mode == FaceMode.Glyph ? face.Glyph : null;
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
                StaticLabel: plan.StaticLabel,
                Glyph: glyph);
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
            Freshness: freshness,
            Glyph: glyph);
    }

    // ---- vehicles -----------------------------------------------------------------------------------------------

    private static VehicleVm VehicleOf(VehicleRuntime runtime, BuildInput input, IReadOnlyDictionary<string, FusedPosition?> fused)
    {
        var plan = runtime.Plan;
        var fix = runtime.Fix;
        var lastUpdate = fix?.Ts;
        var face = AvatarFace.Resolve(plan.Icon, asTracker: true, hasPhoto: plan.AvatarUpstream is not null);
        return new VehicleVm(
            Id: plan.Id,
            Name: plan.Name,
            LoreTitle: plan.LoreTitle,
            Glyph: RosterIcons.GlyphOf(plan.Icon) ?? plan.Glyph,
            Lat: fix?.Lat,
            Lon: fix?.Lon,
            Street: BorrowedStreet(runtime, input, fused),
            PlaceId: fix is null ? null : runtime.PlaceId,
            LastUpdateUtc: lastUpdate,
            SpeedMps: VehicleRules.FreshSpeedMps(fix?.SpeedMps, lastUpdate, input.Now),
            IsMoving: VehicleRules.IsMoving(fix?.SpeedMps, lastUpdate, input.Now),
            Freshness: FreshnessRules.ForVehicle(input.Now, lastUpdate, input.Options.UiVehicleStaleAfterMinutes),
            Color: string.IsNullOrEmpty(plan.Color) ? "#E8BC4E" : plan.Color,
            AvatarUrl: face.Mode == FaceMode.Photo ? AvatarRoute + plan.Id : null,
            ShowInitial: face.Mode == FaceMode.Initial,
            KeepHistory: plan.KeepHistory);
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
}
