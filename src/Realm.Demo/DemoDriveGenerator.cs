using Realm.Domain;

namespace Realm.Demo;

/// <summary>
/// Builds one driver's drive list for one week (02 section 9.4, constraints (a) to (e)). Everything is integer arithmetic
/// on a seeded <see cref="DemoPrng"/>, so the same spec always gives the same list.
/// </summary>
internal static class DemoDriveGenerator
{
    // (a) each drive is at least 0.3 mi and at most 160 mi.
    private const int MinTenths = 3;
    private const int MaxTenths = 1_600;

    // (b) a drive with speeding needs a top speed of at least 80 mph (the default threshold); one with phone use at least 10 mph.
    private const int SpeedingMph = 80;
    private const int PhoneMph = 10;

    // A drive that is neither the week's top speed nor a speeding drive tops out between these.
    private const int SlowFloorMph = 35;
    private const int SlowCeilingMph = 79;

    // (c) starts on a 5-minute grid between 06:30 and 22:00, with a gap of 5 minutes between two drives of one driver.
    private const int DayMinutes = 24 * 60;
    private const int FirstStartMinute = (6 * 60) + 30;
    private const int LastStartMinute = 22 * 60;
    private const int GridMinutes = 5;
    private const int GapMinutes = 5;

    private static readonly string[] PlaceNames = [.. DemoPlaces.Drawn.Select(place => place.Name)];

    /// <summary>The drives of <paramref name="spec"/>, newest first.</summary>
    public static IReadOnlyList<DemoGeneratedDrive> Generate(DemoDriveSpec spec)
    {
        var rng = new DemoPrng(spec.MemberId + spec.WeekStartIso);
        var tops = DrawTopSpeeds(spec, rng);
        var tenths = DrawDistances(spec, rng, tops);
        var events = DrawEvents(spec, rng, tops);
        var (starts, ends) = Schedule(spec, rng, tenths);
        var (froms, tos) = Label(spec, rng, starts);

        return
        [
            .. Enumerable.Range(0, spec.Count)
                .OrderByDescending(index => starts[index])
                .Select(index => new DemoGeneratedDrive(
                    starts[index],
                    ends[index],
                    froms[index],
                    tos[index],
                    tenths[index],
                    tops[index],
                    new DemoEventCounts(events[0][index], events[1][index], events[2][index], events[3][index]))),
        ];
    }

    // Exactly one drive reaches the week's top speed (a fixed one if the spec states it, else a drawn one); a few others are
    // fast enough to speed (about one in four speeding events needs its own drive); the rest stay below the threshold.
    private static int[] DrawTopSpeeds(DemoDriveSpec spec, DemoPrng rng)
    {
        var fixedCount = spec.Fixed.Count;
        var generatedCount = spec.Count - fixedCount;
        if (generatedCount < 0)
        {
            throw new InvalidOperationException($"{spec.MemberId}: {fixedCount} fixed drives for {spec.Count} drives.");
        }

        var tops = new int[spec.Count];
        var peak = -1;
        for (var index = 0; index < fixedCount && peak < 0; index++)
        {
            if (spec.Fixed[index].TopMph == spec.TopMph)
            {
                peak = index;
            }
        }

        if (peak < 0)
        {
            if (generatedCount == 0)
            {
                throw new InvalidOperationException($"{spec.MemberId}: no drive can carry the top speed.");
            }

            peak = fixedCount + rng.Next(generatedCount);
        }

        tops[peak] = spec.TopMph;
        var others = Enumerable.Range(fixedCount, generatedCount).Where(index => index != peak).ToList();
        Shuffle(others, rng);
        var wanted = Math.Max(1, (spec.Counts.Speeding.GetValueOrDefault() + 3) / 4);
        var fast = spec.TopMph > SpeedingMph ? Math.Min(wanted - 1, others.Count) : 0;
        var slowCeiling = Math.Min(SlowCeilingMph, spec.TopMph - 1);
        for (var k = 0; k < others.Count; k++)
        {
            tops[others[k]] = k < fast
                ? SpeedingMph + rng.Next(spec.TopMph - SpeedingMph)
                : SlowFloorMph + rng.Next(slowCeiling - SlowFloorMph + 1);
        }

        for (var index = 0; index < fixedCount; index++)
        {
            if (index != peak)
            {
                tops[index] = spec.Fixed[index].TopMph ?? (SlowFloorMph + rng.Next(slowCeiling - SlowFloorMph + 1));
            }
        }

        return tops;
    }

