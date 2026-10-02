using Realm.Domain;

namespace Realm.Demo;

/// <summary>
/// The Demo fixture (02 section 9): the snapshot at the frozen instant, built from hand-made raw fixes that go through
/// the same <see cref="Fuse"/>, <see cref="PlaceResolver"/> and <see cref="FreshnessRules"/> as the live service, and the
/// four weeks of driving data (<see cref="WeekReport"/>, <see cref="GetDriverWeek"/>), both under the active
/// <see cref="DemoVariants"/>. It makes no network call and reads no file. Fixture instants are fixed; only the clock it
/// is given moves (so a later ?now= ages the fixes and can turn a member Stale, but never changes the driving history).
/// </summary>
public sealed class DemoDataSource
{
    /// <summary>The frozen instant of the fixture: 2026-09-30T21:25:00-05:00, as a UTC instant.</summary>
    public static readonly DateTimeOffset Anchor = new DateTimeOffset(2026, 9, 30, 21, 25, 0, TimeSpan.FromHours(-5)).ToUniversalTime();

    /// <summary>The zone of the fixture; the clock's local time is always shown in it.</summary>
    public const string ZoneId = "America/Chicago";

    // The add-on option defaults the fixture runs under (02 section 3.1).
    private const int StaleAfterMinutes = 30;
    private const int OfflineAfterHours = 24;
    private const int VehicleStaleAfterMinutes = 45;

    // Each live member has a companion-app fix (the winner: it carries accuracy, battery and, for the queen, speed) and a
    // Life360 fix a little older (it carries the address, never an accuracy, 02 section 1.3).
    private const int Life360LagSeconds = 30;

    // The static pin of the prince (02 section 9.3); the fixture has no street address for it (D24).
    private const double PrinceLat = 38.8339;
    private const double PrinceLon = -104.8214;

    // The pickup: 7.78 m from the king, inside home, ignition off, 71% fuel, 18 432 mi on the clock, last update 21:05.
    private const double WagonLat = 31.09907;
    private const double WagonLon = -97.34100;
    private const int WagonFuelPct = 71;
    private const double WagonOdometerMiles = 18_432;
    private const double MetresPerMile = 1609.344;

    // The all-near variant (02 section 9.5): the cryptid 3.07 km and the prince 3.48 km from the king.
    private const double NearCryptidLat = 31.1250;
    private const double NearCryptidLon = -97.3300;
    private const double NearPrinceLat = 31.0800;
    private const double NearPrinceLon = -97.3700;

    // The poor-accuracy variant: the jester's accuracy in metres.
    private const double PoorAccuracyM = 800;

    private readonly DemoDrivingData _driving;
    private readonly DateTimeOffset _startedUtc;

    /// <param name="time">The session's clock; the snapshot is evaluated at its current instant.</param>
    /// <param name="variants">The active variants; null for the default fixture.</param>
    public DemoDataSource(TimeProvider time, DemoVariants? variants = null)
    {
        Time = time;
        Variants = variants ?? DemoVariants.None;
        Zone = TimeZoneInfo.FindSystemTimeZoneById(ZoneId);
        _driving = new DemoDrivingData(Variants, Zone);
        _startedUtc = time.GetUtcNow();
    }

    /// <summary>The active variants.</summary>
    public DemoVariants Variants { get; }

    /// <summary>The session's clock.</summary>
    public TimeProvider Time { get; }

    /// <summary>America/Chicago.</summary>
    public TimeZoneInfo Zone { get; }

    /// <summary>The member whose demo person user id is <paramref name="haUserId"/>; null for any other id or none.</summary>
    public string? ResolveMe(string? haUserId)
    {
        return haUserId is null
            ? null
            : DemoCast.Members.FirstOrDefault(member => member.PersonUserId == haUserId)?.Id;
    }

