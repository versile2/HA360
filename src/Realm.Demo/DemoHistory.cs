using Realm.Domain;

namespace Realm.Demo;

/// <summary>
/// The Demo's Location History (0.3.0, D123): ten days of stored fixes and trips for the four live people, in the fictional geography of <see cref="DemoPlaces"/>, ending at the
/// fixture's instant (<see cref="DemoDataSource.Anchor"/>), where each person is where the map shows them. Generated, never stored: a weekday and weekend routine per person (the
/// King works at the Counting House and goes home, the Queen drives the interstate to her office, the Jester skates, the Cryptid works late, visits the cemetery on Friday night
/// and is on a long road trip at the end), seeded by <see cref="DemoPrng"/> so the days never change. Visits at no zone carry an address, so they are named by their town.
/// </summary>
internal sealed class DemoHistory
{
    /// <summary>The first and the last day of the generated history (the last is the fixture's day, 2026-09-30, a Wednesday).</summary>
    public static readonly DateOnly FirstDay = new(2026, 9, 21);

    /// <summary>The fixture's day.</summary>
    public static readonly DateOnly LastDay = new(2026, 9, 30);

    private const double SpeedingMps = 80 * 0.44704;
    private static readonly TimeSpan Heartbeat = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(30);

    private readonly TimeZoneInfo _zone;
    private readonly Dictionary<string, Lazy<Generated>> _byMember;

    public DemoHistory(TimeZoneInfo zone)
    {
        _zone = zone;
        _byMember = DemoCast.Members.ToDictionary(
            member => member.Id,
            member => new Lazy<Generated>(() => Generate(member)),
            StringComparer.Ordinal);
    }

    /// <summary>True for the people that have a history.</summary>
    public bool Has(string memberId) => _byMember.ContainsKey(memberId);

    /// <summary>The stored fixes of a person, oldest first.</summary>
    public IReadOnlyList<RawFix> Fixes(string memberId) => _byMember.TryGetValue(memberId, out var data) ? data.Value.Fixes : [];

    /// <summary>The stored trips of a person, oldest first.</summary>
    public IReadOnlyList<StatsTrip> Trips(string memberId) => _byMember.TryGetValue(memberId, out var data) ? data.Value.Trips : [];

    // ---- the routines ----------------------------------------------------------------------------------------------------------------------------

    private sealed record Pt(string? Id, double Lat, double Lon, double JitterM, string? Address)
    {
        public static Pt Of(DemoPlace place) => new(place.Id, place.Lat, place.Lon, Math.Min(30, place.RadiusM * 0.3), null);
    }

    // A person is at At from the arrival (the end of the drive from the stop before, or ArriveMin when given; minutes after local midnight) until LeaveMin. Open: the drive
    // into this stop is still going on at the fixture's instant (so no trip is stored) and the person is somewhere along it.
    private sealed record Stop(Pt At, int LeaveMin, int? ArriveMin = null, bool Open = false);

    private sealed record Generated(IReadOnlyList<RawFix> Fixes, IReadOnlyList<StatsTrip> Trips);

    private static readonly Pt Eastgate = new(null, 31.1250, -85.3300, 12, "Eastgate Avenue, Pinebrook, AL");
    private static readonly Pt RoadTripEnd = new(null, 31.3382, -82.7291, 12, "Larkspur Road, Fernhollow, GA");
    private static readonly Pt QueenOnTheRoad = new(null, 31.0560, -85.4647, 0, "I-65");

    private static int Min(int hour, int minute) => (hour * 60) + minute;

