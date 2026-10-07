namespace Realm.Domain;

/// <summary>
/// The weekly driving statistics as pure rules (02 sections 6.1 to 6.8): what is counted, null versus 0,
/// the like-for-like comparator and trend, coverage, top speed and its ties. The caller reads the trips and
/// the members' recording starts; nothing here reads a clock or a database. Metres are summed exactly and
/// never rounded (the UI rounds once, for display).
/// </summary>
public static class StatsRules
{
    /// <summary>The note of the speeding event type: the count is sampled and under-counts by design (02 section 5.8, D39).</summary>
    public const string SpeedingNote = "Counted from ~42 s samples; short bursts are missed";

    private static readonly string[] EventKeyOrder = [EventKeys.Speeding, EventKeys.Phone, EventKeys.Accel, EventKeys.Braking];

    /// <summary>
    /// The window the trips of week <paramref name="weekOffset"/> are compared with (02 section 6.4). An older
    /// week is compared with the whole week before it. The current week is compared like for like: from the
    /// start of the previous week for as much local wall-clock time as has elapsed in this one, so a DST change
    /// does not skew it.
    /// </summary>
    public static TimeWindow ComparatorWindow(DateTimeOffset now, DayOfWeek weekStart, TimeZoneInfo zone, int weekOffset)
    {
        var start = WeekMath.StartUtc(now, weekStart, zone, weekOffset + 1);
        if (weekOffset >= 1)
        {
            return new TimeWindow(start, WeekMath.EndUtc(now, weekStart, zone, weekOffset + 1));
        }

        var localNow = TimeZoneInfo.ConvertTime(now, zone).DateTime;
        var thisWeekStartLocal = TimeZoneInfo.ConvertTime(WeekMath.StartUtc(now, weekStart, zone, 0), zone).DateTime;
        var previousWeekStartLocal = TimeZoneInfo.ConvertTime(start, zone).DateTime;
        var end = LocalToUtc(previousWeekStartLocal + (localNow - thisWeekStartLocal), zone);
        return new TimeWindow(start, end);
    }

