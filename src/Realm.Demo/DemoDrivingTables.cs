using Realm.Domain;

namespace Realm.Demo;

/// <summary>
/// The driving numbers of 02 section 9.4: the base data (the all-sources dataset), week 0 comparators and the drives the
/// fixture states outright. 02 owns every number here; the default fixture and the variants are derived from this table
/// by <see cref="DemoDrivingData"/>.
/// </summary>
internal static class DemoDrivingTables
{
    /// <summary>A tenth of a mile in metres (1609.344 / 10); distances are held in tenths so sums stay exact.</summary>
    public const double TenthMileMetres = 160.9344;

    /// <summary>The street string of the king's week-0 top speed (02 section 6.6 and 9.4 (d)).</summary>
    public const string TopSpeedStreet = "I-35";

    /// <summary>Minutes in a week; drives of weeks 1 to 3 end before Sunday 23:59 local (02 section 9.4 (f)).</summary>
    public const int WeekEndMin = (7 * 24 * 60) - 1;

    // Per week, in cast order king, queen, jester, cryptid: drives, tenths of a mile, top mph, then the all-sources counts
    // speeding, phone, rapid acceleration, hard braking (02 section 9.4: the week 0 table and the weeks 1 to 3 splits).
    private static readonly (DemoMember Driver, int Drives, int Tenths, int TopMph, int Speeding, int Phone, int Accel, int Braking)[][] Weeks =
    [
        [
            (DemoCast.King, 22, 944, 96, 6, 60, 3, 0),
            (DemoCast.Queen, 10, 1182, 82, 2, 31, 1, 1),
            (DemoCast.Jester, 18, 2026, 88, 38, 115, 11, 3),
            (DemoCast.Cryptid, 14, 3660, 84, 10, 44, 3, 1),
        ],
        [
            (DemoCast.King, 24, 1013, 91, 5, 64, 3, 0),
            (DemoCast.Queen, 11, 1265, 82, 3, 33, 1, 1),
            (DemoCast.Jester, 20, 2152, 92, 41, 142, 8, 5),
            (DemoCast.Cryptid, 16, 3996, 83, 14, 62, 3, 3),
        ],
        [
            (DemoCast.King, 20, 928, 94, 4, 70, 2, 1),
            (DemoCast.Queen, 10, 1204, 81, 2, 36, 2, 1),
            (DemoCast.Jester, 19, 1903, 86, 36, 150, 8, 6),
            (DemoCast.Cryptid, 17, 3978, 81, 16, 66, 3, 3),
        ],
        [
            (DemoCast.King, 18, 851, 88, 4, 61, 3, 0),
            (DemoCast.Queen, 9, 997, 81, 1, 30, 1, 1),
            (DemoCast.Jester, 17, 1764, 90, 27, 133, 10, 5),
            (DemoCast.Cryptid, 14, 3417, 82, 12, 66, 3, 2),
        ],
    ];

    // Week 0 comparators (like-for-like to last Wednesday 21:25), speeding, phone, accel, braking; they sum to 49, 274, 12, 7.
    private static readonly Dictionary<string, DemoEventCounts> Week0Comparators = new(StringComparer.Ordinal)
    {
        [DemoCast.King.Id] = new(5, 71, 2, 0),
        [DemoCast.Queen.Id] = new(3, 33, 1, 1),
        [DemoCast.Jester.Id] = new(29, 120, 6, 4),
        [DemoCast.Cryptid.Id] = new(12, 50, 3, 2),
    };

    // When each driver's last completed drive of week 0 ends: the "since" time of the snapshot (02 section 9.3), so a
    // member the snapshot has at home since 17:52 does not have a drive after it. All are before Wed 21:20 (9.4 (c)).
    private static readonly Dictionary<string, int> Week0LatestEnd = new(StringComparer.Ordinal)
    {
        [DemoCast.King.Id] = Minute(2, 17, 52),
        [DemoCast.Queen.Id] = Minute(2, 21, 12),
        [DemoCast.Jester.Id] = Minute(2, 21, 6),
        [DemoCast.Cryptid.Id] = Minute(2, 20, 10),
    };