    // The week's tenths of a mile, shared out by largest remainder over the drives whose distance is not stated; faster
    // drives get twice the weight, so a 96 mph drive is not a one-mile hop.
    private static int[] DrawDistances(DemoDriveSpec spec, DemoPrng rng, int[] tops)
    {
        var tenths = new int[spec.Count];
        var free = new List<int>();
        var stated = 0;
        for (var index = 0; index < spec.Count; index++)
        {
            var fixedTenths = index < spec.Fixed.Count ? spec.Fixed[index].Tenths : null;
            if (fixedTenths is int value)
            {
                tenths[index] = value;
                stated += value;
            }
            else
            {
                free.Add(index);
            }
        }

        var remaining = spec.TotalTenths - stated - (MinTenths * free.Count);
        if (remaining < 0 || (free.Count == 0 && remaining != 0))
        {
            throw new InvalidOperationException($"{spec.MemberId}: {spec.TotalTenths} tenths do not fit {spec.Count} drives.");
        }

        var weights = free.Select(index => (50 + rng.Next(100)) * (tops[index] >= SpeedingMph ? 2 : 1)).ToArray();
        var shares = Apportion(remaining, weights);
        for (var k = 0; k < free.Count; k++)
        {
            tenths[free[k]] = MinTenths + shares[k];
            if (tenths[free[k]] > MaxTenths)
            {
                throw new InvalidOperationException($"{spec.MemberId}: a drive of {tenths[free[k]]} tenths exceeds 160 mi.");
            }
        }

        return tenths;
    }

    // Each kind's weekly count is shared out by largest remainder over the drives that can carry it (speeding needs a fast
    // drive, phone use a drive above 10 mph); a stated phone count is taken off first.
    private static int[][] DrawEvents(DemoDriveSpec spec, DemoPrng rng, int[] tops)
    {
        var result = new int[DemoEventCounts.Keys.Count][];
        for (var kind = 0; kind < result.Length; kind++)
        {
            var key = DemoEventCounts.Keys[kind];
            var total = spec.Counts.Get(key).GetValueOrDefault();
            var perDrive = new int[spec.Count];
            var stated = new bool[spec.Count];
            for (var index = 0; index < spec.Fixed.Count; index++)
            {
                if (key == EventKeys.Phone && spec.Fixed[index].Phone is int pinned)
                {
                    perDrive[index] = pinned;
                    stated[index] = true;
                    total -= pinned;
                }
            }

            var minimumMph = key switch
            {
                EventKeys.Speeding => SpeedingMph,
                EventKeys.Phone => PhoneMph,
                _ => 0,
            };
            var eligible = Enumerable.Range(0, spec.Count).Where(index => !stated[index] && tops[index] >= minimumMph).ToList();
            var weights = eligible.Select(_ => 1 + rng.Next(4)).ToArray();
            if (total < 0 || (eligible.Count == 0 && total != 0))
            {
                throw new InvalidOperationException($"{spec.MemberId}: {key} count {total} cannot be placed.");
            }

            var shares = Apportion(total, weights);
            for (var k = 0; k < eligible.Count; k++)
            {
                perDrive[eligible[k]] = shares[k];
            }

            result[kind] = perDrive;
        }

        return result;
    }