    /// <summary>
    /// The driving report of week <paramref name="weekOffset"/> (0 to 3; anything else is a caller error). A frozen history:
    /// the figures are those of the default instant whatever the clock says; <paramref name="weekStart"/> only moves the
    /// bounds (there is no Sunday fixture, 02 section 9.0).
    /// </summary>
    public WeekReportVm WeekReport(int weekOffset, DayOfWeek weekStart) => _driving.Report(weekOffset, weekStart);

    /// <summary>One driver's week: the summary of the report and the generated drives, newest first; null for the prince, an unknown id or any member that is not a report driver.</summary>
    public DriverWeek? GetDriverWeek(string memberId, int weekOffset) => _driving.GetDriverWeek(memberId, weekOffset);

    /// <summary>
    /// The snapshot at the clock's current instant, with the snapshot variants applied. <paramref name="homeAssistantRestored"/>
    /// is the test hook of the ha-down variant: Home Assistant is connected again.
    /// </summary>
    public RealmSnapshot Snapshot(bool homeAssistantRestored = false)
    {
        var now = Time.GetUtcNow();
        var zones = DemoPlaces.Drawn.Select(place => place.ToRaw()).ToArray();
        var staleAfter = FreshnessRules.StaleAfter(StaleAfterMinutes, FreshnessRules.Heartbeat([], StaleAfterMinutes));

        MemberVm Live(DemoMember cast, RawFix companion, DateTimeOffset since, bool driving)
        {
            var life360 = new RawFix(
                EntityId: $"device_tracker.life360_{cast.Name.ToLowerInvariant()}",
                Source: FixSource.Life360,
                Ts: companion.Ts.AddSeconds(-Life360LagSeconds),
                Lat: companion.Lat,
                Lon: companion.Lon,
                Driving: driving ? true : null,
                Address: cast.Address);
            var fused = Fuse.Position([companion, life360], now, OfflineAfterHours)
                ?? throw new InvalidOperationException($"No fix for {cast.Id}.");
            var address = AddressParser.Parse(fused.Address);
            var freshness = FreshnessRules.ForMember(MemberKind.Live, now, fused.Ts, staleAfter, OfflineAfterHours);

            return new MemberVm(
                Id: cast.Id,
                DisplayName: cast.Name,
                LoreTitle: cast.Lore,
                AvatarUrl: null,
                Color: cast.Color,
                Kind: MemberKind.Live,
                Lat: fused.Lat,
                Lon: fused.Lon,
                AccuracyM: fused.AccuracyM,
                BatteryPct: fused.BatteryPct,
                Charging: fused.Charging,
                BatteryAsOfUtc: fused.BatteryAsOfUtc,
                // Stands in for the trip state machine (02 section 5.4): the fixture says who is driving, and a member whose fix has aged out is not.
                IsDriving: driving && freshness == Freshness.Fresh,
                SpeedMps: fused.SpeedMps,
                Street: address?.Street,
                City: address?.City,
                Region: address?.Region,
                FullAddress: address?.FullAddress,
                PlaceId: PlaceResolver.Resolve(fused.Lat, fused.Lon, fused.AccuracyM, zones, []).PlaceId,
                SinceUtc: since,
                LastUpdateUtc: fused.Ts,
                SortOrder: cast.SortOrder,
                Freshness: freshness);
        }

        MemberVm Static(DemoMember cast, double lat, double lon) => new(
            Id: cast.Id,
            DisplayName: cast.Name,
            LoreTitle: cast.Lore,
            AvatarUrl: null,
            Color: cast.Color,
            Kind: MemberKind.Static,
            Lat: lat,
            Lon: lon,
            AccuracyM: null,
            BatteryPct: null,
            Charging: null,
            BatteryAsOfUtc: null,
            IsDriving: false,
            SpeedMps: null,
            Street: null,
            City: null,
            Region: null,
            FullAddress: null,
            PlaceId: PlaceResolver.Resolve(lat, lon, null, zones, []).PlaceId,
            SinceUtc: null,
            LastUpdateUtc: null,
            SortOrder: cast.SortOrder,
            Freshness: FreshnessRules.ForMember(MemberKind.Static, now, null, staleAfter, OfflineAfterHours),
            StaticLabel: cast.StaticLabel);

        // 02 section 9.3: positions, accuracies, batteries and fix ages are Appendix A.1 verbatim; "since" is the local time of the table.
        MemberVm[] members =
        [
            Live(DemoCast.King, Companion(DemoCast.King, ageSeconds: 0, 31.0990, -97.3410, accuracyM: 18, battery: 19, charging: true), Local(17, 52), driving: false),
            Live(DemoCast.Queen, Companion(DemoCast.Queen, ageSeconds: 60, 31.0560, -97.4647, accuracyM: 12, battery: 62, charging: false, speedMps: 24.1), Local(21, 12), driving: true),
            Live(DemoCast.Jester, Companion(DemoCast.Jester, ageSeconds: 3 * 60, 31.1040, -97.3560, accuracyM: 22, battery: 12, charging: false), Local(21, 6), driving: false),
            Live(DemoCast.Cryptid, Companion(DemoCast.Cryptid, ageSeconds: 42 * 60, 31.3382, -94.7291, accuracyM: 35, battery: 10, charging: false), Local(20, 10), driving: false),
            Static(DemoCast.Prince, PrinceLat, PrinceLon),
        ];
        members = [.. members.Select(ApplyMemberVariants)];

        var wagonUpdate = Local(21, 5);
        var wagonState = new RawVehicleState(
            LastUpdateUtc: wagonUpdate,
            OdometerM: WagonOdometerMiles * MetresPerMile,
            FuelPct: WagonFuelPct,
            Ignition: IgnitionState.Off,
            RemoteStartSecondsLeft: null,
            SpeedMps: 0);

        VehicleVm[] vehicles =
        [
            new VehicleVm(
                Id: DemoCast.Wagon.Id,
                Name: DemoCast.Wagon.Name,
                LoreTitle: DemoCast.Wagon.Lore,
                Glyph: DemoCast.Wagon.Glyph,
                Lat: WagonLat,
                Lon: WagonLon,
                Street: null,
                PlaceId: PlaceResolver.Resolve(WagonLat, WagonLon, null, zones, []).PlaceId,
                Ignition: wagonState.Ignition,
                RemoteStartSecondsLeft: wagonState.RemoteStartSecondsLeft,
                FuelPct: wagonState.FuelPct,
                OdometerM: wagonState.OdometerM,
                LastUpdateUtc: wagonState.LastUpdateUtc,
                SpeedMps: wagonState.SpeedMps,
                IsMoving: VehicleRules.IsMoving(wagonState.Ignition, wagonState.SpeedMps, wagonState.LastUpdateUtc, now),
                Freshness: FreshnessRules.ForVehicle(now, wagonState.LastUpdateUtc, VehicleStaleAfterMinutes),
                IsPlaceholder: DemoCast.Wagon.IsPlaceholder,
                PlaceholderNote: DemoCast.Wagon.PlaceholderNote),
            new VehicleVm(
                Id: DemoCast.Chariot.Id,
                Name: DemoCast.Chariot.Name,
                LoreTitle: DemoCast.Chariot.Lore,
                Glyph: DemoCast.Chariot.Glyph,
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
                Freshness: FreshnessRules.ForVehicle(now, null, VehicleStaleAfterMinutes),
                IsPlaceholder: DemoCast.Chariot.IsPlaceholder,
                PlaceholderNote: DemoCast.Chariot.PlaceholderNote),
        ];

        // A member or vehicle is listed under its own place only, stale members included (02 section 4.5).
        PlaceVm[] places = DemoPlaces.Drawn
            .Select(place => new PlaceVm(
                Id: place.Id,
                DisplayName: place.Name,
                Subtitle: place.Subtitle,
                Kind: place.Kind,
                Lat: place.Lat,
                Lon: place.Lon,
                RadiusM: place.RadiusM,
                MemberIdsInside: members.Where(member => member.PlaceId == place.Id).Select(member => member.Id).ToArray(),
                VehicleIdsInside: vehicles.Where(vehicle => vehicle.PlaceId == place.Id).Select(vehicle => vehicle.Id).ToArray()))
            .ToArray();

        ConnectionVm[] connections =
        [
            new ConnectionVm(ConnectionNames.HomeAssistant, ConnectionState.Connected, now),
            new ConnectionVm(ConnectionNames.Life360Trackers, ConnectionState.Connected, now),
            new ConnectionVm(ConnectionNames.FordPass, ConnectionState.Connected, now),
            new ConnectionVm(ConnectionNames.VehiclePlaceholder, ConnectionState.NotConnected, null),
        ];
        connections = [.. connections.Select(connection => ApplyConnectionVariants(connection, homeAssistantRestored))];

        return new RealmSnapshot(
            ServerNowUtc: now,
            StatsVersion: 0,
            WeekStart: DayOfWeek.Monday,
            Members: members,
            Vehicles: vehicles,
            Places: places,
            Connections: connections,
            Zone: ZoneId,
            UnitSystem: UnitSystem.Imperial);
    }