    /// <summary>The drivers of the report: every member with a live tracker (the static prince is not one).</summary>
    public static IReadOnlyList<DemoMember> ReportDrivers { get; } = [.. DemoCast.Members.Where(member => member.Kind == MemberKind.Live)];

    /// <summary>Minutes after Monday 00:00 local: day 0 is Monday.</summary>
    public static int Minute(int day, int hour, int minute) => (((day * 24) + hour) * 60) + minute;

    /// <summary>The base row (all-sources, covered) of a driver and week 0 to 3, with its comparators.</summary>
    public static DemoDriverWeekRow Row(int week, DemoMember driver)
    {
        var entry = Entry(week, driver);
        return new DemoDriverWeekRow(
            driver,
            Covered: true,
            CoverageStartUtc: null,
            entry.Drives,
            entry.Tenths,
            entry.TopMph,
            Counts(entry),
            Comparators(week, driver));
    }

    /// <summary>The latest minute a drive of the driver's week 0 may end.</summary>
    public static int LatestEndMin(int week, DemoMember driver) => week == 0 ? Week0LatestEnd[driver.Id] : WeekEndMin;

    /// <summary>The drives of the driver's week that the fixture states outright (02 section 9.4 (d)); only week 0 has any.</summary>
    public static IReadOnlyList<DemoFixedDrive> FixedDrives(int week, DemoMember driver)
    {
        if (week != 0)
        {
            return [];
        }

        if (driver.Id == DemoCast.King.Id)
        {
            return
            [
                // Tue 16:12, the week's top speed; its distance and labels come from the generator.
                new DemoFixedDrive(Minute(1, 16, 12), null, null, null, null, 96),
                new DemoFixedDrive(Minute(2, 8, 4), Minute(2, 8, 21), DemoPlaces.Home.Name, DemoPlaces.Work.Name, 72, null),
                // The 02 section 6.7 example row: Work to Hearth Haven, 7.2 mi, top 62 mph, no speeding, 2 phone events.
                new DemoFixedDrive(Minute(2, 17, 31), Minute(2, 17, 52), DemoPlaces.Work.Name, DemoPlaces.Home.Name, 72, 62, Phone: 2),
            ];
        }

        if (driver.Id == DemoCast.Jester.Id)
        {
            // The newest row of the jester's list: 1.1 mi, top 38 mph, so his other 17 drives sum to 201.5 mi.
            return [new DemoFixedDrive(Minute(2, 21, 1), Minute(2, 21, 6), DemoPlaces.Home.Name, DemoPlaces.JesterHall.Name, 11, 38)];
        }

        return [];
    }

    /// <summary>The street of the week's top speed fix; only the king's week 0 top speed has one (02 section 6.6).</summary>
    public static string? TopSpeedStreetOf(int week, DemoMember driver) =>
        week == 0 && driver.Id == DemoCast.King.Id ? TopSpeedStreet : null;

    private static (DemoMember Driver, int Drives, int Tenths, int TopMph, int Speeding, int Phone, int Accel, int Braking) Entry(int week, DemoMember driver) =>
        Weeks[week].Single(entry => entry.Driver.Id == driver.Id);

    private static DemoEventCounts Counts((DemoMember Driver, int Drives, int Tenths, int TopMph, int Speeding, int Phone, int Accel, int Braking) entry) =>
        new(entry.Speeding, entry.Phone, entry.Accel, entry.Braking);

    // Week 0 has its own like-for-like figures; weeks 1 and 2 compare with the next older week in full; week 3 has no older week.
    private static DemoEventCounts Comparators(int week, DemoMember driver) => week switch
    {
        0 => Week0Comparators[driver.Id],
        < WeekMath.ChipCount - 1 => Counts(Entry(week + 1, driver)),
        _ => DemoEventCounts.NotRecorded,
    };
}