    // Fixed drives keep their stated times; each generated drive takes the first free start on the grid from a drawn position,
    // inside the window, inside one day, with a 5-minute gap to every other drive. Duration is distance / 28 mph + 4 minutes.
    private static (int[] Starts, int[] Ends) Schedule(DemoDriveSpec spec, DemoPrng rng, int[] tenths)
    {
        var starts = new int[spec.Count];
        var ends = new int[spec.Count];
        var placed = new List<(int Start, int End)>();
        for (var index = 0; index < spec.Fixed.Count; index++)
        {
            var stated = spec.Fixed[index];
            starts[index] = stated.StartMin;
            ends[index] = stated.EndMin ?? (stated.StartMin + Duration(tenths[index]));
            placed.Add((starts[index], ends[index]));
        }

        var slotsPerDay = ((LastStartMinute - FirstStartMinute) / GridMinutes) + 1;
        var slots = ((spec.WindowEndMin / DayMinutes) + 1) * slotsPerDay;
        for (var index = spec.Fixed.Count; index < spec.Count; index++)
        {
            var duration = Duration(tenths[index]);
            var begin = rng.Next(slots);
            var found = false;
            for (var attempt = 0; attempt < slots && !found; attempt++)
            {
                var position = (begin + attempt) % slots;
                var start = ((position / slotsPerDay) * DayMinutes) + FirstStartMinute + ((position % slotsPerDay) * GridMinutes);
                var end = start + duration;
                var free = start >= spec.WindowStartMin
                    && end <= spec.WindowEndMin
                    && end < (((start / DayMinutes) + 1) * DayMinutes)
                    && !placed.Any(other => start < other.End + GapMinutes && other.Start < end + GapMinutes);
                if (free)
                {
                    starts[index] = start;
                    ends[index] = end;
                    placed.Add((start, end));
                    found = true;
                }
            }

            if (!found)
            {
                throw new InvalidOperationException($"{spec.MemberId}: no free slot for drive {index}.");
            }
        }

        return (starts, ends);
    }

    // In time order, a drive that starts on the day of the one before it starts where that one ended (02 section 9.4 (e)); the first
    // drive of a day starts at home. A generated drive ends at a place drawn from the demo places, never where it started.
    private static (string[] Froms, string[] Tos) Label(DemoDriveSpec spec, DemoPrng rng, int[] starts)
    {
        var froms = new string[spec.Count];
        var tos = new string[spec.Count];
        var order = Enumerable.Range(0, spec.Count).OrderBy(index => starts[index]).ToArray();
        for (var k = 0; k < order.Length; k++)
        {
            var index = order[k];
            var stated = index < spec.Fixed.Count ? spec.Fixed[index] : null;
            var sameDay = k > 0 && starts[order[k - 1]] / DayMinutes == starts[index] / DayMinutes;
            froms[index] = stated?.From ?? (sameDay ? tos[order[k - 1]] : spec.HomeLabel);
            tos[index] = stated?.To ?? PickPlace(rng, froms[index]);
        }

        return (froms, tos);
    }

    // Distance / 28 mph + 4 minutes, in whole minutes (tenths * 6 / 28 rounded half up).
    private static int Duration(int tenths) => (((tenths * 6) + 14) / 28) + 4;

    private static string PickPlace(DemoPrng rng, string from)
    {
        var index = rng.Next(PlaceNames.Length);
        return PlaceNames[index] == from ? PlaceNames[(index + 1) % PlaceNames.Length] : PlaceNames[index];
    }

    private static void Shuffle(List<int> items, DemoPrng rng)
    {
        for (var index = items.Count - 1; index > 0; index--)
        {
            var other = rng.Next(index + 1);
            (items[index], items[other]) = (items[other], items[index]);
        }
    }

    // Largest-remainder apportionment of a whole number over integer weights; ties go to the lower index.
    private static int[] Apportion(int total, int[] weights)
    {
        var shares = new int[weights.Length];
        if (total == 0)
        {
            return shares;
        }

        if (weights.Length == 0)
        {
            throw new InvalidOperationException("Nothing to share the total with.");
        }

        var sum = weights.Sum(weight => (long)weight);
        var remainders = new long[weights.Length];
        var assigned = 0;
        for (var index = 0; index < weights.Length; index++)
        {
            var scaled = (long)total * weights[index];
            shares[index] = (int)(scaled / sum);
            remainders[index] = scaled % sum;
            assigned += shares[index];
        }

        var winners = Enumerable.Range(0, weights.Length)
            .OrderByDescending(index => remainders[index])
            .ThenBy(index => index)
            .Take(total - assigned);
        foreach (var winner in winners)
        {
            shares[winner]++;
        }

        return shares;
    }
}