    // The member variants of 02 section 9.5, each a pure transform of one member. no-fix comes after all-near, so a member
    // that never reported stays without a position.
    private MemberVm ApplyMemberVariants(MemberVm member)
    {
        if (Variants.AllNear && member.Id == DemoCast.Cryptid.Id)
        {
            member = member with { Lat = NearCryptidLat, Lon = NearCryptidLon };
        }

        if (Variants.AllNear && member.Id == DemoCast.Prince.Id)
        {
            member = member with { Lat = NearPrinceLat, Lon = NearPrinceLon };
        }

        if (Variants.PoorAccuracy && member.Id == DemoCast.Jester.Id)
        {
            member = member with { AccuracyM = PoorAccuracyM };
        }

        if (Variants.NoFix && member.Id == DemoCast.Cryptid.Id)
        {
            member = member with { Lat = null, Lon = null, LastUpdateUtc = null, Freshness = Freshness.NoFix };
        }

        // The Life360 trackers are gone: the address, the driving flag and the reported speed go with them; the HA-side fields stay.
        if (Variants.Life360Down && member.Kind == MemberKind.Live)
        {
            member = member with { Street = null, City = null, Region = null, FullAddress = null, IsDriving = false, SpeedMps = null };
        }

        return member;
    }

    // The connection variants: a source that is down reports Unavailable and its last sync is the start of the session.
    private ConnectionVm ApplyConnectionVariants(ConnectionVm connection, bool homeAssistantRestored)
    {
        var down = connection.Name switch
        {
            ConnectionNames.HomeAssistant => Variants.HaDown && !homeAssistantRestored,
            ConnectionNames.Life360Trackers => Variants.Life360Down,
            _ => false,
        };
        return down ? connection with { State = ConnectionState.Unavailable, LastSyncUtc = _startedUtc } : connection;
    }

    // A fixture clock time: 2026-09-30 at the given local hour and minute (CDT, UTC-5), as a UTC instant.
    private static DateTimeOffset Local(int hour, int minute) =>
        new DateTimeOffset(2026, 9, 30, hour, minute, 0, TimeSpan.FromHours(-5)).ToUniversalTime();

    // The companion-app fix of a member, ageSeconds before the anchor.
    private static RawFix Companion(
        DemoMember cast,
        int ageSeconds,
        double lat,
        double lon,
        double accuracyM,
        int battery,
        bool charging,
        double? speedMps = null) => new(
            EntityId: $"device_tracker.{cast.Name.ToLowerInvariant()}_phone",
            Source: FixSource.Companion,
            Ts: Anchor.AddSeconds(-ageSeconds),
            Lat: lat,
            Lon: lon,
            AccuracyM: accuracyM,
            SpeedMps: speedMps,
            BatteryPct: battery,
            Charging: charging);
}
