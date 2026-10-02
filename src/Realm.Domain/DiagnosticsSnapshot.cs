namespace Realm.Domain;

/// <summary>
/// The body of <c>diagnostics.json</c>, field for field as 03 section 2.11 lists it. Small by construction: there is nothing here to redact.
/// The web host serialises it with camel-case names and string enums.
/// </summary>
/// <param name="Schema">1.</param>
/// <param name="Version">The add-on version.</param>
/// <param name="Mode"><c>live</c> or <c>demo</c>.</param>
/// <param name="UptimeSeconds">Process uptime.</param>
/// <param name="Zone">The IANA zone Home Assistant reports.</param>
/// <param name="ZoneDataOk">Whether the start-up self-check resolved zone data.</param>
/// <param name="Circuits">Filled by the web host from its circuit handler; an implementation of <see cref="IDiagnostics"/> leaves it at zero.</param>
/// <param name="Connections">The same list as <c>RealmSnapshot.Connections</c>.</param>
/// <param name="Members">One entry per member: the option slug, the current freshness and the effective threshold.</param>
/// <param name="Warnings">Fixed codes, never prose (03 section 2.11).</param>
public sealed record DiagnosticsSnapshot(
    int Schema,
    string Version,
    string Mode,
    long UptimeSeconds,
    string Zone,
    bool ZoneDataOk,
    DiagnosticsSnapshot.CircuitCounts Circuits,
    IReadOnlyList<ConnectionVm> Connections,
    DiagnosticsSnapshot.EntityCounts Counts,
    DiagnosticsSnapshot.HaCounters Ha,
    DiagnosticsSnapshot.IngestionCounters Ingestion,
    DiagnosticsSnapshot.DbCounters Db,
    IReadOnlyList<DiagnosticsSnapshot.MemberEntry> Members,
    IReadOnlyList<string> Warnings)
{
    /// <summary>The schema number of the file.</summary>
    public const int CurrentSchema = 1;

    /// <summary>Open and disconnected Blazor circuits.</summary>
    public sealed record CircuitCounts(int Open, int Disconnected);

    /// <summary>How many members, vehicles and places the app is showing.</summary>
    public sealed record EntityCounts(int Members, int Vehicles, int Places);

    /// <summary>The Home Assistant link: <c>WebsocketState</c> is a short state word, never an address.</summary>
    public sealed record HaCounters(string WebsocketState, int Reconnects, DateTimeOffset? LastMessageUtc, int WatchedEntities, int MessagesPerMinute);

    /// <summary>The ingestion queue.</summary>
    public sealed record IngestionCounters(int EventsPerMinute, int QueueDepth, long Dropped);

    /// <summary>The database file: no path.</summary>
    public sealed record DbCounters(int SchemaVersion, bool UncleanShutdownAtStart, long SizeBytes, int WriterQueueDepth, DateTimeOffset? LastCommitUtc);

    /// <summary>One member's freshness and the stale threshold in force for it (02 section 4.7).</summary>
    public sealed record MemberEntry(string Id, Freshness Freshness, int StaleAfterMinutes);
}