    private static IReadOnlyList<Stop> Plan(DemoMember member, DateOnly day, DemoPrng rng)
    {
        var last = day == LastDay;
        int J(int range) => last ? 0 : rng.Next((2 * range) + 1) - range;
        var weekday = day.DayOfWeek is >= DayOfWeek.Monday and <= DayOfWeek.Friday;
        var home = Pt.Of(DemoPlaces.Home);
        var work = Pt.Of(DemoPlaces.Work);
        var work2 = Pt.Of(DemoPlaces.Work2);
        var park = Pt.Of(DemoPlaces.Park);
        var hall = Pt.Of(DemoPlaces.JesterHall);
        var mara = Pt.Of(DemoPlaces.Mara);
        var wheels = Pt.Of(DemoPlaces.Wheels);
        var vet = Pt.Of(DemoPlaces.Vet);
        var skate = Pt.Of(DemoPlaces.SkateOne);
        var derby = Pt.Of(DemoPlaces.Derby);
        var orrin = Pt.Of(DemoPlaces.Orrin);
        var office = Pt.Of(DemoPlaces.QueenOffice);
        var cemetery = Pt.Of(DemoPlaces.Cemetery);

        switch (member.Id)
        {
            case "king":
                if (!weekday)
                {
                    return day.DayOfWeek == DayOfWeek.Saturday
                        ? [new(home, Min(9, 50) + J(15)), new(wheels, Min(12, 40) + J(10)), new(mara, Min(16, 40) + J(15)), new(home, 1440)]
                        : [new(home, Min(10, 20) + J(15)), new(park, Min(12, 10) + J(10)), new(home, Min(14, 50) + J(10)), new(hall, Min(18, 0) + J(10)), new(home, 1440)];
                }

                return day.DayOfWeek switch
                {
                    DayOfWeek.Wednesday => [new(home, Min(7, 35) + J(8)), new(work, Min(12, 10) + J(5)), new(park, Min(12, 55) + J(5)), new(work, Min(17, 36) + J(8)), new(home, 1440, last ? Min(17, 52) : null)],
                    DayOfWeek.Thursday => [new(home, Min(7, 40) + J(8)), new(work, Min(16, 50) + J(8)), new(vet, Min(17, 32) + J(5)), new(home, 1440)],
                    DayOfWeek.Friday => [new(home, Min(7, 30) + J(8)), new(work, Min(17, 0) + J(10)), new(wheels, Min(20, 0) + J(10)), new(home, 1440)],
                    _ => [new(home, Min(7, 35) + J(8)), new(work, Min(17, 15) + J(12)), new(home, 1440)],
                };

            case "queen":
                if (!weekday)
                {
                    return day.DayOfWeek == DayOfWeek.Saturday
                        ? [new(home, Min(10, 40) + J(10)), new(mara, Min(15, 20) + J(10)), new(home, 1440)]
                        : [new(home, Min(13, 30) + J(10)), new(hall, Min(17, 10) + J(10)), new(home, 1440)];
                }

                return day.DayOfWeek switch
                {
                    DayOfWeek.Wednesday => [new(home, Min(6, 50) + J(5)), new(office, Min(21, 12)), new(QueenOnTheRoad, 1440, Min(21, 24), Open: true)],
                    DayOfWeek.Friday => [new(home, Min(6, 50) + J(8)), new(office, Min(17, 0) + J(10)), new(mara, Min(20, 30) + J(10)), new(home, 1440)],
                    _ => [new(home, Min(6, 50) + J(8)), new(office, Min(16, 40) + J(15)), new(home, 1440)],
                };

            case "jester":
                if (!weekday)
                {
                    return day.DayOfWeek == DayOfWeek.Saturday
                        ? [new(hall, Min(11, 20) + J(10)), new(derby, Min(14, 40) + J(10)), new(hall, 1440)]
                        : [new(hall, Min(10, 30) + J(10)), new(wheels, Min(12, 40) + J(10)), new(hall, 1440)];
                }

                return day.DayOfWeek switch
                {
                    DayOfWeek.Tuesday or DayOfWeek.Thursday => [new(hall, Min(15, 40) + J(8)), new(skate, Min(18, 20) + J(8)), new(hall, 1440)],
                    DayOfWeek.Wednesday => [new(hall, Min(15, 20) + J(5)), new(park, Min(17, 5) + J(5)), new(hall, Min(18, 25) + J(5)), new(orrin, Min(20, 52) + J(0)), new(hall, 1440, last ? Min(21, 6) : null)],
                    _ => [new(hall, Min(15, 20) + J(8)), new(park, Min(17, 0) + J(8)), new(hall, 1440)],
                };

            default:
                if (!weekday)
                {
                    return day.DayOfWeek == DayOfWeek.Saturday
                        ? [new(Eastgate, Min(11, 0) + J(15)), new(derby, Min(15, 30) + J(10)), new(Eastgate, 1440)]
                        : [new(Eastgate, Min(13, 40) + J(15)), new(park, Min(15, 20) + J(10)), new(Eastgate, 1440)];
                }

                return day.DayOfWeek switch
                {
                    DayOfWeek.Wednesday => [new(Eastgate, Min(9, 10)), new(work2, Min(16, 30)), new(RoadTripEnd, 1440, Min(20, 10))],
                    DayOfWeek.Friday => [new(Eastgate, Min(9, 10) + J(8)), new(work2, Min(17, 50) + J(10)), new(Eastgate, Min(22, 10) + J(5)), new(cemetery, Min(23, 20) + J(0)), new(Eastgate, 1440)],
                    _ => [new(Eastgate, Min(9, 10) + J(10)), new(work2, Min(17, 50) + J(10)), new(Eastgate, 1440)],
                };
        }
    }

