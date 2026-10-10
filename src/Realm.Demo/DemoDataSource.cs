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

    /// <summary>The Demo keeps its (synthetic) history for the longest retention the add-on allows, so every period of the split button is offered.</summary>
    public const int DemoRetentionFixDays = 400;

    // The add-on option defaults the fixture runs under (02 section 3.1).
    private const int StaleAfterMinutes = 30;
    private const int OfflineAfterHours = 24;
    private const int VehicleStaleAfterMinutes = 45;

    // Each live member has a companion-app fix (the winner: it carries accuracy, battery and, for the queen, speed) and a
    // Life360 fix a little older (it carries the address, never an accuracy, 02 section 1.3).
    private const int Life360LagSeconds = 30;

    // The static pin of the prince (02 section 9.3); the fixture has no street address for it (D24).
    private const double PrinceLat = 38.8339;
    private const double PrinceLon = -92.8214;

    // The pickup: 7.78 m from the king, inside home, standing still, last update 21:05. The hatchback is parked near it, last seen 20 days ago.
    private const double WagonLat = 31.09907;
    private const double WagonLon = -85.34100;

    // The all-near variant (02 section 9.5): the cryptid 3.07 km and the prince 3.48 km from the king.
    private const double NearCryptidLat = 31.1250;
    private const double NearCryptidLon = -85.3300;
    private const double NearPrinceLat = 31.0800;
    private const double NearPrinceLon = -85.3700;

    // The poor-accuracy variant: the jester's accuracy in metres.
    private const double PoorAccuracyM = 800;

    private readonly DemoDrivingData _driving;
    private readonly DateTimeOffset _startedUtc;
    private readonly object _placesGate = new();
    private IReadOnlyList<DemoPlace> _added = [];

    /// <param name="time">The session's clock; the snapshot is evaluated at its current instant.</param>
    /// <param name="variants">The active variants; null for the default fixture.</param>
    /// <param name="roster">The roster to start from; null for the default one (<see cref="DemoRoster"/>).</param>
    public DemoDataSource(TimeProvider time, DemoVariants? variants = null, DemoRoster? roster = null)
    {
        Time = time;
        Variants = variants ?? DemoVariants.None;
        Zone = TimeZoneInfo.FindSystemTimeZoneById(ZoneId);
        _driving = new DemoDrivingData(Variants, Zone);
        _startedUtc = time.GetUtcNow();
        Roster = roster ?? new DemoRoster();
    }

    /// <summary>The roster of this session: in memory, editable from Settings.</summary>
    public DemoRoster Roster { get; }

    /// <summary>
    /// Adds a place the way the Live app would, but only in memory (0.2.2, D119): nothing is written to Home Assistant, and the place is gone with the session. It is
    /// listed after the fixture's places and drawn on the map; <see cref="PlacesChanged"/> follows.
    /// </summary>
    /// <returns>The place that was added.</returns>
    public DemoPlace AddPlace(NewZone zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        DemoPlace added;
        lock (_placesGate)
        {
            var name = zone.Name.Trim();
            added = new DemoPlace($"added_{_added.Count + 1}", name, name, string.Empty, PlaceKindIcons.KindOf(zone.Icon), zone.Latitude, zone.Longitude, NewZone.ClampRadius(zone.RadiusM));
            _added = [.. _added, added];
        }

        PlacesChanged?.Invoke();
        return added;
    }

    /// <summary>Raised after <see cref="AddPlace"/> added a place.</summary>
    public event Action? PlacesChanged;

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

    /// <summary>The report of any period (a month, a rolling window, a custom range) over the fixture's drives.</summary>
    public WeekReportVm PeriodReport(ReportWindow window) => _driving.PeriodReport(window);

    /// <summary>One driver's period over the fixture's drives; null for an unknown or non-report member.</summary>
    public DriverWeek? GetDriverPeriod(string memberId, ReportWindow window) => _driving.DriverPeriod(memberId, window);

    /// <summary>
    /// The snapshot at the clock's current instant, with the snapshot variants applied. <paramref name="homeAssistantRestored"/>
    /// is the test hook of the ha-down variant: Home Assistant is connected again.
    /// </summary>
    public RealmSnapshot Snapshot(bool homeAssistantRestored = false)
    {
        var now = Time.GetUtcNow();
        var zones = DemoPlaces.Drawn.Select(place => place.ToRaw()).ToArray();
        var staleAfter = FreshnessRules.StaleAfter(StaleAfterMinutes, FreshnessRules.Heartbeat([], StaleAfterMinutes));

        MemberVm Live(RosterEntry entry, Spot spot)
        {
            var companion = spot.Fix!;
            var life360 = new RawFix(
                EntityId: $"device_tracker.life360_{spot.Key}",
                Source: FixSource.Life360,
                Ts: companion.Ts.AddSeconds(-Life360LagSeconds),
                Lat: companion.Lat,
                Lon: companion.Lon,
                Driving: spot.Driving ? true : null,
                Address: spot.Address);
            var fused = Fuse.Position([companion, life360], now, OfflineAfterHours)
                ?? throw new InvalidOperationException($"No fix for {spot.Id}.");
            var address = AddressParser.Parse(fused.Address);
            var freshness = FreshnessRules.ForMember(MemberKind.Live, now, fused.Ts, staleAfter, OfflineAfterHours);

            return new MemberVm(
                Id: spot.Id,
                DisplayName: entry.DisplayName,
                LoreTitle: entry.LoreTitle,
                AvatarUrl: null,
                Color: entry.Color,
                Kind: MemberKind.Live,
                Lat: fused.Lat,
                Lon: fused.Lon,
                AccuracyM: fused.AccuracyM,
                BatteryPct: fused.BatteryPct,
                Charging: fused.Charging,
                BatteryAsOfUtc: fused.BatteryAsOfUtc,
                // Stands in for the trip state machine (02 section 5.4): the fixture says who is driving, and a member whose fix has aged out is not.
                IsDriving: spot.Driving && freshness == Freshness.Fresh,
                SpeedMps: fused.SpeedMps,
                Street: address?.Street,
                City: address?.City,
                Region: address?.Region,
                FullAddress: address?.FullAddress,
                PlaceId: PlaceResolver.Resolve(fused.Lat, fused.Lon, fused.AccuracyM, zones, []).PlaceId,
                SinceUtc: spot.Since,
                LastUpdateUtc: fused.Ts,
                SortOrder: entry.SortOrder,
                Freshness: freshness,
                Glyph: GlyphOf(entry));
        }

        MemberVm Static(RosterEntry entry, Spot spot) => new(
            Id: spot.Id,
            DisplayName: entry.DisplayName,
            LoreTitle: entry.LoreTitle,
            AvatarUrl: null,
            Color: entry.Color,
            Kind: MemberKind.Static,
            Lat: spot.Lat,
            Lon: spot.Lon,
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
            PlaceId: PlaceResolver.Resolve(spot.Lat, spot.Lon, null, zones, []).PlaceId,
            SinceUtc: null,
            LastUpdateUtc: null,
            SortOrder: entry.SortOrder,
            Freshness: FreshnessRules.ForMember(MemberKind.Static, now, null, staleAfter, OfflineAfterHours),
            StaticLabel: spot.StaticLabel,
            Glyph: GlyphOf(entry));

        VehicleVm Vehicle(RosterEntry entry, Spot spot) => new(
            Id: spot.Id,
            Name: entry.DisplayName,
            LoreTitle: entry.LoreTitle,
            Glyph: RosterIcons.GlyphOf(entry.Icon) ?? spot.Glyph,
            Lat: double.IsNaN(spot.Lat) ? null : spot.Lat,
            Lon: double.IsNaN(spot.Lon) ? null : spot.Lon,
            Street: null,
            PlaceId: double.IsNaN(spot.Lat) ? null : PlaceResolver.Resolve(spot.Lat, spot.Lon, null, zones, []).PlaceId,
            LastUpdateUtc: spot.Fix?.Ts,
            SpeedMps: spot.Fix?.SpeedMps,
            IsMoving: VehicleRules.IsMoving(spot.Fix?.SpeedMps, spot.Fix?.Ts, now),
            Freshness: FreshnessRules.ForVehicle(now, spot.Fix?.Ts, VehicleStaleAfterMinutes),
            Color: entry.Color,
            AvatarUrl: null,
            ShowInitial: entry.Icon == RosterIcons.Initial);

        // 02 section 9.3: positions, accuracies, batteries and fix ages are Appendix A.1 verbatim; "since" is the local time of the table.
        var spots = Spots();
        var people = new List<MemberVm>();
        var vehicleList = new List<VehicleVm>();
        foreach (var entry in Roster.Entries)
        {
            if (entry.Group == RosterGroup.NotTracked || !CastIdByEntity(entry.EntityId, out var castId) || !spots.TryGetValue(castId, out var spot))
            {
                continue;
            }

            if (entry.Group == RosterGroup.People)
            {
                people.Add(spot.Fix is null ? Static(entry, spot) : Live(entry, spot));
            }
            else
            {
                vehicleList.Add(Vehicle(entry, spot));
            }
        }

        var members = people.Select(ApplyMemberVariants).ToArray();
        var vehicles = vehicleList.ToArray();

        // A member or vehicle is listed under its own place only, stale members included (02 section 4.5).
        IReadOnlyList<DemoPlace> added;
        lock (_placesGate)
        {
            added = _added;
        }

        PlaceVm[] places = DemoPlaces.Drawn.Concat(added)
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
            UnitSystem: UnitSystem.Imperial,
            RetentionFixDays: DemoRetentionFixDays,
            Thresholds: DrivingThresholds.Default);
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

    // Where each role of the cast is (Appendix A.1), whichever group the roster puts it in.
    private static Dictionary<string, Spot> Spots()
    {
        Spot Located(DemoMember cast, int ageSeconds, double lat, double lon, double accuracyM, int battery, bool charging, DateTimeOffset since, bool driving, double? speedMps = null) =>
            new(cast.Id, cast.Name.ToLowerInvariant(), Fix(cast.Name.ToLowerInvariant() + "_phone", ageSeconds, lat, lon, accuracyM, battery, charging, speedMps), lat, lon, since, driving, cast.Address, null, VehicleGlyph.Car);

        Spot Parked(string id, string key, VehicleGlyph glyph, int ageSeconds, double lat, double lon) =>
            new(id, key, Fix(key, ageSeconds, lat, lon, null, null, null, 0), lat, lon, null, false, null, null, glyph);

        // The hatchback last reported 20 days ago and has no position now: it is listed ("Last heard 20 days ago") but has no pin (as in 0.1, where its row had none).
        Spot Silent(string id, string key, VehicleGlyph glyph, int ageSeconds) =>
            new(id, key, Fix(key, ageSeconds, 0, 0, null, null, null, 0), double.NaN, double.NaN, null, false, null, null, glyph);

        return new Dictionary<string, Spot>(StringComparer.Ordinal)
        {
            [DemoCast.King.Id] = Located(DemoCast.King, 0, 31.0990, -85.3410, 18, 19, true, Local(17, 52), driving: false),
            [DemoCast.Queen.Id] = Located(DemoCast.Queen, 60, 31.0560, -85.4647, 12, 62, false, Local(21, 12), driving: true, speedMps: 24.1),
            [DemoCast.Jester.Id] = Located(DemoCast.Jester, 3 * 60, 31.1040, -85.3560, 22, 12, false, Local(21, 6), driving: false),
            [DemoCast.Cryptid.Id] = Located(DemoCast.Cryptid, 42 * 60, 31.3382, -82.7291, 35, 10, false, Local(20, 10), driving: false),
            [DemoCast.Prince.Id] = new(DemoCast.Prince.Id, "prince", null, PrinceLat, PrinceLon, null, false, null, DemoCast.Prince.StaticLabel, VehicleGlyph.Car),
            [DemoCast.Wagon.Id] = Parked(DemoCast.Wagon.Id, "wagon", DemoCast.Wagon.Glyph, (int)(Anchor - Local(21, 5)).TotalSeconds, WagonLat, WagonLon),
            [DemoCast.Chariot.Id] = Silent(DemoCast.Chariot.Id, "hatchback", DemoCast.Chariot.Glyph, 20 * 24 * 3600),
        };
    }

    // The companion-app fix of a role, ageSeconds before the anchor.
    private static RawFix Fix(string name, int ageSeconds, double lat, double lon, double? accuracyM, int? battery, bool? charging, double? speedMps) => new(
        EntityId: $"device_tracker.{name}",
        Source: FixSource.Companion,
        Ts: Anchor.AddSeconds(-ageSeconds),
        Lat: lat,
        Lon: lon,
        AccuracyM: accuracyM,
        SpeedMps: speedMps,
        BatteryPct: battery,
        Charging: charging);

    // The Demo has no photos, so a person's face is the initial unless the owner chose a glyph.
    private static VehicleGlyph? GlyphOf(RosterEntry entry) => RosterIcons.GlyphOf(entry.Icon);

    private static bool CastIdByEntity(string entityId, out string castId) => DemoRoster.CastIdByEntity.TryGetValue(entityId, out castId!);

    // One role of the cast as the snapshot needs it. A role with no fix is the static prince (D24).
    private sealed record Spot(string Id, string Key, RawFix? Fix, double Lat, double Lon, DateTimeOffset? Since, bool Driving, string? Address, string? StaticLabel, VehicleGlyph Glyph);
}
