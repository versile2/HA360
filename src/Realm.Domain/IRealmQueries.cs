namespace Realm.Domain;

/// <summary>
/// The read-only SQL queries of the live data layer (03 section 2.2; the reads of 02 sections 4.7, 6.5 to 6.7 and 7.5). Every method uses its own
/// short-lived connection, so reads never wait for the writer (WAL). Times are UTC instants; a range is half open: from inclusive, to exclusive.
/// </summary>
public interface IRealmQueries
{
    /// <summary>
    /// The stored trips whose start lies in [<paramref name="fromUtc"/>, <paramref name="toUtc"/>), newest start first, for one member or (null) for everyone.
    /// This is the input of the weekly statistics rules (<see cref="StatsRules"/>) and of the driver's trip list.
    /// </summary>
    Task<IReadOnlyList<StatsTrip>> GetTripsAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, string? memberId, CancellationToken cancellationToken = default);

    /// <summary>The earliest stored fix of each member that has one (<c>members.recording_start</c>, 02 section 6.5); members with no fix are absent.</summary>
    Task<IReadOnlyDictionary<string, DateTimeOffset>> GetRecordingStartsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// The stored fixes of a member in [<paramref name="fromUtc"/>, <paramref name="toUtc"/>), oldest first, in the form the trip detector takes (detector replay and
    /// backfill, 02 sections 5.4 and 8.3). The table keeps no entity id, so <see cref="RawFix.EntityId"/> is empty and <see cref="RawFix.BatteryAsOfUtc"/> is null.
    /// </summary>
    Task<IReadOnlyList<RawFix>> GetFixesAsync(string memberId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default);

    /// <summary>The newest stored fix of a member's source, or null when there is none (the fusion seed and restart-echo check, 02 section 1.6; gap-fill start, 02 section 8.1).</summary>
    Task<RawFix?> GetLatestFixAsync(string memberId, FixSource source, CancellationToken cancellationToken = default);

    /// <summary>The times of the stored fixes of one member source in [<paramref name="fromUtc"/>, <paramref name="toUtc"/>), oldest first: the input of <see cref="FreshnessRules.Heartbeat"/> (02 section 4.7).</summary>
    Task<IReadOnlyList<DateTimeOffset>> GetFixTimesAsync(string memberId, FixSource source, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default);

    /// <summary>The newest stored fix of a member at or before <paramref name="atOrBeforeUtc"/> that carries an address, or null (the street near a time, 02 section 7.5).</summary>
    Task<RawFix?> GetLatestAddressFixAsync(string memberId, DateTimeOffset atOrBeforeUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// The phone sensor transitions of a member in [<paramref name="fromUtc"/>, <paramref name="toUtc"/>), oldest first (the input of <see cref="PhoneUseDetector"/>, 02 section 7.5).
    /// Activity transitions are not returned: phone-use detection does not read them.
    /// </summary>
    Task<IReadOnlyList<PhoneSignal>> GetPhoneSignalsAsync(string memberId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default);

    /// <summary>Every roster row (02 section 7.2, "Who's on the map"), in group and sort order. Empty on a database that has never discovered anyone.</summary>
    Task<IReadOnlyList<RosterEntry>> GetRosterAsync(CancellationToken cancellationToken = default);
}