    // The latest fix of a person: where the fixture's map says they were last heard, so the history ends where the map shows them.
    private static DateTimeOffset CapOf(DemoMember member) => member.Id switch
    {
        "queen" => DemoDataSource.Anchor.AddSeconds(-60),
        "jester" => DemoDataSource.Anchor.AddSeconds(-180),
        "cryptid" => DemoDataSource.Anchor.AddMinutes(-42),
        _ => DemoDataSource.Anchor,
    };

    // ---- the generator ---------------------------------------------------------------------------------------------------------------------------

    private Generated Generate(DemoMember member)
    {
        var fixes = new List<RawFix>();
        var trips = new List<StatsTrip>();
        var cap = CapOf(member);
        var entity = $"device_tracker.{member.Name.ToLowerInvariant()}_phone";
        for (var day = FirstDay; day <= LastDay; day = day.AddDays(1))
        {
            var rng = new DemoPrng($"{member.Id}:{HistoryDayMath.Iso(day)}:history");
            var midnight = HistoryDayMath.Bounds(day, _zone).StartUtc;
            var plan = Plan(member, day, rng);
            var arrive = midnight;
            for (var i = 0; i < plan.Count; i++)
            {
                var stop = plan[i];
                var leave = i == plan.Count - 1 ? midnight.AddMinutes(1440) : midnight.AddMinutes(stop.LeaveMin);
                if (i > 0)
                {
                    // The drive from the stop before.
                    var from = plan[i - 1].At;
                    var departed = midnight.AddMinutes(plan[i - 1].LeaveMin);
                    var drive = Drive(member, entity, rng, from, stop.At, departed, stop.ArriveMin is { } at ? midnight.AddMinutes(at) : null, stop.Open ? cap : null, plan[i - 1].At.Id, stop.At.Id);
                    fixes.AddRange(drive.Fixes);
                    if (drive.Trip is not null && drive.Trip.EndUtc <= cap)
                    {
                        trips.Add(drive.Trip);
                    }

                    arrive = drive.ArrivedUtc;
                    if (stop.Open)
                    {
                        break;
                    }

                    if (leave < arrive.AddMinutes(6))
                    {
                        leave = arrive.AddMinutes(6);
                    }
                }

                fixes.AddRange(Stationary(entity, rng, stop.At, arrive, leave, cap));
            }
        }

        return new Generated(
            [.. fixes.Where(fix => fix.Ts <= cap).OrderBy(fix => fix.Ts)],
            [.. trips.OrderBy(trip => trip.StartUtc)]);
    }

    // The heartbeat fixes of a stay: a minute after arriving, every half hour, and a minute before leaving.
    private static IEnumerable<RawFix> Stationary(string entity, DemoPrng rng, Pt at, DateTimeOffset arrive, DateTimeOffset leave, DateTimeOffset cap)
    {
        var end = leave.AddMinutes(-1);
        for (var t = arrive.AddMinutes(1); ; t += Heartbeat)
        {
            var when = t > end ? end : t;
            if (when > cap)
            {
                when = cap;
            }

            if (when >= arrive)
            {
                yield return Fix(entity, rng, at, when, speed: 0, driving: false);
            }

            if (t >= end || when >= cap)
            {
                yield break;
            }
        }
    }

    private static RawFix Fix(string entity, DemoPrng rng, Pt at, DateTimeOffset when, double speed, bool driving)
    {
        var meters = at.JitterM;
        var angle = rng.Next(360) * Math.PI / 180;
        var radius = meters * rng.Next(100) / 100d;
        var lat = at.Lat + (radius * Math.Cos(angle) / 111_320d);
        var lon = at.Lon + (radius * Math.Sin(angle) / (111_320d * Math.Cos(at.Lat * Math.PI / 180)));
        return new RawFix(
            EntityId: entity,
            Source: FixSource.Companion,
            Ts: when,
            Lat: lat,
            Lon: lon,
            AccuracyM: 10 + rng.Next(20),
            SpeedMps: speed,
            BatteryPct: 25 + rng.Next(70),
            Charging: false,
            Driving: driving,
            Address: at.Address);
    }

    private sealed record DriveResult(IReadOnlyList<RawFix> Fixes, StatsTrip? Trip, DateTimeOffset ArrivedUtc);

