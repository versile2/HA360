using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Realm.Domain;
using Xunit;

namespace Realm.Demo.Tests;

// The data-layer tests of 02 section 9.7. The first half is the snapshot at the frozen instant 2026-09-30T21:25:00-05:00,
// as 02 section 9.3 and 01 Appendix A.1 to A.3 give it; the second half (from "The driving half") is the week reports, the
// drive lists, the nine variants and the session hooks of 02 sections 9.4 and 9.5. Expected values are the spec's own numbers
// typed out here (positions, batteries, accuracies, fix ages, "since" times, distances and bearings; drives, miles, top
// speeds and event counts), never read back from the data under test.
public class DemoDataTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-30T21:25:00-05:00", CultureInfo.InvariantCulture);

    private static IRealmSession NewSession() => new DemoRealmSessionFactory().Create(null);

    private static RealmSnapshot Snapshot() => NewSession().Current;

    private static MemberVm Member(string id) => Snapshot().Members.Single(m => m.Id == id);

    private static VehicleVm Vehicle(string id) => Snapshot().Vehicles.Single(v => v.Id == id);

    // Local clock time on the fixture day (CDT, UTC-5) as an instant.
    private static DateTimeOffset LocalTime(int hour, int minute) => new(2026, 9, 30, hour, minute, 0, TimeSpan.FromHours(-5));

    // Great-circle distance in metres between two members (both have a position).
    private static double DistanceM(MemberVm origin, MemberVm target) =>
        Geo.DistanceM(origin.Lat!.Value, origin.Lon!.Value, target.Lat!.Value, target.Lon!.Value);

    // Initial great-circle bearing in degrees from one member to another.
    private static double BearingDeg(MemberVm origin, MemberVm target)
    {
        var phi1 = origin.Lat!.Value * Math.PI / 180;
        var phi2 = target.Lat!.Value * Math.PI / 180;
        var deltaLambda = (target.Lon!.Value - origin.Lon!.Value) * Math.PI / 180;
        var y = Math.Sin(deltaLambda) * Math.Cos(phi2);
        var x = (Math.Cos(phi1) * Math.Sin(phi2)) - (Math.Sin(phi1) * Math.Cos(phi2) * Math.Cos(deltaLambda));
        return ((Math.Atan2(y, x) * 180 / Math.PI) + 360) % 360;
    }

    // ---- the snapshot as a whole ----------------------------------------------------------------------------

    [Fact]
    public void The_snapshot_is_taken_at_the_frozen_instant_in_chicago()
    {
        var session = NewSession();
        var snapshot = session.Current;

        Assert.Equal(Now, snapshot.ServerNowUtc);
        Assert.Equal(Now, session.Time.GetUtcNow());
        Assert.Equal("America/Chicago", snapshot.Zone);
        Assert.Equal("America/Chicago", session.Zone.Id);
        Assert.Equal(0, snapshot.StatsVersion);
        Assert.Equal(DayOfWeek.Monday, snapshot.WeekStart);
        Assert.Equal(UnitSystem.Imperial, snapshot.UnitSystem);
    }

    [Fact]
    public void The_snapshot_lists_the_cast_in_order()
    {
        var snapshot = Snapshot();

        Assert.Equal("king,queen,jester,cryptid,prince", string.Join(",", snapshot.Members.Select(m => m.Id)));
        Assert.Equal("wagon,chariot", string.Join(",", snapshot.Vehicles.Select(v => v.Id)));

        foreach (var member in snapshot.Members)
        {
            var cast = DemoCast.Members.Single(c => c.Id == member.Id);

            Assert.Equal(cast.Name, member.DisplayName);
            Assert.Equal(cast.Lore, member.LoreTitle);
            Assert.Equal(cast.Color, member.Color);
            Assert.Equal(cast.SortOrder, member.SortOrder);
        }
    }

    // R2-016: the demo has no avatar images; the UI draws initials on the member colour.
    [Fact]
    public void Nobody_has_an_avatar_url()
    {
        var snapshot = Snapshot();

        Assert.Equal(5, snapshot.Members.Count);
        Assert.All(snapshot.Members, m => Assert.Null(m.AvatarUrl));
    }

    // Four members share a position, one is a static pin: "4 in the Realm · 1 driving" (the stale member is counted, the
    // static pin is not).
    [Fact]
    public void Four_live_members_are_in_the_realm_and_one_is_driving()
    {
        var snapshot = Snapshot();

        Assert.Equal(4, snapshot.Members.Count(m => m.Kind == MemberKind.Live));
        Assert.Equal("prince", Assert.Single(snapshot.Members, m => m.Kind == MemberKind.Static).Id);
        Assert.Equal("queen", Assert.Single(snapshot.Members, m => m.IsDriving).Id);
    }

    [Fact]
    public void Connections_are_the_four_names_with_three_connected_and_the_placeholder_not()
    {
        var connections = Snapshot().Connections;

        Assert.Equal("HomeAssistant,Life360Trackers,FordPass,VehiclePlaceholder", string.Join(",", connections.Select(c => c.Name)));
        Assert.Equal("Connected,Connected,Connected,NotConnected", string.Join(",", connections.Select(c => c.State)));
        Assert.Equal(Now, connections[0].LastSyncUtc);
        Assert.Equal(Now, connections[1].LastSyncUtc);
        Assert.Equal(Now, connections[2].LastSyncUtc);
        Assert.Null(connections[3].LastSyncUtc);
    }

    // ---- members: 02 section 9.3, row by row -----------------------------------------------------------------

    // id, latitude, longitude, accuracy (m), battery (%), fix age (min), "since" hour and minute (local), place.
    [Theory]
    [InlineData("king", 31.0990, -85.3410, 18.0, 19, 0, 17, 52, "home")]
    [InlineData("queen", 31.0560, -85.4647, 12.0, 62, 1, 21, 12, null)]
    [InlineData("jester", 31.1040, -85.3560, 22.0, 12, 3, 21, 6, "jester_hall")]
    [InlineData("cryptid", 31.3382, -82.7291, 35.0, 10, 42, 20, 10, null)]
    public void Live_members_have_the_position_accuracy_battery_age_since_and_place_of_the_table(
        string id, double lat, double lon, double accuracyM, int batteryPct, int ageMinutes, int sinceHour, int sinceMinute, string? placeId)
    {
        var member = Member(id);

        Assert.Equal(MemberKind.Live, member.Kind);
        Assert.Equal(lat, member.Lat);
        Assert.Equal(lon, member.Lon);
        Assert.Equal(accuracyM, member.AccuracyM);
        Assert.Equal(batteryPct, member.BatteryPct);
        Assert.Equal(Now.AddMinutes(-ageMinutes), member.LastUpdateUtc);
        Assert.Equal(Now.AddMinutes(-ageMinutes), member.BatteryAsOfUtc);
        Assert.Equal(LocalTime(sinceHour, sinceMinute), member.SinceUtc);
        Assert.Equal(placeId, member.PlaceId);
    }

    [Fact]
    public void King_is_at_home_charging_and_fresh_with_no_street()
    {
        var king = Member("king");

        Assert.True(king.Charging);
        Assert.Equal(Freshness.Fresh, king.Freshness);
        Assert.False(king.IsDriving);
        Assert.Null(king.SpeedMps);
        Assert.Null(king.Street);
        Assert.Null(king.City);
        Assert.Null(king.Region);
        Assert.Null(king.FullAddress);
        Assert.Null(king.StaticLabel);
    }

    // The queen drives at 24.1 m/s (54 mph) on "I-65", her battery is not charging, and she is in no zone.
    [Fact]
    public void Queen_is_driving_at_24_1_metres_per_second_on_i_35()
    {
        var queen = Member("queen");

        Assert.True(queen.IsDriving);
        Assert.Equal(24.1, queen.SpeedMps);
        Assert.Equal("I-65", queen.Street);
        Assert.Null(queen.City);
        Assert.Null(queen.Region);
        Assert.False(queen.Charging);
        Assert.Equal(Freshness.Fresh, queen.Freshness);
    }

    [Fact]
    public void Jester_is_at_the_jesters_hall_with_the_fictional_address()
    {
        var jester = Member("jester");

        Assert.Equal("48 Larkspur Lane, Millbrook, AL", jester.FullAddress);
        Assert.Equal("48 Larkspur Lane", jester.Street);
        Assert.Equal("Millbrook", jester.City);
        Assert.Equal("AL", jester.Region);
        Assert.False(jester.IsDriving);
        Assert.Null(jester.SpeedMps);
        Assert.Equal(Freshness.Fresh, jester.Freshness);
    }

    // The member whose fix is 42 minutes old is Stale (42 is above the 40 minute effective threshold, 02 section 4.7).
    // The street comes from the Life360 fix and its age is measured from the winning fix, not from now (D54), so a stale
    // member keeps their last-known street.
    [Fact]
    public void Cryptid_is_stale_at_42_minutes_and_keeps_its_last_known_street()
    {
        var snapshot = Snapshot();
        var cryptid = snapshot.Members.Single(m => m.Id == "cryptid");

        Assert.Equal(TimeSpan.FromMinutes(42), snapshot.ServerNowUtc - cryptid.LastUpdateUtc);
        Assert.Equal(Freshness.Stale, cryptid.Freshness);
        Assert.Equal("Eastgate Avenue", cryptid.Street);
        Assert.Equal("Pinebrook", cryptid.City);
        Assert.Equal("AL", cryptid.Region);
        Assert.Equal("Eastgate Avenue, Pinebrook, AL", cryptid.FullAddress);
        Assert.False(cryptid.IsDriving);
        Assert.Equal("Fresh,Fresh,Fresh,Stale,Static", string.Join(",", snapshot.Members.Select(m => m.Freshness)));
    }

    [Fact]
    public void Prince_is_a_static_pin_with_only_the_label()
    {
        var prince = Member("prince");

        Assert.Equal(MemberKind.Static, prince.Kind);
        Assert.Equal(38.8339, prince.Lat);
        Assert.Equal(-92.8214, prince.Lon);
        Assert.Equal("Home · Highmeadow", prince.StaticLabel);
        Assert.Equal(DemoCast.Prince.StaticLabel, prince.StaticLabel);
        Assert.Equal(Freshness.Static, prince.Freshness);
        Assert.False(prince.IsDriving);
        Assert.Null(prince.AccuracyM);
        Assert.Null(prince.BatteryPct);
        Assert.Null(prince.Charging);
        Assert.Null(prince.BatteryAsOfUtc);
        Assert.Null(prince.SpeedMps);
        Assert.Null(prince.Street);
        Assert.Null(prince.City);
        Assert.Null(prince.Region);
        Assert.Null(prince.FullAddress);
        Assert.Null(prince.PlaceId);
        Assert.Null(prince.SinceUtc);
        Assert.Null(prince.LastUpdateUtc);
    }

    // Only the king shows a charging battery; every other live member has a known battery that is not charging.
    [Fact]
    public void Only_the_king_is_charging()
    {
        var live = Snapshot().Members.Where(m => m.Kind == MemberKind.Live).ToList();

        Assert.Equal("king", Assert.Single(live, m => m.Charging == true).Id);
        Assert.All(live.Where(m => m.Id != "king"), m => Assert.False(m.Charging));
    }

    // ---- distances and edge-bubble bearings from the king (02 section 9.3, 01 Appendix A.1) --------------------

    // id, distance in km, bearing in degrees. The cryptid (east) and the prince (north-west) are the two edge bubbles.
    [Theory]
    [InlineData("queen", 12.71, 248.0)]
    [InlineData("jester", 1.53, 291.0)]
    [InlineData("cryptid", 249.79, 83.0)]
    [InlineData("prince", 1096.54, 324.0)]
    public void Members_are_at_the_distance_and_bearing_of_the_table_from_the_king(string id, double distanceKm, double bearingDeg)
    {
        var king = Member("king");
        var member = Member(id);

        Assert.InRange(DistanceM(king, member) / 1000, distanceKm - 0.01, distanceKm + 0.01);
        Assert.InRange(BearingDeg(king, member), bearingDeg - 0.5, bearingDeg + 0.5);
    }

    // The two members behind the edge bubbles are the only ones beyond the 80 km "far away" distance.
    [Fact]
    public void The_cryptid_and_the_prince_are_the_two_members_far_enough_for_edge_bubbles()
    {
        var snapshot = Snapshot();
        var king = snapshot.Members.Single(m => m.Id == "king");

        var far = snapshot.Members.Where(m => DistanceM(king, m) > 80_000).Select(m => m.Id);

        Assert.Equal("cryptid,prince", string.Join(",", far));
    }

    // ---- vehicles -------------------------------------------------------------------------------------------

    // 02 section 9.3: the pickup is 7.78 m from the king, inside home, ignition off, 71% fuel, 18 432 mi, last update 21:05.
    [Fact]
    public void Wagon_is_parked_at_home_with_the_ignition_off()
    {
        var snapshot = Snapshot();
        var king = snapshot.Members.Single(m => m.Id == "king");
        var wagon = snapshot.Vehicles.Single(v => v.Id == "wagon");

        Assert.Equal("The King's Wagon", wagon.LoreTitle);
        Assert.Equal(VehicleGlyph.Pickup, wagon.Glyph);
        Assert.Equal(31.09907, wagon.Lat);
        Assert.Equal(-85.34100, wagon.Lon);
        Assert.InRange(Geo.DistanceM(king.Lat!.Value, king.Lon!.Value, wagon.Lat!.Value, wagon.Lon!.Value), 7.7, 7.9);
        Assert.Equal("home", wagon.PlaceId);
        Assert.Null(wagon.Street);
        Assert.Equal(IgnitionState.Off, wagon.Ignition);
        Assert.Null(wagon.RemoteStartSecondsLeft);
        Assert.Equal(0.0, wagon.SpeedMps);
        Assert.False(wagon.IsMoving);
        Assert.Equal(71, wagon.FuelPct);
        Assert.InRange(wagon.OdometerM!.Value, 29_663_428.0, 29_663_429.0);
        Assert.Equal(LocalTime(21, 5), wagon.LastUpdateUtc);
        Assert.Equal(Freshness.Fresh, wagon.Freshness);
        Assert.False(wagon.IsPlaceholder);
        Assert.Null(wagon.PlaceholderNote);
    }

    // The second vehicle is a placeholder: no position, no sensors, never connected.
    [Fact]
    public void Chariot_is_a_placeholder_with_no_position()
    {
        var chariot = Vehicle("chariot");

        Assert.Equal("The Queen's Chariot", chariot.LoreTitle);
        Assert.Equal(VehicleGlyph.Car, chariot.Glyph);
        Assert.True(chariot.IsPlaceholder);
        Assert.Equal("Awaiting the royal scribes (the maker's app)", chariot.PlaceholderNote);
        Assert.Equal(DemoCast.ChariotNote, chariot.PlaceholderNote);
        Assert.Null(chariot.Lat);
        Assert.Null(chariot.Lon);
        Assert.Null(chariot.PlaceId);
        Assert.Null(chariot.Ignition);
        Assert.Null(chariot.FuelPct);
        Assert.Null(chariot.OdometerM);
        Assert.Null(chariot.LastUpdateUtc);
        Assert.Null(chariot.SpeedMps);
        Assert.False(chariot.IsMoving);
        Assert.Equal(Freshness.NoFix, chariot.Freshness);
    }

    // ---- places ---------------------------------------------------------------------------------------------

    // 14 zones are drawn and listed; the 15th, the arrival zone, is not. Only home and the jester's hall are occupied.
    [Fact]
    public void Fourteen_places_are_drawn_and_two_are_occupied()
    {
        var places = Snapshot().Places;

        Assert.Equal(14, places.Count);
        Assert.Equal(
            "home,jester_hall,work,work_2,park,orrin,mara,skate_one,skate_two,queen_office,derby,cemetery,vet,wheels",
            string.Join(",", places.Select(p => p.Id)));
        Assert.DoesNotContain(places, p => p.Id == "approach");
        Assert.Equal(2, places.Count(p => p.MemberIdsInside.Count + p.VehicleIdsInside.Count > 0));
    }

    [Fact]
    public void Home_holds_the_king_and_the_wagon_and_the_jesters_hall_holds_the_jester()
    {
        var places = Snapshot().Places;
        var home = places.Single(p => p.Id == "home");
        var hall = places.Single(p => p.Id == "jester_hall");

        Assert.Equal("king", Assert.Single(home.MemberIdsInside));
        Assert.Equal("wagon", Assert.Single(home.VehicleIdsInside));
        Assert.Equal("jester", Assert.Single(hall.MemberIdsInside));
        Assert.Empty(hall.VehicleIdsInside);
        Assert.All(places.Where(p => p.Id is not ("home" or "jester_hall")), p => Assert.Empty(p.MemberIdsInside));
        Assert.All(places.Where(p => p.Id is not "home"), p => Assert.Empty(p.VehicleIdsInside));
    }

    // The display names of the table: the second Work and the second Rollerdome carry " (2)".
    [Fact]
    public void Places_carry_the_display_names_subtitles_and_geometry_of_the_table()
    {
        var places = Snapshot().Places;

        var home = places.Single(p => p.Id == "home");
        Assert.Equal("Hearth Haven", home.DisplayName);
        Assert.Equal("Home", home.Subtitle);
        Assert.Equal(PlaceKind.Home, home.Kind);
        Assert.Equal(31.0990, home.Lat);
        Assert.Equal(-85.3410, home.Lon);
        Assert.Equal(100.0, home.RadiusM);

        Assert.Equal("Work", places.Single(p => p.Id == "work").DisplayName);
        Assert.Equal("Work (2)", places.Single(p => p.Id == "work_2").DisplayName);
        Assert.Equal("Rollerdome", places.Single(p => p.Id == "skate_one").DisplayName);
        Assert.Equal("Rollerdome (2)", places.Single(p => p.Id == "skate_two").DisplayName);
        Assert.Equal("The Jester's Hall", places.Single(p => p.Id == "jester_hall").DisplayName);
        Assert.Equal("Cass's house", places.Single(p => p.Id == "jester_hall").Subtitle);
    }

    // Every zone of the snapshot is the DemoPlaces row of that id.
    [Fact]
    public void Every_place_of_the_snapshot_is_its_demo_place()
    {
        foreach (var place in Snapshot().Places)
        {
            var demo = DemoPlaces.All.Single(p => p.Id == place.Id);

            Assert.Equal(demo.Name, place.DisplayName);
            Assert.Equal(demo.Subtitle, place.Subtitle);
            Assert.Equal(demo.Kind, place.Kind);
            Assert.Equal(demo.Lat, place.Lat);
            Assert.Equal(demo.Lon, place.Lon);
            Assert.Equal(demo.RadiusM, place.RadiusM);
        }
    }

    // ---- the session ----------------------------------------------------------------------------------------

    // 02 section 2.5, 9.2: the two demo user ids resolve to the king and the queen; any other id, or none, to nobody.
    [Theory]
    [InlineData("demo-user-1", "king")]
    [InlineData("demo-user-2", "queen")]
    [InlineData("demo-user-3", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void ResolveMe_maps_the_demo_person_user_ids_to_the_king_and_the_queen(string? haUserId, string? memberId)
    {
        Assert.Equal(memberId, NewSession().ResolveMe(haUserId));
    }

    // The snapshot is built once per session and handed out as the same immutable object.
    [Fact]
    public void The_session_builds_its_snapshot_once()
    {
        var session = NewSession();

        Assert.Same(session.Current, session.Current);
    }

    // Two circuits never share a clock or a snapshot, and the same fixture comes out each time (no randomness).
    [Fact]
    public void Each_session_has_its_own_clock_and_builds_the_same_fixture()
    {
        var factory = new DemoRealmSessionFactory();
        var first = factory.Create(null);
        var second = factory.Create(null);

        Assert.NotSame(first.Time, second.Time);
        Assert.NotSame(first.Current, second.Current);
        Assert.Equal(first.Current.Members, second.Current.Members);
        Assert.Equal(first.Current.Vehicles, second.Current.Vehicles);
    }

    // ?now= moves only the live view: the fixes keep their instants, so they age, and Freshness follows.
    [Fact]
    public void A_later_now_ages_the_fixes_without_moving_them()
    {
        var later = Now.AddMinutes(10);
        var session = new DemoRealmSessionFactory().Create(new DemoUrlParams(later, []));
        var snapshot = session.Current;

        Assert.Equal(later, session.Time.GetUtcNow());
        Assert.Equal(later, snapshot.ServerNowUtc);
        Assert.Equal(later, snapshot.Connections[0].LastSyncUtc);
        Assert.Equal(Now, snapshot.Members.Single(m => m.Id == "king").LastUpdateUtc);
        Assert.Equal("Fresh,Fresh,Fresh,Stale,Static", string.Join(",", snapshot.Members.Select(m => m.Freshness)));
    }

    // An hour later every live fix is older than the 40 minute threshold, and a stale member is no longer shown driving
    // or with a speed (a reported speed counts for 90 seconds).
    [Fact]
    public void An_hour_later_every_live_member_is_stale_and_nobody_is_driving()
    {
        var session = new DemoRealmSessionFactory().Create(new DemoUrlParams(Now.AddHours(1), []));
        var members = session.Current.Members;

        Assert.Equal("Stale,Stale,Stale,Stale,Static", string.Join(",", members.Select(m => m.Freshness)));
        Assert.DoesNotContain(members, m => m.IsDriving);
        Assert.All(members, m => Assert.Null(m.SpeedMps));
    }

    // 02 section 9.0 and 03 section 2.2: the session wraps the data source and implements all seven members.
    [Fact]
    public async Task The_session_can_be_subscribed_to_and_disposed()
    {
        var session = NewSession();
        var raised = 0;
        void Handler() => raised++;

        session.Changed += Handler;
        session.Changed -= Handler;
        await session.DisposeAsync();

        Assert.Equal(0, raised);
    }

    // AddRealmDemo (03 section 2.1): the factory port resolves to the Demo factory.
    [Fact]
    public void AddRealmDemo_registers_the_demo_session_factory()
    {
        using var provider = new ServiceCollection().AddRealmDemo().BuildServiceProvider();
        using var scope = provider.CreateScope();

        var factory = scope.ServiceProvider.GetRequiredService<IRealmSessionFactory>();

        Assert.IsType<DemoRealmSessionFactory>(factory);
        Assert.Equal(Now, factory.Create(null).Time.GetUtcNow());
    }

    // ================================================================================================================
    // The driving half: 02 sections 9.4 to 9.7, always through the public session members (GetWeekReportAsync and
    // GetDriverWeekAsync). Every expected figure is typed out here from the spec's tables, never read back from the
    // data under test.
    // ================================================================================================================

    private const double TenthMileMetres = 160.9344;
    private const double MetresPerSecondPerMph = 0.44704;
    private const int SpeedingThresholdMph = 80;
    private const string AllSources = "all-sources";

    private static readonly string[] Kinds = [EventKeys.Speeding, EventKeys.Phone, EventKeys.Accel, EventKeys.Braking];

    // The report drivers in table (cast) order. The report itself lists them in card order.
    private static readonly string[] Drivers = ["king", "queen", "jester", "cryptid"];

    private static readonly TimeZoneInfo Chicago = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");

    // 02 section 9.4, the base (all-sources) figures of every driver and week: drives, tenths of a mile (94.4 mi is 944),
    // top speed in mph, then speeding, phone, rapid acceleration and hard braking.
    private static readonly (int Week, string Id, int Drives, int Tenths, int Top, int Speeding, int Phone, int Accel, int Braking)[] Table =
    [
        (0, "king", 22, 944, 96, 6, 60, 3, 0),
        (0, "queen", 10, 1182, 82, 2, 31, 1, 1),
        (0, "jester", 18, 2026, 88, 38, 115, 11, 3),
        (0, "cryptid", 14, 3660, 84, 10, 44, 3, 1),
        (1, "king", 24, 1013, 91, 5, 64, 3, 0),
        (1, "queen", 11, 1265, 82, 3, 33, 1, 1),
        (1, "jester", 20, 2152, 92, 41, 142, 8, 5),
        (1, "cryptid", 16, 3996, 83, 14, 62, 3, 3),
        (2, "king", 20, 928, 94, 4, 70, 2, 1),
        (2, "queen", 10, 1204, 81, 2, 36, 2, 1),
        (2, "jester", 19, 1903, 86, 36, 150, 8, 6),
        (2, "cryptid", 17, 3978, 81, 16, 66, 3, 3),
        (3, "king", 18, 851, 88, 4, 61, 3, 0),
        (3, "queen", 9, 997, 81, 1, 30, 1, 1),
        (3, "jester", 17, 1764, 90, 27, 133, 10, 5),
        (3, "cryptid", 14, 3417, 82, 12, 66, 3, 2),
    ];

    // 02 section 9.4: the like-for-like week-0 comparators (to last Wednesday 21:25) of speeding, phone, accel, braking.
    private static readonly Dictionary<string, int[]> Week0Comparators = new()
    {
        ["king"] = [5, 71, 2, 0],
        ["queen"] = [3, 33, 1, 1],
        ["jester"] = [29, 120, 6, 4],
        ["cryptid"] = [12, 50, 3, 2],
    };

    // The drives the fixture states outright (02 section 9.4 (d)), by local start time.
    private static readonly (string Id, DateTime Start)[] FixedDrives =
    [
        ("king", new DateTime(2026, 9, 29, 16, 12, 0)),
        ("king", new DateTime(2026, 9, 30, 8, 4, 0)),
        ("king", new DateTime(2026, 9, 30, 17, 31, 0)),
        ("jester", new DateTime(2026, 9, 30, 21, 1, 0)),
    ];

    private static IRealmSession SessionWith(string variants, DateTimeOffset? now = null) =>
        new DemoRealmSessionFactory().Create(new DemoUrlParams(now, variants.Length == 0 ? [] : variants.Split(',')));

    private static async Task<WeekReportVm> ReportOf(string variants, int week, DayOfWeek weekStart = DayOfWeek.Monday) =>
        await SessionWith(variants).GetWeekReportAsync(week, weekStart, CancellationToken.None);

    private static async Task<DriverWeek> DriverWeekOf(string variants, string id, int week)
    {
        var driverWeek = await SessionWith(variants).GetDriverWeekAsync(id, week, DayOfWeek.Monday, CancellationToken.None);
        Assert.NotNull(driverWeek);
        return driverWeek;
    }

    // A distance that is a whole number of tenths of a mile (02 section 9.4 (a)); returns the tenths.
    private static int WholeTenths(double meters)
    {
        var tenths = meters / TenthMileMetres;
        Assert.InRange(Math.Abs(tenths - Math.Round(tenths)), 0, 0.000001);
        return (int)Math.Round(tenths);
    }

    private static int Mph(double metersPerSecond) => (int)Math.Round(metersPerSecond / MetresPerSecondPerMph);

    private static DateTime LocalOf(DateTimeOffset instant) => TimeZoneInfo.ConvertTime(instant, Chicago).DateTime;

    private static int[] BaseCounts(int week, string id)
    {
        var row = Table.Single(r => r.Week == week && r.Id == id);
        return [row.Speeding, row.Phone, row.Accel, row.Braking];
    }

    // The comparison counts of a driver: week 0 has its own table, weeks 1 and 2 compare to the next older week, week 3 has none.
    private static int[]? BaseComparators(int week, string id) => week switch
    {
        0 => Week0Comparators[id],
        < 3 => BaseCounts(week + 1, id),
        _ => null,
    };

    // What a dataset shows of a driver's counts: all-sources everything; the default fixture the production rules 1 and 2 of
    // 02 section 9.4 (phone only for the phone-capable king, no rapid acceleration and no hard braking).
    private static int?[] Shown(string variants, string id, int[]? counts)
    {
        if (counts is null)
        {
            return [null, null, null, null];
        }

        var all = variants == AllSources;
        return [counts[0], all || id == "king" ? counts[1] : null, all ? counts[2] : null, all ? counts[3] : null];
    }

    private static string Text(object? value) => value is null ? "null" : Convert.ToString(value, CultureInfo.InvariantCulture)!;

    private static string Counts(IReadOnlyDictionary<string, int?> events)
    {
        Assert.Equal(Kinds.Length, events.Count);
        return string.Join(",", Kinds.Select(kind => Text(events[kind])));
    }

    private static void AssertStat(EventStat stat, int? total, int? comparator, int? trend, EventAvailability availability, bool partial)
    {
        Assert.Equal(total, stat.Total);
        Assert.Equal(comparator, stat.ComparatorTotal);
        Assert.Equal(trend, stat.TrendDelta);
        Assert.Equal(availability, stat.Availability);
        Assert.Equal(partial, stat.Partial);
        Assert.Equal(StatSource.Derived, stat.Source);
    }

    private static DriverSummary SummaryOf(WeekReportVm report, string id) => report.Drivers.Single(d => d.MemberId == id);

    private static string Summarise(DriverSummary d) =>
        $"{d.MemberId} {Text(d.Drives)} {Text(d.Meters)} {d.DistanceBasis} {d.CoarseTrips} {d.PhoneCapable} {Counts(d.Events)} {Text(d.EventsTotal)} {d.EventsPartial} {d.Covered} {Text(d.CoverageStartUtc)}";

    private static string Describe(DriveVm t) =>
        $"{Text(t.StartUtc)} {Text(t.EndUtc)} {t.FromLabel} {t.ToLabel} {Text(t.Meters)} {Text(t.TopSpeedMps)} {Counts(t.Events)}";

    private static string TopText(TopSpeedStat? top) => top is null
        ? "top none"
        : $"top {top.MemberId} {Text(top.SpeedMps)} {Text(top.AtUtc)} {top.Street} {string.Join(",", top.Drivers.Select(d => d.MemberId + ":" + Text(d.SpeedMps)))}";

    // The sum of per-drive counts; null when the counts are null (a null figure stays null on every drive).
    private static int? SumOrNull(IEnumerable<int?> counts)
    {
        var all = counts.ToList();
        return all.Exists(count => count is null) ? null : all.Sum();
    }

    // Every figure of a report except its week bounds.
    private static string Figures(WeekReportVm report)
    {
        var lines = new List<string>
        {
            $"{report.IsCurrent} {report.Coverage} {report.DistanceBasis} {report.Totals.Drives} {Text(report.Totals.Meters)}",
            TopText(report.TopSpeed),
        };
        foreach (var kind in Kinds)
        {
            var stat = report.Events[kind];
            lines.Add($"{kind} {Text(stat.Total)} {Text(stat.ComparatorTotal)} {Text(stat.TrendDelta)} {stat.Source} {stat.Availability} {stat.Partial} {stat.Note} "
                + string.Join(",", stat.Drivers.Select(d => $"{d.MemberId}:{Text(d.Count)}/{Text(d.ComparatorCount)}")));
        }

        lines.AddRange(report.Drivers.Select(Summarise));
        return string.Join("\n", lines);
    }

    // The whole demo history of a session: the report of every week and the driver week of every cast member.
    private static async Task<string> Digest(IRealmSession session)
    {
        var lines = new List<string>();
        for (var week = 0; week < 4; week++)
        {
            var report = await session.GetWeekReportAsync(week, DayOfWeek.Monday, CancellationToken.None);
            lines.Add($"week {week} {Text(report.Start)} {Text(report.End)}");
            lines.Add(Figures(report));
            foreach (var id in DemoCast.Members.Select(m => m.Id))
            {
                var driverWeek = await session.GetDriverWeekAsync(id, week, DayOfWeek.Monday, CancellationToken.None);
                lines.Add(driverWeek is null
                    ? $"{id} none"
                    : $"{id} {Summarise(driverWeek.Summary)}\n{string.Join("\n", driverWeek.Trips.Select(Describe))}");
            }
        }

        return string.Join("\n", lines);
    }

    private static Task<string> Digest(string variants, DateTimeOffset? now = null) => Digest(SessionWith(variants, now));

    // ---- the week report: the default fixture (02 section 9.4, "Final totals by week") -----------------------

    // Drives, tenths of a mile, the week's top speed and its driver, speeding with comparator and trend, phone (the king's alone)
    // with comparator and trend, then EventsTotal of king / queen / jester / cryptid.
    [Theory]
    [InlineData(0, 64, 7812, 96, "king", 56, 49, 7, 60, 71, -11, "66,2,38,10")]
    [InlineData(1, 71, 8426, 92, "jester", 63, 58, 5, 64, 70, -6, "69,3,41,14")]
    [InlineData(2, 66, 8013, 94, "king", 58, 44, 14, 70, 61, 9, "74,2,36,16")]
    [InlineData(3, 58, 7029, 90, "jester", 44, null, null, 61, null, null, "65,1,27,12")]
    public async Task The_default_report_has_the_final_totals_of_the_week(
        int week, int drives, int tenths, int topMph, string topDriver, int speeding, int? speedingComparator, int? speedingTrend,
        int phone, int? phoneComparator, int? phoneTrend, string eventsTotals)
    {
        var report = await ReportOf("", week);

        Assert.Equal(drives, report.Totals.Drives);
        Assert.Equal(tenths, WholeTenths(report.Totals.Meters));
        var top = Assert.IsType<TopSpeedStat>(report.TopSpeed);
        Assert.Equal(topDriver, top.MemberId);
        Assert.Equal(topMph, Mph(top.SpeedMps));
        AssertStat(report.Events[EventKeys.Speeding], speeding, speedingComparator, speedingTrend, EventAvailability.All, partial: false);
        AssertStat(report.Events[EventKeys.Phone], phone, phoneComparator, phoneTrend, EventAvailability.Some, partial: true);
        AssertStat(report.Events[EventKeys.Accel], null, null, null, EventAvailability.None, partial: false);
        AssertStat(report.Events[EventKeys.Braking], null, null, null, EventAvailability.None, partial: false);
        Assert.Equal(eventsTotals, string.Join(",", Drivers.Select(id => SummaryOf(report, id).EventsTotal)));
        Assert.Equal(speeding + phone, Drivers.Sum(id => SummaryOf(report, id).EventsTotal));
    }

    // 02 section 9.6: week 0 is 781.2 mi = 1 257 219.53 m (7 812 tenths x 160.9344 m) and the king's 94.4 mi is 151 922.07 m.
    [Fact]
    public async Task Week_0_distances_are_the_exact_metres_of_the_tenths()
    {
        var report = await ReportOf("", 0);

        Assert.Equal(1_257_219.53, report.Totals.Meters, 2);
        Assert.Equal(151_922.07, SummaryOf(report, "king").Meters!.Value, 2);
    }

    [Theory]
    [InlineData(0, "2026-09-28T00:00:00-05:00", "2026-10-04T23:59:59-05:00")]
    [InlineData(1, "2026-09-21T00:00:00-05:00", "2026-09-27T23:59:59-05:00")]
    [InlineData(2, "2026-09-14T00:00:00-05:00", "2026-09-20T23:59:59-05:00")]
    [InlineData(3, "2026-09-07T00:00:00-05:00", "2026-09-13T23:59:59-05:00")]
    public async Task Each_report_covers_the_monday_week_of_the_default_instant(int week, string start, string end)
    {
        var report = await ReportOf("", week);

        Assert.Equal(DateTimeOffset.Parse(start, CultureInfo.InvariantCulture), report.Start);
        Assert.Equal(DateTimeOffset.Parse(end, CultureInfo.InvariantCulture), report.End);
        Assert.Equal(TimeSpan.FromHours(-5), report.Start.Offset);
        Assert.Equal(week == 0, report.IsCurrent);
        Assert.Equal(WeekCoverage.Full, report.Coverage);
        Assert.Equal(DistanceBasis.Gps, report.DistanceBasis);
    }

    // The driver rows of the report in card order (drives, then miles, then name) and every figure of the tables, in both datasets.
    // Dataset "" is the default fixture; the speeding note is the sampled-count qualifier of 02 section 5.8.
    [Theory]
    [InlineData("", 0)]
    [InlineData("", 1)]
    [InlineData("", 2)]
    [InlineData("", 3)]
    [InlineData(AllSources, 0)]
    [InlineData(AllSources, 1)]
    [InlineData(AllSources, 2)]
    [InlineData(AllSources, 3)]
    public async Task The_driver_rows_are_the_table_rows_in_card_order(string variants, int week)
    {
        var report = await ReportOf(variants, week);

        Assert.Equal("king,jester,cryptid,queen", string.Join(",", report.Drivers.Select(d => d.MemberId)));
        var top = Assert.IsType<TopSpeedStat>(report.TopSpeed);
        Assert.Equal("king,jester,cryptid,queen", string.Join(",", top.Drivers.Select(d => d.MemberId)));
        Assert.Equal("king,jester,cryptid,queen", string.Join(",", report.Events[EventKeys.Speeding].Drivers.Select(d => d.MemberId)));
        foreach (var driver in report.Drivers)
        {
            var row = Table.Single(r => r.Week == week && r.Id == driver.MemberId);
            var counts = Shown(variants, driver.MemberId, BaseCounts(week, driver.MemberId));

            Assert.Equal(row.Drives, driver.Drives);
            Assert.Equal(row.Tenths, WholeTenths(driver.Meters!.Value));
            Assert.Equal(DistanceBasis.Gps, driver.DistanceBasis);
            Assert.Equal(0, driver.CoarseTrips);
            Assert.False(driver.EventsPartial);
            Assert.True(driver.Covered);
            Assert.Null(driver.CoverageStartUtc);
            Assert.Equal(variants == AllSources || driver.MemberId == "king", driver.PhoneCapable);
            Assert.Equal(string.Join(",", counts.Select(count => Text(count))), Counts(driver.Events));
            Assert.Equal(counts.Sum(), driver.EventsTotal);
            Assert.Equal(row.Top, Mph(top.Drivers.Single(d => d.MemberId == driver.MemberId).SpeedMps!.Value));
        }

        Assert.Equal(Table.Where(r => r.Week == week).Max(r => r.Top), Mph(top.SpeedMps));
    }

    // Each event type across the drivers, and the like-for-like comparators of the table, in both datasets.
    [Theory]
    [InlineData("", 0)]
    [InlineData("", 1)]
    [InlineData("", 2)]
    [InlineData("", 3)]
    [InlineData(AllSources, 0)]
    [InlineData(AllSources, 1)]
    [InlineData(AllSources, 2)]
    [InlineData(AllSources, 3)]
    public async Task The_event_stats_carry_the_per_driver_counts_and_comparators_of_the_tables(string variants, int week)
    {
        var report = await ReportOf(variants, week);

        for (var kind = 0; kind < Kinds.Length; kind++)
        {
            var stat = report.Events[Kinds[kind]];

            Assert.Equal(Drivers.Length, stat.Drivers.Count);
            foreach (var driver in stat.Drivers)
            {
                Assert.Equal(Shown(variants, driver.MemberId, BaseCounts(week, driver.MemberId))[kind], driver.Count);
                Assert.Equal(Shown(variants, driver.MemberId, BaseComparators(week, driver.MemberId))[kind], driver.ComparatorCount);
            }
        }
    }

    // Rules 1 and 2 of 02 section 9.4 hold in every week of the default fixture, comparators included.
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task The_default_fixture_shows_phone_use_for_the_king_only_and_no_accel_or_braking(int week)
    {
        var report = await ReportOf("", week);

        Assert.NotNull(SummaryOf(report, "king").Events[EventKeys.Phone]);
        Assert.All(report.Drivers.Where(d => d.MemberId != "king"), d => Assert.Null(d.Events[EventKeys.Phone]));
        Assert.All(report.Drivers, d => Assert.Null(d.Events[EventKeys.Accel]));
        Assert.All(report.Drivers, d => Assert.Null(d.Events[EventKeys.Braking]));
        Assert.All(report.Drivers, d => Assert.Equal(d.MemberId == "king", d.PhoneCapable));

        var phone = report.Events[EventKeys.Phone].Drivers;
        Assert.NotNull(phone.Single(d => d.MemberId == "king").Count);
        Assert.All(phone.Where(d => d.MemberId != "king"), d =>
        {
            Assert.Null(d.Count);
            Assert.Null(d.ComparatorCount);
        });
        Assert.All(report.Events[EventKeys.Accel].Drivers, d => Assert.Null(d.Count));
        Assert.All(report.Events[EventKeys.Braking].Drivers, d => Assert.Null(d.Count));
    }

    // The speeding note is the sampled-count qualifier; the other kinds have none; every source is Derived (D26).
    [Fact]
    public async Task Only_the_speeding_stat_carries_the_sampled_count_note()
    {
        var report = await ReportOf("", 0);

        Assert.Equal("Counted from ~42 s samples; short bursts are missed", report.Events[EventKeys.Speeding].Note);
        Assert.Null(report.Events[EventKeys.Phone].Note);
        Assert.Null(report.Events[EventKeys.Accel].Note);
        Assert.Null(report.Events[EventKeys.Braking].Note);
        Assert.Equal("speeding,phone,accel,braking", string.Join(",", Kinds.Where(report.Events.ContainsKey)));
        Assert.All(report.Events.Values, stat => Assert.Equal(StatSource.Derived, stat.Source));
    }

    // 02 section 9.4 (d): the week-0 top speed is the king's 96 mph on I-65, Tuesday 29 September at 16:12.
    [Fact]
    public async Task The_week_0_top_speed_is_the_kings_96_mph_on_i_35_on_tuesday_at_16_12()
    {
        var report = await ReportOf("", 0);
        var top = Assert.IsType<TopSpeedStat>(report.TopSpeed);

        Assert.Equal("king", top.MemberId);
        Assert.Equal(96, Mph(top.SpeedMps));
        Assert.Equal(42.91584, top.SpeedMps, 5);
        Assert.Equal("I-65", top.Street);
        Assert.Equal(new DateTimeOffset(2026, 9, 29, 16, 12, 0, TimeSpan.FromHours(-5)), top.AtUtc);
    }

    // Each week's overall top speed is unique, and its time is the start of that driver's top-speed drive.
    [Theory]
    [InlineData(0, "king")]
    [InlineData(1, "jester")]
    [InlineData(2, "king")]
    [InlineData(3, "jester")]
    public async Task Each_week_has_one_overall_top_speed_at_the_start_of_the_winners_fastest_drive(int week, string winner)
    {
        var session = SessionWith("");
        var report = await session.GetWeekReportAsync(week, DayOfWeek.Monday, CancellationToken.None);
        var top = Assert.IsType<TopSpeedStat>(report.TopSpeed);

        Assert.Equal(winner, top.MemberId);
        Assert.Single(top.Drivers, d => d.SpeedMps == top.SpeedMps);
        var trips = (await session.GetDriverWeekAsync(winner, week, DayOfWeek.Monday, CancellationToken.None))!.Trips;
        Assert.Equal(top.AtUtc, trips.MaxBy(t => t.TopSpeedMps)!.StartUtc);
        Assert.InRange(top.AtUtc, report.Start, report.End);
    }

    // 02 section 9.0: with a Sunday week start the same per-week figures are served for the Sunday-based bounds.
    [Fact]
    public async Task A_sunday_week_start_serves_the_same_figures_for_the_sunday_bounds()
    {
        var monday = await ReportOf("", 0);
        var sunday = await ReportOf("", 0, DayOfWeek.Sunday);

        Assert.Equal(new DateTimeOffset(2026, 9, 27, 0, 0, 0, TimeSpan.FromHours(-5)), sunday.Start);
        Assert.Equal(new DateTimeOffset(2026, 10, 3, 23, 59, 59, TimeSpan.FromHours(-5)), sunday.End);
        Assert.Equal(64, sunday.Totals.Drives);
        Assert.Equal(Figures(monday), Figures(sunday));
    }

    // 02 section 9.0: the demo report is a frozen history; ?now= and the running clock of ha-down move only the live snapshot.
    [Theory]
    [InlineData("", 3)]
    [InlineData("", -5)]
    [InlineData("ha-down", 0)]
    [InlineData("ha-down", 7)]
    [InlineData(AllSources, 2)]
    public async Task The_report_and_the_drive_lists_are_unchanged_by_now(string variants, int shiftDays)
    {
        var shifted = await Digest(variants, Now.AddDays(shiftDays));
        var frozen = await Digest(variants);

        Assert.Equal(frozen, shifted);
    }

    [Fact]
    public async Task Every_call_returns_the_same_history()
    {
        var session = SessionWith("");

        Assert.Equal(await Digest(session), await Digest(session));
        Assert.Equal(await Digest(session), await Digest(""));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    public async Task A_week_offset_outside_0_to_3_is_a_caller_error(int week)
    {
        var session = NewSession();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await session.GetWeekReportAsync(week, DayOfWeek.Monday, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await session.GetDriverWeekAsync("king", week, DayOfWeek.Monday, CancellationToken.None));
    }

    // ---- the rules of 02 section 9.4 in both datasets ---------------------------------------------------------

    // Rule 3 (O-7) per driver-week: speeding above zero only with a week top speed of at least 80 mph.
    [Theory]
    [InlineData("", 0)]
    [InlineData("", 1)]
    [InlineData("", 2)]
    [InlineData("", 3)]
    [InlineData(AllSources, 0)]
    [InlineData(AllSources, 1)]
    [InlineData(AllSources, 2)]
    [InlineData(AllSources, 3)]
    public async Task Speeding_is_positive_only_for_a_driver_whose_week_top_speed_reaches_80_mph(string variants, int week)
    {
        var report = await ReportOf(variants, week);
        var tops = Assert.IsType<TopSpeedStat>(report.TopSpeed).Drivers.ToDictionary(d => d.MemberId, d => Mph(d.SpeedMps!.Value));

        var speeders = report.Drivers.Where(d => d.Events[EventKeys.Speeding] > 0).ToList();
        Assert.NotEmpty(speeders);
        Assert.All(speeders, d => Assert.True(tops[d.MemberId] >= SpeedingThresholdMph, $"{d.MemberId} speeds at a top speed of {tops[d.MemberId]} mph"));
    }

    // ---- the drive lists: constraints (a) to (f) of 02 section 9.4 -------------------------------------------

    [Theory]
    [InlineData("prince")]
    [InlineData("nobody")]
    [InlineData("")]
    [InlineData("wagon")]
    public async Task A_driver_week_is_null_for_the_prince_and_for_unknown_ids(string id)
    {
        var session = SessionWith("");

        for (var week = 0; week < 4; week++)
        {
            Assert.Null(await session.GetDriverWeekAsync(id, week, DayOfWeek.Monday, CancellationToken.None));
        }
    }

    // "Summary is that driver's element of the same report's Drivers[]" (02 section 9.0).
    [Theory]
    [InlineData("", 0)]
    [InlineData("", 1)]
    [InlineData("", 2)]
    [InlineData("", 3)]
    [InlineData(AllSources, 0)]
    [InlineData(AllSources, 1)]
    [InlineData(AllSources, 2)]
    [InlineData(AllSources, 3)]
    public async Task A_driver_week_summary_is_the_drivers_element_of_the_report(string variants, int week)
    {
        var session = SessionWith(variants);
        var report = await session.GetWeekReportAsync(week, DayOfWeek.Monday, CancellationToken.None);

        foreach (var expected in report.Drivers)
        {
            var driverWeek = await session.GetDriverWeekAsync(expected.MemberId, week, DayOfWeek.Monday, CancellationToken.None);

            Assert.NotNull(driverWeek);
            Assert.Equal(Summarise(expected), Summarise(driverWeek.Summary));
        }
    }

    // (a) and (b): the count, the exact sum in tenths of a mile, 0.3 to 160 mi per drive, and per-drive event counts that are
    // non-negative integers summing to the driver's figure (a null figure stays null on every drive).
    [Theory]
    [InlineData("", 0)]
    [InlineData("", 1)]
    [InlineData("", 2)]
    [InlineData("", 3)]
    [InlineData(AllSources, 0)]
    [InlineData(AllSources, 1)]
    [InlineData(AllSources, 2)]
    [InlineData(AllSources, 3)]
    public async Task Drive_lists_have_the_table_counts_and_sum_exactly_to_the_tenth_of_a_mile(string variants, int week)
    {
        foreach (var id in Drivers)
        {
            var row = Table.Single(r => r.Week == week && r.Id == id);
            var trips = (await DriverWeekOf(variants, id, week)).Trips;
            var tenths = trips.Select(t => WholeTenths(t.Meters)).ToList();

            Assert.Equal(row.Drives, trips.Count);
            Assert.Equal(row.Tenths, tenths.Sum());
            Assert.All(tenths, t => Assert.InRange(t, 3, 1600));

            var expected = Shown(variants, id, BaseCounts(week, id));
            for (var kind = 0; kind < Kinds.Length; kind++)
            {
                var perDrive = trips.Select(t => t.Events[Kinds[kind]]).ToList();
                if (expected[kind] is null)
                {
                    Assert.All(perDrive, count => Assert.Null(count));
                }
                else
                {
                    Assert.All(perDrive, count =>
                    {
                        Assert.NotNull(count);
                        Assert.True(count.Value >= 0);
                    });
                    Assert.Equal(expected[kind], perDrive.Sum());
                }
            }
        }
    }

    // (b): the top speed of every drive is at most the driver's maximum, and exactly one drive equals it.
    [Theory]
    [InlineData("", 0)]
    [InlineData("", 1)]
    [InlineData("", 2)]
    [InlineData("", 3)]
    [InlineData(AllSources, 0)]
    [InlineData(AllSources, 1)]
    [InlineData(AllSources, 2)]
    [InlineData(AllSources, 3)]
    public async Task Each_driver_has_exactly_one_drive_at_the_week_top_speed(string variants, int week)
    {
        foreach (var id in Drivers)
        {
            var maximum = Table.Single(r => r.Week == week && r.Id == id).Top;
            var trips = (await DriverWeekOf(variants, id, week)).Trips;
            Assert.All(trips, t => Assert.NotNull(t.TopSpeedMps));
            var tops = trips.Select(t => Mph(t.TopSpeedMps!.Value)).ToList();

            Assert.Equal(maximum, tops.Max());
            Assert.Single(tops, top => top == maximum);
        }
    }

    // (b), rule 3 applied per drive in both datasets: speeding needs 80 mph on that drive, phone use at least 10 mph.
    [Theory]
    [InlineData("", 0)]
    [InlineData("", 1)]
    [InlineData("", 2)]
    [InlineData("", 3)]
    [InlineData(AllSources, 0)]
    [InlineData(AllSources, 1)]
    [InlineData(AllSources, 2)]
    [InlineData(AllSources, 3)]
    public async Task A_drive_with_speeding_tops_80_mph_and_one_with_phone_use_tops_10_mph(string variants, int week)
    {
        foreach (var id in Drivers)
        {
            var trips = (await DriverWeekOf(variants, id, week)).Trips;

            Assert.Contains(trips, t => t.Events[EventKeys.Speeding] > 0);
            Assert.All(trips.Where(t => t.Events[EventKeys.Speeding] > 0), t => Assert.True(Mph(t.TopSpeedMps!.Value) >= SpeedingThresholdMph));
            Assert.All(trips.Where(t => t.Events[EventKeys.Phone] > 0), t => Assert.True(Mph(t.TopSpeedMps!.Value) >= 10));
        }
    }

    // (c): newest first, non-overlapping, inside the week; week 0 ends no later than Wednesday 21:20 local.
    [Theory]
    [InlineData("", 0)]
    [InlineData("", 1)]
    [InlineData("", 2)]
    [InlineData("", 3)]
    [InlineData(AllSources, 0)]
    [InlineData(AllSources, 1)]
    [InlineData(AllSources, 2)]
    [InlineData(AllSources, 3)]
    public async Task Drives_are_newest_first_and_do_not_overlap_inside_the_week(string variants, int week)
    {
        var report = await ReportOf(variants, week);
        var latest = week == 0 ? new DateTimeOffset(2026, 9, 30, 21, 20, 0, TimeSpan.FromHours(-5)) : report.End;

        foreach (var id in Drivers)
        {
            var trips = (await DriverWeekOf(variants, id, week)).Trips;

            Assert.All(trips, t =>
            {
                Assert.True(t.StartUtc < t.EndUtc);
                Assert.True(t.StartUtc >= report.Start, $"{id} starts before the week");
                Assert.True(t.EndUtc <= latest, $"{id} ends after {latest}");
            });
            for (var i = 0; i + 1 < trips.Count; i++)
            {
                Assert.True(trips[i + 1].EndUtc <= trips[i].StartUtc, $"{id}: drive {i + 1} overlaps drive {i}");
            }
        }
    }

    // (c): start times on a 5-minute grid between 06:30 and 22:00; duration is the distance at 28 mph plus 4 minutes. The four
    // fixed drives of (d) keep their stated times, so they are exempt from the grid and the duration rule.
    [Theory]
    [InlineData("", 0)]
    [InlineData("", 1)]
    [InlineData("", 2)]
    [InlineData("", 3)]
    [InlineData(AllSources, 0)]
    public async Task Generated_drives_start_on_the_five_minute_grid_and_last_distance_over_28_mph_plus_4_minutes(string variants, int week)
    {
        foreach (var id in Drivers)
        {
            var trips = (await DriverWeekOf(variants, id, week)).Trips;

            foreach (var trip in trips)
            {
                var start = LocalOf(trip.StartUtc);
                Assert.InRange(start.TimeOfDay, new TimeSpan(6, 30, 0), new TimeSpan(22, 0, 0));
                if (week == 0 && FixedDrives.Contains((id, start)))
                {
                    continue;
                }

                Assert.Equal(0, start.Minute % 5);
                Assert.Equal(0, start.Second);
                var minutes = (trip.EndUtc - trip.StartUtc).TotalMinutes;
                var expected = (trip.Meters / (TenthMileMetres * 10) / 28 * 60) + 4;
                Assert.InRange(minutes, expected - 1, expected + 1);
            }
        }
    }

    // (d), the king: Wed 17:31-17:52 "Work -> Hearth Haven" 7.2 mi at 62 mph, Wed 08:04-08:21 "Hearth Haven -> Work" 7.2 mi,
    // and the Tuesday 16:12 drive that carries the week's 96 mph.
    [Theory]
    [InlineData("")]
    [InlineData(AllSources)]
    public async Task The_kings_fixed_drives_of_week_0_are_the_ones_of_the_spec(string variants)
    {
        var trips = (await DriverWeekOf(variants, "king", 0)).Trips;

        var evening = Assert.Single(trips, t => LocalOf(t.StartUtc) == new DateTime(2026, 9, 30, 17, 31, 0));
        Assert.Equal(new DateTime(2026, 9, 30, 17, 52, 0), LocalOf(evening.EndUtc));
        Assert.Equal("Work", evening.FromLabel);
        Assert.Equal("Hearth Haven", evening.ToLabel);
        Assert.Equal(72, WholeTenths(evening.Meters));
        Assert.Equal(62, Mph(evening.TopSpeedMps!.Value));

        var morning = Assert.Single(trips, t => LocalOf(t.StartUtc) == new DateTime(2026, 9, 30, 8, 4, 0));
        Assert.Equal(new DateTime(2026, 9, 30, 8, 21, 0), LocalOf(morning.EndUtc));
        Assert.Equal("Hearth Haven", morning.FromLabel);
        Assert.Equal("Work", morning.ToLabel);
        Assert.Equal(72, WholeTenths(morning.Meters));

        var fastest = Assert.Single(trips, t => LocalOf(t.StartUtc) == new DateTime(2026, 9, 29, 16, 12, 0));
        Assert.Equal(96, Mph(fastest.TopSpeedMps!.Value));
        Assert.Equal(96, Mph(trips.Max(t => t.TopSpeedMps!.Value)));
    }

    // (d), the jester: the newest drive is Wed 21:01-21:06 "Hearth Haven -> The Jester's Hall", 1.1 mi at 38 mph (AC-41),
    // so his other 17 drives sum to 201.5 mi.
    [Theory]
    [InlineData("")]
    [InlineData(AllSources)]
    public async Task The_jesters_newest_drive_is_the_1_1_mile_hop_to_the_hall(string variants)
    {
        var trips = (await DriverWeekOf(variants, "jester", 0)).Trips;
        var newest = trips[0];

        Assert.Equal(new DateTime(2026, 9, 30, 21, 1, 0), LocalOf(newest.StartUtc));
        Assert.Equal(new DateTime(2026, 9, 30, 21, 6, 0), LocalOf(newest.EndUtc));
        Assert.Equal("Hearth Haven", newest.FromLabel);
        Assert.Equal("The Jester's Hall", newest.ToLabel);
        Assert.Equal(11, WholeTenths(newest.Meters));
        Assert.Equal(38, Mph(newest.TopSpeedMps!.Value));
        Assert.Equal(17, trips.Count - 1);
        Assert.Equal(2015, trips.Skip(1).Sum(t => WholeTenths(t.Meters)));
    }

    // (e): labels are demo place names or street strings of the cast, and a drive that starts on the day of the one before
    // it starts where that one ended. The drives with stated labels (d) are exempt.
    [Theory]
    [InlineData("", 0)]
    [InlineData("", 1)]
    [InlineData("", 2)]
    [InlineData("", 3)]
    [InlineData(AllSources, 0)]
    public async Task Drive_labels_are_demo_places_and_chain_through_the_day(string variants, int week)
    {
        var allowed = DemoPlaces.All.Select(p => p.Name).Concat(DemoCast.Members.Select(m => m.Address).OfType<string>()).ToHashSet();

        foreach (var id in Drivers)
        {
            var oldestFirst = (await DriverWeekOf(variants, id, week)).Trips.Reverse().ToList();

            for (var i = 0; i < oldestFirst.Count; i++)
            {
                Assert.NotNull(oldestFirst[i].FromLabel);
                Assert.NotNull(oldestFirst[i].ToLabel);
                Assert.Contains(oldestFirst[i].FromLabel!, allowed);
                Assert.Contains(oldestFirst[i].ToLabel!, allowed);

                var start = LocalOf(oldestFirst[i].StartUtc);
                var stated = week == 0 && FixedDrives.Contains((id, start));
                if (i > 0 && !stated && LocalOf(oldestFirst[i - 1].StartUtc).Date == start.Date)
                {
                    Assert.Equal(oldestFirst[i - 1].ToLabel, oldestFirst[i].FromLabel);
                }
            }
        }
    }

    // (f): weeks 1 to 3 have no fixed drives and use the whole week, so every week of the chips has a drive list.
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Earlier_weeks_have_no_fixed_drives_and_run_to_the_end_of_the_week(int week)
    {
        var session = SessionWith("");
        var starts = new List<DateTime>();
        foreach (var id in Drivers)
        {
            var trips = (await session.GetDriverWeekAsync(id, week, DayOfWeek.Monday, CancellationToken.None))!.Trips;
            starts.AddRange(trips.Select(t => LocalOf(t.StartUtc)));
        }

        Assert.Contains(starts, start => start.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday);
        Assert.All(starts, start => Assert.Equal(0, start.Minute % 5));
    }

    // ---- all-sources (02 section 9.5, D27) ---------------------------------------------------------------------

    // Phone, rapid acceleration and hard braking with comparator and trend, then EventsTotal of king / queen / jester / cryptid.
    [Theory]
    [InlineData(0, 250, 274, -24, 18, 12, 6, 5, 7, -2, "69,35,167,58")]
    [InlineData(1, 301, 322, -21, 15, 15, 0, 9, 11, -2, "72,38,196,82")]
    [InlineData(2, 322, 290, 32, 15, 17, -2, 11, 8, 3, "77,41,200,88")]
    [InlineData(3, 290, null, null, 17, null, null, 8, null, null, "68,33,175,83")]
    public async Task The_all_sources_report_serves_every_kind_for_every_driver(
        int week, int phone, int? phoneComparator, int? phoneTrend, int accel, int? accelComparator, int? accelTrend,
        int braking, int? brakingComparator, int? brakingTrend, string eventsTotals)
    {
        var report = await ReportOf(AllSources, week);
        var normal = await ReportOf("", week);

        AssertStat(report.Events[EventKeys.Phone], phone, phoneComparator, phoneTrend, EventAvailability.All, partial: false);
        AssertStat(report.Events[EventKeys.Accel], accel, accelComparator, accelTrend, EventAvailability.All, partial: false);
        AssertStat(report.Events[EventKeys.Braking], braking, brakingComparator, brakingTrend, EventAvailability.All, partial: false);
        AssertStat(report.Events[EventKeys.Speeding], normal.Events[EventKeys.Speeding].Total, normal.Events[EventKeys.Speeding].ComparatorTotal, normal.Events[EventKeys.Speeding].TrendDelta, EventAvailability.All, partial: false);
        Assert.Equal(eventsTotals, string.Join(",", Drivers.Select(id => SummaryOf(report, id).EventsTotal)));
        Assert.All(report.Drivers, d => Assert.True(d.PhoneCapable));
        Assert.All(report.Drivers, d => Assert.False(d.EventsPartial));

        // The same drives, miles, speeding and top speeds as the default fixture.
        Assert.Equal(normal.Totals.Drives, report.Totals.Drives);
        Assert.Equal(normal.Totals.Meters, report.Totals.Meters);
        Assert.Equal(TopText(normal.TopSpeed), TopText(report.TopSpeed));
    }

    // ---- the other variants through the report (02 section 9.5) -------------------------------------------------

    // phone-unavailable nulls the king's phone too: no totals, no asterisk, availability unchanged, and the king's EventsTotal is
    // his speeding alone (AC-40: "6 speeding events") with no asterisk.
    [Theory]
    [InlineData(0, 6)]
    [InlineData(1, 5)]
    [InlineData(2, 4)]
    [InlineData(3, 4)]
    public async Task Phone_unavailable_nulls_the_phone_of_every_driver_the_king_included(int week, int kingSpeeding)
    {
        var report = await ReportOf("phone-unavailable", week);
        var normal = await ReportOf("", week);

        AssertStat(report.Events[EventKeys.Phone], null, null, null, EventAvailability.Some, partial: false);
        Assert.All(report.Events[EventKeys.Phone].Drivers, d =>
        {
            Assert.Null(d.Count);
            Assert.Null(d.ComparatorCount);
        });
        Assert.All(report.Drivers, d => Assert.Null(d.Events[EventKeys.Phone]));
        Assert.Equal(kingSpeeding, SummaryOf(report, "king").EventsTotal);
        Assert.All(report.Drivers, d => Assert.False(d.EventsPartial));
        Assert.Equal(normal.Events[EventKeys.Speeding].Total, report.Events[EventKeys.Speeding].Total);
        Assert.Equal(normal.Totals, report.Totals);

        // The drive lists follow: every drive's phone count is null and the rest is as before.
        foreach (var id in Drivers)
        {
            var trips = (await DriverWeekOf("phone-unavailable", id, week)).Trips;

            Assert.All(trips, t => Assert.Null(t.Events[EventKeys.Phone]));
            Assert.Equal(BaseCounts(week, id)[0], trips.Sum(t => t.Events[EventKeys.Speeding]));
        }
    }

    // all-sources,phone-unavailable: the jester's EventsTotal is 167 - 115 = 52 (38 + 11 + 3), and the composition is the same
    // in either order because no variant undoes another.
    [Theory]
    [InlineData("all-sources,phone-unavailable")]
    [InlineData("phone-unavailable,all-sources")]
    public async Task All_sources_with_phone_unavailable_removes_only_the_phone_counts(string variants)
    {
        var report = await ReportOf(variants, 0);

        Assert.Equal("9,4,52,14", string.Join(",", Drivers.Select(id => SummaryOf(report, id).EventsTotal)));
        AssertStat(report.Events[EventKeys.Phone], null, null, null, EventAvailability.All, partial: false);
        AssertStat(report.Events[EventKeys.Accel], 18, 12, 6, EventAvailability.All, partial: false);
        AssertStat(report.Events[EventKeys.Braking], 5, 7, -2, EventAvailability.All, partial: false);
        Assert.All(report.Drivers, d => Assert.False(d.EventsPartial));
        Assert.All(report.Drivers, d => Assert.True(d.PhoneCapable));
    }

    // life360-down: all four kinds are null for every driver (EventsTotal null: no kind has a count); drives and miles stay.
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Life360_down_nulls_all_four_kinds_but_keeps_the_drives_and_miles(int week)
    {
        var report = await ReportOf("life360-down", week);
        var normal = await ReportOf("", week);

        foreach (var kind in Kinds)
        {
            Assert.Equal(normal.Events[kind].Availability, report.Events[kind].Availability);
            Assert.Null(report.Events[kind].Total);
            Assert.Null(report.Events[kind].ComparatorTotal);
            Assert.Null(report.Events[kind].TrendDelta);
            Assert.False(report.Events[kind].Partial);
            Assert.All(report.Events[kind].Drivers, d =>
            {
                Assert.Null(d.Count);
                Assert.Null(d.ComparatorCount);
            });
        }

        Assert.All(report.Drivers, d =>
        {
            Assert.Null(d.EventsTotal);
            Assert.Equal("null,null,null,null", Counts(d.Events));
            Assert.True(d.Covered);
        });
        Assert.Equal(normal.Totals, report.Totals);
        Assert.Equal(normal.TopSpeed!.MemberId, report.TopSpeed!.MemberId);

        foreach (var id in Drivers)
        {
            var driverWeek = await DriverWeekOf("life360-down", id, week);

            Assert.Equal(Table.Single(r => r.Week == week && r.Id == id).Drives, driverWeek.Trips.Count);
            Assert.All(driverWeek.Trips, t => Assert.Equal("null,null,null,null", Counts(t.Events)));
        }
    }

    // empty-week: week 0 has no drives and no distance, event counts 0 where there is a source and null where there is none, no
    // top speed and empty drive lists; the other weeks are untouched.
    [Fact]
    public async Task Empty_week_empties_week_0_only()
    {
        var report = await ReportOf("empty-week", 0);

        Assert.Equal(0, report.Totals.Drives);
        Assert.Equal(0.0, report.Totals.Meters);
        Assert.Null(report.TopSpeed);
        Assert.Equal("king,queen,jester,cryptid", string.Join(",", report.Drivers.Select(d => d.MemberId)));
        Assert.All(report.Drivers, d =>
        {
            Assert.True(d.Covered);
            Assert.Equal(0, d.Drives);
            Assert.Equal(0.0, d.Meters);
            Assert.Equal(d.MemberId == "king" ? "0,0,null,null" : "0,null,null,null", Counts(d.Events));
            Assert.Equal(0, d.EventsTotal);
        });
        Assert.Equal(0, report.Events[EventKeys.Speeding].Total);
        Assert.Equal(0, report.Events[EventKeys.Phone].Total);
        Assert.Null(report.Events[EventKeys.Accel].Total);
        Assert.Null(report.Events[EventKeys.Braking].Total);

        foreach (var id in Drivers)
        {
            var driverWeek = await DriverWeekOf("empty-week", id, 0);

            Assert.Empty(driverWeek.Trips);
            Assert.Equal(0, driverWeek.Summary.Drives);
        }

        for (var week = 1; week < 4; week++)
        {
            Assert.Equal(Figures(await ReportOf("", week)), Figures(await ReportOf("empty-week", week)));
            Assert.Equal(
                string.Join("\n", (await DriverWeekOf("", "king", week)).Trips.Select(Describe)),
                string.Join("\n", (await DriverWeekOf("empty-week", "king", week)).Trips.Select(Describe)));
        }
    }

    // fresh-install, week 0: still Full, but every comparator is null, so there are no trend arrows.
    [Fact]
    public async Task Fresh_install_keeps_week_0_full_but_without_comparators()
    {
        var report = await ReportOf("fresh-install", 0);
        var normal = await ReportOf("", 0);

        Assert.Equal(WeekCoverage.Full, report.Coverage);
        Assert.Equal(64, report.Totals.Drives);
        Assert.Equal(7812, WholeTenths(report.Totals.Meters));
        AssertStat(report.Events[EventKeys.Speeding], 56, null, null, EventAvailability.All, partial: false);
        AssertStat(report.Events[EventKeys.Phone], 60, null, null, EventAvailability.Some, partial: true);
        AssertStat(report.Events[EventKeys.Accel], null, null, null, EventAvailability.None, partial: false);
        AssertStat(report.Events[EventKeys.Braking], null, null, null, EventAvailability.None, partial: false);
        Assert.All(Kinds.SelectMany(kind => report.Events[kind].Drivers), d => Assert.Null(d.ComparatorCount));
        Assert.All(report.Drivers, d => Assert.True(d.Covered));
        Assert.All(report.Drivers, d => Assert.Null(d.CoverageStartUtc));
        Assert.Equal(TopText(normal.TopSpeed), TopText(report.TopSpeed));
    }

    // fresh-install, week 1 (02 section 9.5): Partial from 2026-09-23 12:00 local (17:00Z), counts and miles scaled by 9/14 and
    // rounded half up, top speeds unchanged, comparators null.
    [Fact]
    public async Task Fresh_install_scales_last_week_to_the_days_since_the_recording_began()
    {
        var report = await ReportOf("fresh-install", 1);
        var start = new DateTimeOffset(2026, 9, 23, 17, 0, 0, TimeSpan.Zero);

        Assert.Equal(WeekCoverage.Partial, report.Coverage);
        Assert.Equal(45, report.Totals.Drives);
        Assert.Equal(5416, WholeTenths(report.Totals.Meters));
        AssertStat(report.Events[EventKeys.Speeding], 40, null, null, EventAvailability.All, partial: false);
        AssertStat(report.Events[EventKeys.Phone], 41, null, null, EventAvailability.Some, partial: true);
        AssertStat(report.Events[EventKeys.Accel], null, null, null, EventAvailability.None, partial: false);
        AssertStat(report.Events[EventKeys.Braking], null, null, null, EventAvailability.None, partial: false);
        var top = Assert.IsType<TopSpeedStat>(report.TopSpeed);
        Assert.Equal("jester", top.MemberId);
        Assert.Equal(92, Mph(top.SpeedMps));
        Assert.Equal("91,82,92,83", string.Join(",", Drivers.Select(id => Mph(top.Drivers.Single(d => d.MemberId == id).SpeedMps!.Value))));

        // king / queen / jester / cryptid: drives 15 / 7 / 13 / 10, miles 65.1 / 81.3 / 138.3 / 256.9, speeding 3 / 2 / 26 / 9.
        Assert.Equal("15,7,13,10", string.Join(",", Drivers.Select(id => SummaryOf(report, id).Drives)));
        Assert.Equal("651,813,1383,2569", string.Join(",", Drivers.Select(id => WholeTenths(SummaryOf(report, id).Meters!.Value))));
        Assert.Equal("3,2,26,9", string.Join(",", Drivers.Select(id => SummaryOf(report, id).Events[EventKeys.Speeding])));
        Assert.Equal("44,2,26,9", string.Join(",", Drivers.Select(id => SummaryOf(report, id).EventsTotal)));
        Assert.Equal(81, Drivers.Sum(id => SummaryOf(report, id).EventsTotal));
        Assert.All(report.Drivers, d =>
        {
            Assert.True(d.Covered);
            Assert.Equal(start, d.CoverageStartUtc);
            Assert.False(d.EventsPartial);
        });
        Assert.All(Kinds.SelectMany(kind => report.Events[kind].Drivers), d => Assert.Null(d.ComparatorCount));
    }

    // The drive lists of that week use the scaled figures and begin at the recording start.
    [Fact]
    public async Task Fresh_install_drive_lists_hold_the_scaled_figures_from_the_recording_start()
    {
        var report = await ReportOf("fresh-install", 1);
        var start = new DateTimeOffset(2026, 9, 23, 17, 0, 0, TimeSpan.Zero);
        var top = Assert.IsType<TopSpeedStat>(report.TopSpeed);

        foreach (var id in Drivers)
        {
            var scaled = SummaryOf(report, id);
            var trips = (await DriverWeekOf("fresh-install", id, 1)).Trips;

            Assert.Equal(scaled.Drives, trips.Count);
            Assert.Equal(WholeTenths(scaled.Meters!.Value), trips.Sum(t => WholeTenths(t.Meters)));
            Assert.Equal(scaled.Events[EventKeys.Speeding], SumOrNull(trips.Select(t => t.Events[EventKeys.Speeding])));
            Assert.Equal(scaled.Events[EventKeys.Phone], SumOrNull(trips.Select(t => t.Events[EventKeys.Phone])));
            Assert.All(trips, t =>
            {
                Assert.True(t.StartUtc >= start);
                Assert.True(t.EndUtc <= report.End);
            });
            Assert.Equal(Mph(top.Drivers.Single(d => d.MemberId == id).SpeedMps!.Value), trips.Max(t => Mph(t.TopSpeedMps!.Value)));
        }
    }

    // Weeks 2 and 3: NoRecord, nobody covered, null counts, no top speed, no drives.
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Fresh_install_has_no_record_of_weeks_2_and_3(int week)
    {
        var session = SessionWith("fresh-install");
        var report = await session.GetWeekReportAsync(week, DayOfWeek.Monday, CancellationToken.None);

        Assert.Equal(WeekCoverage.NoRecord, report.Coverage);
        Assert.Equal(0, report.Totals.Drives);
        Assert.Equal(0.0, report.Totals.Meters);
        Assert.Null(report.TopSpeed);
        Assert.All(Kinds.Select(kind => report.Events[kind]), stat =>
        {
            Assert.Null(stat.Total);
            Assert.Null(stat.ComparatorTotal);
            Assert.Null(stat.TrendDelta);
            Assert.False(stat.Partial);
        });
        foreach (var id in Drivers)
        {
            var driver = SummaryOf(report, id);
            var driverWeek = await session.GetDriverWeekAsync(id, week, DayOfWeek.Monday, CancellationToken.None);

            Assert.False(driver.Covered);
            Assert.Null(driver.Drives);
            Assert.Null(driver.Meters);
            Assert.Null(driver.EventsTotal);
            Assert.Null(driver.CoverageStartUtc);
            Assert.Equal("null,null,null,null", Counts(driver.Events));
            Assert.NotNull(driverWeek);
            Assert.False(driverWeek.Summary.Covered);
            Assert.Empty(driverWeek.Trips);
        }
    }

    // The snapshot-only variants leave every figure and every drive list as it was.
    [Theory]
    [InlineData("ha-down")]
    [InlineData("poor-accuracy")]
    [InlineData("no-fix")]
    [InlineData("all-near")]
    [InlineData("poor-accuracy,no-fix,all-near,ha-down")]
    public async Task The_snapshot_variants_leave_the_driving_data_unchanged(string variants)
    {
        Assert.Equal(await Digest(""), await Digest(variants));
    }

    // Unknown names are ignored; every name of 02 section 9.5 is accepted and they compose with commas.
    [Fact]
    public async Task Unknown_variant_names_are_ignored()
    {
        Assert.Equal(await Digest(AllSources), await Digest("bogus,all-sources,also-not-a-variant"));
        Assert.Equal(await Digest(""), await Digest("bogus"));
    }

    [Theory]
    [InlineData("all-sources")]
    [InlineData("phone-unavailable")]
    [InlineData("life360-down")]
    [InlineData("ha-down")]
    [InlineData("poor-accuracy")]
    [InlineData("no-fix")]
    [InlineData("all-near")]
    [InlineData("empty-week")]
    [InlineData("fresh-install")]
    public async Task Every_variant_name_builds_a_session_with_a_snapshot_and_four_weeks(string variant)
    {
        var session = SessionWith(variant);

        Assert.Equal(5, session.Current.Members.Count);
        for (var week = 0; week < WeekMath.ChipCount; week++)
        {
            var report = await session.GetWeekReportAsync(week, DayOfWeek.Monday, CancellationToken.None);
            Assert.Equal(4, report.Drivers.Count);
        }
    }

    [Fact]
    public async Task All_nine_variants_compose_in_one_session()
    {
        var session = SessionWith("all-sources,phone-unavailable,life360-down,ha-down,poor-accuracy,no-fix,all-near,empty-week,fresh-install");
        var report = await session.GetWeekReportAsync(1, DayOfWeek.Monday, CancellationToken.None);

        Assert.Equal("Unavailable,Unavailable,Connected,NotConnected", string.Join(",", session.Current.Connections.Select(c => c.State)));
        Assert.Equal(WeekCoverage.Partial, report.Coverage);
        Assert.Equal(45, report.Totals.Drives);
        Assert.Null(report.Events[EventKeys.Speeding].Total);
        Assert.Equal(WeekCoverage.NoRecord, (await session.GetWeekReportAsync(2, DayOfWeek.Monday, CancellationToken.None)).Coverage);
        Assert.Equal(0, (await session.GetWeekReportAsync(0, DayOfWeek.Monday, CancellationToken.None)).Totals.Drives);
    }

    // ---- the snapshot variants (02 section 9.5) -----------------------------------------------------------------

    [Fact]
    public void Life360_down_reports_the_trackers_unavailable_and_members_lose_their_address_and_speed()
    {
        var snapshot = SessionWith("life360-down").Current;
        var normal = Snapshot();

        Assert.Equal("Connected,Unavailable,Connected,NotConnected", string.Join(",", snapshot.Connections.Select(c => c.State)));
        Assert.All(snapshot.Members.Where(m => m.Kind == MemberKind.Live), m =>
        {
            Assert.Null(m.Street);
            Assert.Null(m.City);
            Assert.Null(m.Region);
            Assert.Null(m.FullAddress);
            Assert.False(m.IsDriving);
            Assert.Null(m.SpeedMps);
        });

        // The HA-side fields stay.
        var king = snapshot.Members.Single(m => m.Id == "king");
        var normalKing = normal.Members.Single(m => m.Id == "king");
        Assert.Equal(normalKing.Lat, king.Lat);
        Assert.Equal(normalKing.Lon, king.Lon);
        Assert.Equal(normalKing.BatteryPct, king.BatteryPct);
        Assert.Equal(normalKing.Charging, king.Charging);
        Assert.Equal(normalKing.PlaceId, king.PlaceId);
        Assert.Equal(normalKing.Freshness, king.Freshness);
    }

    [Fact]
    public void Ha_down_reports_home_assistant_unavailable_from_the_start_of_the_session()
    {
        var connections = SessionWith("ha-down").Current.Connections;

        Assert.Equal("Unavailable,Connected,Connected,NotConnected", string.Join(",", connections.Select(c => c.State)));
        Assert.InRange(connections[0].LastSyncUtc!.Value, Now, Now.AddSeconds(30));
    }

    [Fact]
    public void Poor_accuracy_gives_the_jester_800_metres()
    {
        var snapshot = SessionWith("poor-accuracy").Current;

        Assert.Equal(800.0, snapshot.Members.Single(m => m.Id == "jester").AccuracyM);
        Assert.Equal(
            Snapshot().Members.Where(m => m.Id != "jester"),
            snapshot.Members.Where(m => m.Id != "jester"));
    }

    [Fact]
    public void No_fix_leaves_the_cryptid_without_a_position()
    {
        var snapshot = SessionWith("no-fix").Current;
        var cryptid = snapshot.Members.Single(m => m.Id == "cryptid");

        Assert.Null(cryptid.Lat);
        Assert.Null(cryptid.Lon);
        Assert.Null(cryptid.LastUpdateUtc);
        Assert.Equal(Freshness.NoFix, cryptid.Freshness);
        Assert.Equal(
            Snapshot().Members.Where(m => m.Id != "cryptid"),
            snapshot.Members.Where(m => m.Id != "cryptid"));
    }

    // The cryptid sits 3.07 km and the prince 3.48 km from the king.
    [Fact]
    public void All_near_places_the_cryptid_and_the_prince_within_5_km_of_the_king()
    {
        var snapshot = SessionWith("all-near").Current;
        var king = snapshot.Members.Single(m => m.Id == "king");
        var cryptid = snapshot.Members.Single(m => m.Id == "cryptid");
        var prince = snapshot.Members.Single(m => m.Id == "prince");

        Assert.Equal(31.1250, cryptid.Lat);
        Assert.Equal(-85.3300, cryptid.Lon);
        Assert.Equal(31.0800, prince.Lat);
        Assert.Equal(-85.3700, prince.Lon);
        Assert.InRange(DistanceM(king, cryptid) / 1000, 3.06, 3.08);
        Assert.InRange(DistanceM(king, prince) / 1000, 3.47, 3.49);
        Assert.Equal(Snapshot().Members.Single(m => m.Id == "queen"), snapshot.Members.Single(m => m.Id == "queen"));
    }

    // Under ha-down the clock runs, so the 30 second tick re-evaluates the snapshot and raises Changed (02 section 9.0).
    [Fact]
    public async Task Under_ha_down_the_tick_raises_changed_and_the_snapshot_follows_the_clock()
    {
        var source = new DemoDataSource(new DemoTimeProvider(DemoDataSource.Anchor, advancing: true), DemoVariants.Parse(["ha-down"]));
        await using var session = new DemoRealmSession(source, TimeSpan.FromMilliseconds(20));
        var raised = new TaskCompletionSource();
        var first = session.Current.ServerNowUtc;

        session.Changed += () => raised.TrySetResult();
        var finished = await Task.WhenAny(raised.Task, Task.Delay(TimeSpan.FromSeconds(30)));

        Assert.Same(raised.Task, finished);
        Assert.True(session.Current.ServerNowUtc > first);
        Assert.Equal(TimeSpan.FromSeconds(30), DemoRealmSession.DefaultTickInterval);
    }

    // The test hook of ha-down (02 section 9.5): the banner clears and Changed is raised once; a second call does nothing.
    [Fact]
    public async Task The_restore_hook_connects_home_assistant_again_and_raises_changed_once()
    {
        await using var session = Assert.IsType<DemoRealmSession>(SessionWith("ha-down"));
        var raised = 0;
        session.Changed += () => raised++;
        Assert.Equal(ConnectionState.Unavailable, session.Current.Connections[0].State);

        session.RestoreHomeAssistant();
        session.RestoreHomeAssistant();

        Assert.Equal(1, raised);
        Assert.Equal("Connected,Connected,Connected,NotConnected", string.Join(",", session.Current.Connections.Select(c => c.State)));
    }

    // Without ha-down the clock is frozen and nothing changes: Changed is never raised, even with a very short tick.
    [Fact]
    public async Task Without_ha_down_changed_is_never_raised()
    {
        var source = new DemoDataSource(new DemoTimeProvider(DemoDataSource.Anchor), DemoVariants.None);
        await using var session = new DemoRealmSession(source, TimeSpan.FromMilliseconds(10));
        var raised = 0;

        session.Changed += () => Interlocked.Increment(ref raised);
        await Task.Delay(200);
        session.RestoreHomeAssistant();

        Assert.Equal(0, Volatile.Read(ref raised));
    }

    // ---- no network (02 section 9.7) ---------------------------------------------------------------------------

    // The spec injects a throwing HttpMessageHandler. The Demo assembly has no HttpClient, no handler and no injection point,
    // so there is nothing to inject into; the honest check is that it cannot reach the network at all. The compiler records only
    // the assemblies the code really uses, and none of them is a networking one.
    [Fact]
    public void The_demo_assembly_references_no_networking_library()
    {
        var referenced = typeof(DemoDataSource).Assembly.GetReferencedAssemblies().Select(name => name.Name ?? string.Empty).ToList();

        Assert.Contains("Realm.Domain", referenced);
        Assert.DoesNotContain(referenced, name => name.StartsWith("System.Net", StringComparison.Ordinal));
        Assert.DoesNotContain(referenced, name => name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal));
        Assert.DoesNotContain(referenced, name => name.StartsWith("Grpc", StringComparison.Ordinal));
    }
}