    /// <summary>
    /// Builds the report of one week for the report drivers. <paramref name="trips"/> must hold the valid trips
    /// of the week and of its comparator window; trips of other members or outside both windows are ignored.
    /// </summary>
    public static WeekReportVm WeekReport(
        DateTimeOffset now,
        DayOfWeek weekStart,
        TimeZoneInfo zone,
        int weekOffset,
        IReadOnlyList<StatsMember> members,
        IReadOnlyList<StatsTrip> trips)
    {
        var week = WeekMath.Week(now, weekStart, zone, weekOffset);
        var current = new TimeWindow(WeekMath.StartUtc(now, weekStart, zone, weekOffset), WeekMath.EndUtc(now, weekStart, zone, weekOffset));
        var comparator = ComparatorWindow(now, weekStart, zone, weekOffset);

        var drivers = members.Select(m => Evaluate(m, current, comparator, trips)).ToList();
        var ordered = drivers
            .OrderByDescending(d => d.Summary.Drives ?? -1)
            .ThenByDescending(d => d.Summary.Meters ?? -1)
            .ThenBy(d => d.Member.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(d => d.Member.MemberId, StringComparer.Ordinal)
            .ToList();

        var covered = ordered.Where(d => d.Summary.Covered).ToList();
        // Partial means recording began mid-week for a covered driver; a driver who is not covered says nothing about it.
        var coverage = covered.Count == 0
            ? WeekCoverage.NoRecord
            : covered.Any(d => d.Summary.CoverageStartUtc is not null) ? WeekCoverage.Partial : WeekCoverage.Full;

        var events = EventKeyOrder.ToDictionary(key => key, key => EventStatOf(key, ordered), StringComparer.Ordinal);
        var top = TopSpeedOf(ordered);
        var totals = new WeekTotals(covered.Sum(d => d.Summary.Drives ?? 0), covered.Sum(d => d.Summary.Meters ?? 0));
        var bases = covered.SelectMany(d => d.Trips).Select(t => t.DistanceBasis).Distinct().ToList();

        return new WeekReportVm(
            Start: week.Start,
            End: week.End,
            IsCurrent: weekOffset == 0,
            Coverage: coverage,
            DistanceBasis: bases.Count > 1 ? DistanceBasis.Mixed : bases.Count == 1 ? bases[0] : DistanceBasis.Gps,
            Events: events,
            TopSpeed: top,
            Totals: totals,
            Drivers: ordered.Select(d => d.Summary).ToList());
    }

    /// <summary>
    /// One driver's week: the summary of the driver card and the week's trips, newest first (02 section 6.7).
    /// Null for a member who is not a report driver. A driver who is not covered for the week gets a summary
    /// with Covered false, null counts and no trips.
    /// </summary>
    /// <param name="placeNames">The display names of the zones that still exist, by place id: a deleted zone falls back to the street.</param>
    public static DriverWeek? DriverWeekOf(
        DateTimeOffset now,
        DayOfWeek weekStart,
        TimeZoneInfo zone,
        int weekOffset,
        string memberId,
        IReadOnlyList<StatsMember> members,
        IReadOnlyList<StatsTrip> trips,
        IReadOnlyDictionary<string, string> placeNames)
    {
        var member = members.FirstOrDefault(m => string.Equals(m.MemberId, memberId, StringComparison.Ordinal));
        if (member is null)
        {
            return null;
        }

        var current = new TimeWindow(WeekMath.StartUtc(now, weekStart, zone, weekOffset), WeekMath.EndUtc(now, weekStart, zone, weekOffset));
        var driver = Evaluate(member, current, ComparatorWindow(now, weekStart, zone, weekOffset), trips);
        var drives = driver.Trips
            .OrderByDescending(t => t.StartUtc)
            .Select(t => new DriveVm(
                StartUtc: t.StartUtc,
                EndUtc: t.EndUtc,
                FromLabel: Label(t.StartPlaceId, t.StartStreet, placeNames),
                ToLabel: Label(t.EndPlaceId, t.EndStreet, placeNames),
                Meters: t.Meters,
                TopSpeedMps: t.Quality == TripQuality.Coarse ? null : t.TopSpeedMps,
                Events: EventCounts(t.SpeedingCount, t.PhoneCount)))
            .ToList();
        return new DriverWeek(driver.Summary, drives);
    }

    // ---- one driver ---------------------------------------------------------------------------------------------

    private sealed record Figures(
        int Drives,
        double Meters,
        DistanceBasis Basis,
        int Coarse,
        int? Speeding,
        int? Phone,
        StatsTrip? TopTrip);

    private sealed record Driver(
        StatsMember Member,
        DriverSummary Summary,
        IReadOnlyList<StatsTrip> Trips,
        Figures? Figures,
        Figures? ComparatorFigures);

    private static Driver Evaluate(StatsMember member, TimeWindow week, TimeWindow comparator, IReadOnlyList<StatsTrip> all)
    {
        var mine = all.Where(t => string.Equals(t.MemberId, member.MemberId, StringComparison.Ordinal)).ToList();

        // A driver is covered when recording began before the week ended; a half-recorded week is reported as is.
        var covered = member.RecordingStart is { } start && start < week.EndUtc;
        if (!covered)
        {
            // Not covered: nothing is known, so every count is null (never 0). The comparator window is older still.
            return new Driver(
                member,
                new DriverSummary(member.MemberId, null, null, DistanceBasis.Gps, 0, member.PhoneCapable, EventCounts(null, null), null, false, false, null),
                [],
                null,
                null);
        }

        var inWeek = mine.Where(t => t.StartUtc >= week.StartUtc && t.StartUtc < week.EndUtc).ToList();
        var figures = FiguresOf(member, inWeek);

        // The comparator is unknown when its window starts before recording did: it must not look like an improvement.
        Figures? comparatorFigures = member.RecordingStart <= comparator.StartUtc
            ? FiguresOf(member, mine.Where(t => t.StartUtc >= comparator.StartUtc && t.StartUtc < comparator.EndUtc).ToList())
            : null;

        int? eventsTotal = figures.Speeding is null && figures.Phone is null ? null : (figures.Speeding ?? 0) + (figures.Phone ?? 0);
        var summary = new DriverSummary(
            MemberId: member.MemberId,
            Drives: figures.Drives,
            Meters: figures.Meters,
            DistanceBasis: figures.Basis,
            CoarseTrips: figures.Coarse,
            PhoneCapable: member.PhoneCapable,
            Events: EventCounts(figures.Speeding, figures.Phone),
            EventsTotal: eventsTotal,
            EventsPartial: figures.Coarse > 0,
            Covered: true,
            CoverageStartUtc: member.RecordingStart > week.StartUtc ? member.RecordingStart : null);
        return new Driver(member, summary, inWeek, figures, comparatorFigures);
    }

    // The counting rules of 02 section 6.2 and 6.3 for one driver over one window of trips.
    private static Figures FiguresOf(StatsMember member, IReadOnlyList<StatsTrip> trips)
    {
        var dense = trips.Where(t => t.Quality == TripQuality.Dense).ToList();
        var coarse = trips.Count - dense.Count;
        var bases = trips.Select(t => t.DistanceBasis).Distinct().ToList();

        int? speeding;
        int? phone;
        if (trips.Count == 0)
        {
            // Covered and no trips: real zeros, except that phone use is only ever 0 for a phone-capable driver.
            speeding = 0;
            phone = member.PhoneCapable ? 0 : null;
        }
        else
        {
            // Speeding is lenient: the sum of the counts that exist over dense trips, null if there are none.
            var counted = dense.Where(t => t.SpeedingCount is not null).ToList();
            speeding = counted.Count == 0 ? null : counted.Sum(t => t.SpeedingCount!.Value);

            // Phone use is strict: null for a driver who cannot record it, and for a week in which any dense trip
            // has no count (it predates the sensor), because a sum over the rest would understate it.
            phone = !member.PhoneCapable || dense.Count == 0 || dense.Any(t => t.PhoneCount is null)
                ? null
                : dense.Sum(t => t.PhoneCount!.Value);
        }

        var top = dense
            .Where(t => t.TopSpeedMps is not null)
            .OrderByDescending(t => Math.Round(t.TopSpeedMps!.Value, 2))
            .ThenBy(t => t.TopSpeedAtUtc ?? t.StartUtc)
            .FirstOrDefault();

        return new Figures(
            Drives: trips.Count,
            Meters: trips.Sum(t => t.Meters),
            Basis: bases.Count > 1 ? DistanceBasis.Mixed : bases.Count == 1 ? bases[0] : DistanceBasis.Gps,
            Coarse: coarse,
            Speeding: speeding,
            Phone: phone,
            TopTrip: top);
    }

    private static IReadOnlyDictionary<string, int?> EventCounts(int? speeding, int? phone) =>
        new Dictionary<string, int?>(StringComparer.Ordinal)
        {
            [EventKeys.Speeding] = speeding,
            [EventKeys.Phone] = phone,
            [EventKeys.Accel] = null,
            [EventKeys.Braking] = null,
        };

    private static string? Label(string? placeId, string? street, IReadOnlyDictionary<string, string> placeNames) =>
        placeId is not null && placeNames.TryGetValue(placeId, out var name) ? name : street;

    // ---- one event type across drivers (02 section 6.4) -----------------------------------------------------------

    private static int? CountOf(Figures? figures, string key) => key switch
    {
        EventKeys.Speeding => figures?.Speeding,
        EventKeys.Phone => figures?.Phone,
        _ => null,
    };

    private static EventStat EventStatOf(string key, IReadOnlyList<Driver> drivers)
    {
        var counts = drivers
            .Select(d => new EventDriverCount(d.Member.MemberId, CountOf(d.Figures, key), CountOf(d.ComparatorFigures, key)))
            .ToList();
        var present = counts.Where(c => c.Count is not null).ToList();

        // A driver who is not covered has no count because nothing is known, not because they did not share.
        var coveredDrivers = drivers.Count(d => d.Summary.Covered);

        int? total = present.Count == 0 ? null : present.Sum(c => c.Count!.Value);

        // The comparator total covers the same drivers as the total, and exists only if every one of them has comparator data.
        int? comparatorTotal = total is not null && present.All(c => c.ComparatorCount is not null)
            ? present.Sum(c => c.ComparatorCount!.Value)
            : null;

        // The trend uses only the drivers with a value in both periods.
        var both = counts.Where(c => c.Count is not null && c.ComparatorCount is not null).ToList();
        int? trend = both.Count == 0 ? null : both.Sum(c => c.Count!.Value) - both.Sum(c => c.ComparatorCount!.Value);

        var availability = key switch
        {
            EventKeys.Speeding => EventAvailability.All,
            EventKeys.Phone => EventAvailability.Some,
            _ => EventAvailability.None,
        };

        return new EventStat(
            Total: total,
            ComparatorTotal: comparatorTotal,
            TrendDelta: trend,
            Source: StatSource.Derived,
            Availability: availability,
            Partial: availability != EventAvailability.None && present.Count > 0 && present.Count < coveredDrivers,
            Note: key == EventKeys.Speeding ? SpeedingNote : null,
            Drivers: counts,
            CoveredCount: coveredDrivers);
    }

    // ---- top speed (02 section 6.8) --------------------------------------------------------------------------------

    private static TopSpeedStat? TopSpeedOf(IReadOnlyList<Driver> drivers)
    {
        var ranked = drivers
            .Where(d => d.Figures?.TopTrip is not null)
            .Select(d => (Driver: d, Trip: d.Figures!.TopTrip!))
            .OrderByDescending(x => Math.Round(x.Trip.TopSpeedMps!.Value, 2))
            .ThenBy(x => x.Trip.TopSpeedAtUtc ?? x.Trip.StartUtc)
            .ThenBy(x => x.Driver.Member.MemberId, StringComparer.Ordinal)
            .ToList();
        if (ranked.Count == 0)
        {
            return null;
        }

        var best = ranked[0];

        return new TopSpeedStat(
            MemberId: best.Driver.Member.MemberId,
            SpeedMps: best.Trip.TopSpeedMps!.Value,
            AtUtc: best.Trip.TopSpeedAtUtc ?? best.Trip.StartUtc,
            Street: best.Trip.TopSpeedStreet,
            Drivers: drivers.Select(d => new DriverTopSpeed(d.Member.MemberId, d.Figures?.TopTrip?.TopSpeedMps)).ToList());
    }

    // ---- local time -----------------------------------------------------------------------------------------------

    // A local wall-clock time as a UTC instant. A time that does not exist (a DST gap) moves to the next valid
    // one; a time that happens twice is the first occurrence. The same rule WeekMath uses for local midnight.
    private static DateTimeOffset LocalToUtc(DateTime local, TimeZoneInfo zone)
    {
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        while (zone.IsInvalidTime(unspecified))
        {
            unspecified = unspecified.AddMinutes(1);
        }

        var offset = zone.IsAmbiguousTime(unspecified)
            ? zone.GetAmbiguousTimeOffsets(unspecified).Max()
            : zone.GetUtcOffset(unspecified);
        return new DateTimeOffset(unspecified, offset).ToUniversalTime();
    }
}