    // A drive between two points: a gently bent line walked in 30 second ticks with a smooth start and stop. A closed drive becomes a stored trip.
    private static DriveResult Drive(DemoMember member, string entity, DemoPrng rng, Pt from, Pt to, DateTimeOffset departed, DateTimeOffset? arrivedAt, DateTimeOffset? openUntil, string? fromId, string? toId)
    {
        var distance = Geo.DistanceM(from.Lat, from.Lon, to.Lat, to.Lon) * 1.18;
        var cruise = distance < 6_000 ? 11 + rng.Next(4) : distance < 30_000 ? 20 + rng.Next(6) : 24 + rng.Next(4);
        if (distance > 15_000 && rng.Next(3) == 0)
        {
            cruise = 35 + rng.Next(2);
        }

        var seconds = arrivedAt is { } known ? (arrivedAt.Value - departed).TotalSeconds : Math.Max(120, distance / (cruise * 0.82));
        var ticks = Math.Max(4, (int)Math.Ceiling(seconds / Tick.TotalSeconds));
        var arrived = departed.AddSeconds(ticks * Tick.TotalSeconds);
        var stopAt = openUntil ?? arrived;
        var bend = (rng.Next(2) == 0 ? 1 : -1) * 0.07 * distance / 111_320d;
        var dLat = to.Lat - from.Lat;
        var dLon = to.Lon - from.Lon;
        var norm = Math.Sqrt((dLat * dLat) + (dLon * dLon));
        var perpLat = norm == 0 ? 0 : -dLon / norm;
        var perpLon = norm == 0 ? 0 : dLat / norm;

        (double Lat, double Lon) PointAt(double f)
        {
            var eased = f * f * (3 - (2 * f));
            var wave = (Math.Sin(Math.PI * f) * bend) + (Math.Sin(2 * Math.PI * f) * bend / 3);
            return (from.Lat + (dLat * eased) + (perpLat * wave), from.Lon + (dLon * eased) + (perpLon * wave));
        }

        var fixes = new List<RawFix>();
        var previous = (Lat: from.Lat, Lon: from.Lon);
        var meters = 0d;
        var top = 0d;
        DateTimeOffset? topAt = null;
        var speeding = 0;
        for (var i = 0; i <= ticks; i++)
        {
            var when = departed.AddSeconds(i * Tick.TotalSeconds);
            if (when > stopAt)
            {
                break;
            }

            var point = i == ticks && openUntil is null ? (to.Lat, to.Lon) : PointAt((double)i / ticks);
            var step = i == 0 ? 0 : Geo.DistanceM(previous.Lat, previous.Lon, point.Item1, point.Item2);
            var speed = Math.Round(step / Tick.TotalSeconds, 1);
            meters += step;
            if (speed > top)
            {
                top = speed;
                topAt = when;
            }

            if (speed > SpeedingMps)
            {
                speeding++;
            }

            previous = (point.Item1, point.Item2);
            fixes.Add(new RawFix(
                EntityId: entity,
                Source: FixSource.Companion,
                Ts: when,
                Lat: point.Item1,
                Lon: point.Item2,
                AccuracyM: 6 + rng.Next(10),
                SpeedMps: speed,
                BatteryPct: 25 + rng.Next(70),
                Charging: false,
                Driving: true,
                Address: i >= ticks - 1 ? to.Address : i <= 1 ? from.Address : null));
        }

        if (openUntil is not null)
        {
            return new DriveResult(fixes, null, stopAt);
        }

        var phone = member.PhoneCapable ? (speeding > 0 ? 1 : rng.Next(4) == 0 ? 1 : 0) : (int?)null;
        var trip = new StatsTrip(
            MemberId: member.Id,
            StartUtc: departed,
            EndUtc: arrived,
            Meters: Math.Round(meters),
            Quality: TripQuality.Dense,
            DistanceBasis: DistanceBasis.Gps,
            TopSpeedMps: Math.Round(top, 1),
            TopSpeedAtUtc: topAt,
            TopSpeedStreet: member.Address == "I-65" && distance > 15_000 ? "I-65" : null,
            SpeedingCount: speeding,
            PhoneCount: phone,
            StartPlaceId: fromId,
            EndPlaceId: toId,
            StartStreet: null,
            EndStreet: null,
            StartLat: from.Lat,
            StartLon: from.Lon,
            EndLat: to.Lat,
            EndLon: to.Lon);
        return new DriveResult(fixes, trip, arrived);
    }
}
