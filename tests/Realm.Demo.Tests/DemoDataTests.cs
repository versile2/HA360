using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Realm.Domain;
using Xunit;

namespace Realm.Demo.Tests;

// The snapshot half of 02 section 9.7: the fixture at the frozen instant 2026-09-30T21:25:00-05:00, as 02 section 9.3
// and 01 Appendix A.1 to A.3 give it. Expected values are the spec's own numbers typed out here (positions, batteries,
// accuracies, fix ages, "since" times, distances and bearings), never read back from the data under test. The driving
// half (weeks, variants) is slice S4b.
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
        Assert.Equal("prince", Assert.Single(snapshot.Members.Where(m => m.Kind == MemberKind.Static)).Id);
        Assert.Equal("queen", Assert.Single(snapshot.Members.Where(m => m.IsDriving)).Id);
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
    [InlineData("king", 31.0990, -97.3410, 18.0, 19, 0, 17, 52, "home")]
    [InlineData("queen", 31.0560, -97.4647, 12.0, 62, 1, 21, 12, null)]
    [InlineData("jester", 31.1040, -97.3560, 22.0, 12, 3, 21, 6, "jester_hall")]
    [InlineData("cryptid", 31.3382, -94.7291, 35.0, 10, 42, 20, 10, null)]
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

    // The queen drives at 24.1 m/s (54 mph) on "I-35", her battery is not charging, and she is in no zone.
    [Fact]
    public void Queen_is_driving_at_24_1_metres_per_second_on_i_35()
    {
        var queen = Member("queen");

        Assert.True(queen.IsDriving);
        Assert.Equal(24.1, queen.SpeedMps);
        Assert.Equal("I-35", queen.Street);
        Assert.Null(queen.City);
        Assert.Null(queen.Region);
        Assert.False(queen.Charging);
        Assert.Equal(Freshness.Fresh, queen.Freshness);
    }

    [Fact]
    public void Jester_is_at_the_jesters_hall_with_the_fictional_address()
    {
        var jester = Member("jester");

        Assert.Equal("48 Larkspur Lane, Millbrook, TX", jester.FullAddress);
        Assert.Equal("48 Larkspur Lane", jester.Street);
        Assert.Equal("Millbrook", jester.City);
        Assert.Equal("TX", jester.Region);
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
        Assert.Equal("TX", cryptid.Region);
        Assert.Equal("Eastgate Avenue, Pinebrook, TX", cryptid.FullAddress);
        Assert.False(cryptid.IsDriving);
        Assert.Equal("Fresh,Fresh,Fresh,Stale,Static", string.Join(",", snapshot.Members.Select(m => m.Freshness)));
    }

    [Fact]
    public void Prince_is_a_static_pin_with_only_the_label()
    {
        var prince = Member("prince");

        Assert.Equal(MemberKind.Static, prince.Kind);
        Assert.Equal(38.8339, prince.Lat);
        Assert.Equal(-104.8214, prince.Lon);
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

        Assert.Equal("king", Assert.Single(live.Where(m => m.Charging == true)).Id);
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
        Assert.Equal(-97.34100, wagon.Lon);
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
        Assert.Equal(-97.3410, home.Lon);
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
}
