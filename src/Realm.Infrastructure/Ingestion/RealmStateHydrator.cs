using System.Collections.Concurrent;
using Realm.Domain;

namespace Realm.Infrastructure.Ingestion;

/// <summary>
/// Hydrates a member from the database when it first appears, so a restart loses neither the last fixes nor the drive in progress (03 section 2.4, 02
/// sections 1.6 rule F0, 5.4 and 7.5): the newest stored fix of each source (the pointers a companion echo is compared with), the stored fix times of the
/// last 24 hours (the heartbeat), and the stored fixes of the last 30 minutes (<c>Trips.ReplayMinutes</c>) to replay through the member's
/// <see cref="TripDetector"/>, which is how an open trip is picked up. The pipeline calls it, one member at a time, from its discovery step; the
/// detector and the runtime stay the pipeline's. A database that cannot answer only costs the hydration, never the live stream.
/// </summary>
/// <remarks>
/// The replay window never starts inside a stored trip: it is moved back to 10 minutes before the start of any stored trip it would cut, so a trip that is
/// replayed is replayed whole and comes out with the start it was stored with. Stored trips are never rewritten, so the second time it is found is
/// harmless (<c>UNIQUE(member_id, start_ts)</c>).
/// </remarks>
public sealed class RealmStateHydrator
{
    /// <summary><c>Trips.ReplayMinutes</c> (02 section 5.4): the least the detector replays at a cold start.</summary>
    public static readonly TimeSpan ReplayWindow = TimeSpan.FromMinutes(30);

    // The quiet time before a trip that the detector needs to find the same departure fix again (its anchor ring and the 240 s look-back, with room to spare).
    private static readonly TimeSpan ContextPad = TimeSpan.FromMinutes(10);

    // Stored trips this far before the window are looked at when the window might cut one.
    private static readonly TimeSpan TripReach = TimeSpan.FromHours(24);

    private static readonly TimeSpan FixHistory = TimeSpan.FromHours(24);

    private readonly IRealmQueries _queries;
    private readonly ConcurrentDictionary<string, HydrationMark> _marks = new(StringComparer.Ordinal);

    public RealmStateHydrator(IRealmQueries queries)
    {
        _queries = queries;
    }

    /// <summary>What the database held for the member at start; null until the member has been hydrated (or the attempt has failed).</summary>
    public HydrationMark? MarkOf(string memberId) => _marks.GetValueOrDefault(memberId);

    /// <summary>
    /// Where a replay that is meant to cover <paramref name="from"/> onwards must really begin: <paramref name="from"/>, or earlier when that would fall
    /// inside a stored trip or too close before one for the detector to find its departure again.
    /// </summary>
    /// <param name="trips">The stored trips of the member that end after <paramref name="from"/> minus a day; their order does not matter.</param>
    internal static DateTimeOffset ReplayStart(DateTimeOffset from, IReadOnlyList<StatsTrip> trips)
    {
        var start = from;
        bool moved;
        do
        {
            moved = false;
            foreach (var trip in trips)
            {
                if (trip.StartUtc - ContextPad < start && start < trip.EndUtc)
                {
                    start = trip.StartUtc - ContextPad;
                    moved = true;
                }
            }
        }
        while (moved);

        return start;
    }

    /// <summary>Reads what one member needs at start and notes its <see cref="HydrationMark"/>. Throws what the queries throw; the caller then calls <see cref="Abandon"/>.</summary>
    internal async Task<Hydration> ReadAsync(string memberId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var life360 = await _queries.GetLatestFixAsync(memberId, FixSource.Life360, cancellationToken);
        var companion = await _queries.GetLatestFixAsync(memberId, FixSource.Companion, cancellationToken);
        _marks[memberId] = new HydrationMark(now, life360?.Ts, companion?.Ts);

        var life360Times = await _queries.GetFixTimesAsync(memberId, FixSource.Life360, now - FixHistory, now, cancellationToken);
        var companionTimes = await _queries.GetFixTimesAsync(memberId, FixSource.Companion, now - FixHistory, now, cancellationToken);

        var from = now - ReplayWindow;
        var trips = await _queries.GetTripsAsync(from - TripReach, now, memberId, cancellationToken);
        var fixes = await _queries.GetFixesAsync(memberId, ReplayStart(from, trips), now.AddMilliseconds(1), cancellationToken);
        return new Hydration(life360, companion, life360Times, companionTimes, fixes);
    }

    /// <summary>The member could not be hydrated: its mark says that nothing was known, so the backfill is not held up for ever.</summary>
    internal void Abandon(string memberId, DateTimeOffset now) => _marks.TryAdd(memberId, new HydrationMark(now, null, null));
}
