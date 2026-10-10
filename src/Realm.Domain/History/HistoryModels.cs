namespace Realm.Domain;

/// <summary>
/// One line of a member's day in Location History (0.3.0, D123): a place visit (<see cref="HistoryStay"/>) or a drive (<see cref="HistoryDrive"/>). The id is stable for a given piece
/// of stored data (<c>s-</c> or <c>d-</c> and the start in Unix milliseconds), so a list can keep its selection across a reload.
/// </summary>
/// <param name="Id">Unique inside the day; safe in an HTML id and a test id.</param>
/// <param name="StartUtc">When the visit began (clipped to the start of the day) or the drive started.</param>
/// <param name="EndUtc">When the visit ended (clipped to the end of the day, or the clock for the visit that is still going on) or the drive ended.</param>
public abstract record HistoryEntry(string Id, DateTimeOffset StartUtc, DateTimeOffset EndUtc)
{
    /// <summary>The length of the entry.</summary>
    public TimeSpan Duration => EndUtc > StartUtc ? EndUtc - StartUtc : TimeSpan.Zero;
}

/// <summary>A place visit: the member stayed at one place for at least the minimum stay (<see cref="StayOptions.MinDuration"/>).</summary>
/// <param name="Label">The zone name, else the city or street near it ("Pinebrook", "near Pinebrook", "I-65 near Pinebrook"), else "near {nearest zone}": never "unknown" (<see cref="PlaceLabeler"/>).</param>
/// <param name="PlaceId">The id of the zone the visit was at; null when it was at no zone.</param>
/// <param name="Lat">The zone's centre, or the middle of the fixes of a visit at no zone.</param>
/// <param name="ContinuesFromPreviousDay">The visit began before this day: <paramref name="StartUtc"/> is the start of the day.</param>
/// <param name="ContinuesIntoNextDay">The visit goes on after this day: <paramref name="EndUtc"/> is the end of the day.</param>
/// <param name="IsOngoing">The member is there now (the last visit of the last stored data, no later than the clock): <paramref name="EndUtc"/> is the clock.</param>
public sealed record HistoryStay(
    string Id,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    string Label,
    string? PlaceId,
    double Lat,
    double Lon,
    bool ContinuesFromPreviousDay,
    bool ContinuesIntoNextDay,
    bool IsOngoing) : HistoryEntry(Id, StartUtc, EndUtc);

/// <summary>A drive of the day (a stored trip that started in the day).</summary>
/// <param name="FromLabel">Named like a stay (<see cref="PlaceLabeler"/>).</param>
/// <param name="TopSpeedMps">Null for a coarse trip.</param>
/// <param name="SpeedingCount">Null when unknown (a coarse trip), never 0 for "unknown".</param>
/// <param name="PhoneCount">Null when phone use could not be measured.</param>
/// <param name="Movement">0.3.1 (D125): the line is a move of a tracker, derived from its fixes (<see cref="MovementDeriver"/>), not a stored trip of a person; it reads "Moved", not "Drive".</param>
/// <param name="Coarse">The trip was recorded from sparse fixes: its path is a rough line.</param>
public sealed record HistoryDrive(
    string Id,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    string FromLabel,
    string ToLabel,
    double Meters,
    double? TopSpeedMps,
    int? SpeedingCount,
    int? PhoneCount,
    bool Coarse,
    double? StartLat,
    double? StartLon,
    double? EndLat,
    double? EndLon,
    bool Movement = false) : HistoryEntry(Id, StartUtc, EndUtc);

/// <summary>
/// One piece of the day's trail: the path of a drive as <c>[lon, lat]</c> points, thinned. A segment is <paramref name="Dashed"/> when the fixes behind it were far apart (a straight
/// guess between them); a hole of more than <see cref="HistoryAssembler.GapBreak"/> without a fix is left out, so the trail has gaps where there is no data.
/// </summary>
/// <param name="EntryId">The id of the <see cref="HistoryDrive"/> the segment belongs to.</param>
public sealed record HistoryTrailSegment(string EntryId, bool Dashed, IReadOnlyList<double[]> Points);

/// <summary>A place on the day's map that is not a visit: where the day began or ended.</summary>
/// <param name="Time">When the member was there.</param>
public sealed record HistoryEndPoint(double Lat, double Lon, DateTimeOffset Time);

/// <summary>
/// A member's day (0.3.0): the visits and drives in time order, the trail, and the totals. <see cref="Recorded"/> says whether the Realm stored anything for the day, so the screen can
/// tell "no data" from "stayed in one place".
/// </summary>
/// <param name="Day">The local day in HA's zone.</param>
/// <param name="StartUtc">Local midnight of <paramref name="Day"/>, as an instant (inclusive).</param>
/// <param name="EndUtc">Local midnight of the next day (exclusive): 23 or 25 hours after the start on a day the clocks change.</param>
/// <param name="Entries">Oldest first. A visit that spans midnight appears on both days, clipped.</param>
/// <param name="Trail">Empty when the day was read without its trail (the range list).</param>
/// <param name="Recorded">At least one fix, or one drive, is stored for the day (or the member is still at a place from the day before).</param>
/// <param name="Start">Where the day began; null when nothing is known.</param>
/// <param name="End">Where the day ended (or the member is now, for today); null when nothing is known.</param>
public sealed record HistoryDayVm(
    string MemberId,
    DateOnly Day,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    IReadOnlyList<HistoryEntry> Entries,
    IReadOnlyList<HistoryTrailSegment> Trail,
    bool Recorded,
    HistoryEndPoint? Start,
    HistoryEndPoint? End)
{
    /// <summary>The drives of the day.</summary>
    public IEnumerable<HistoryDrive> Drives => Entries.OfType<HistoryDrive>();

    /// <summary>The visits of the day.</summary>
    public IEnumerable<HistoryStay> Stays => Entries.OfType<HistoryStay>();

    /// <summary>The distance of the day's drives, metres.</summary>
    public double TotalMeters => Drives.Sum(drive => drive.Meters);

    /// <summary>The number of drives.</summary>
    public int DriveCount => Drives.Count();

    /// <summary>The number of visits.</summary>
    public int StayCount => Stays.Count();

    /// <summary>A day with nothing to list.</summary>
    public static HistoryDayVm Empty(string memberId, DateOnly day, DateTimeOffset startUtc, DateTimeOffset endUtc) =>
        new(memberId, day, startUtc, endUtc, [], [], false, null, null);
}
