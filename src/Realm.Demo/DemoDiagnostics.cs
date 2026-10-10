using System.Reflection;
using Realm.Domain;

namespace Realm.Demo;

/// <summary>
/// The Demo <see cref="IDiagnostics"/> (03 section 2.11): canned values with <c>mode</c> "demo", the fixture's zone, the cast's counts and no warnings.
/// Nothing here comes from a service, so the numbers never change between two calls; the web host adds the circuit counts.
/// </summary>
public sealed class DemoDiagnostics : IDiagnostics
{
    private const int StaleAfterMinutes = 30;   // the option default the fixture runs under (02 section 3.1)

    /// <inheritdoc />
    public DiagnosticsSnapshot GetSnapshot()
    {
        var members = DemoCast.Members
            .Select(member => new DiagnosticsSnapshot.MemberEntry(member.Id, member.Kind == MemberKind.Static ? Freshness.Static : Freshness.Fresh, StaleAfterMinutes))
            .ToArray();
        var connections = new[]
        {
            new ConnectionVm(ConnectionNames.HomeAssistant, ConnectionState.Connected, DemoDataSource.Anchor),
            new ConnectionVm(ConnectionNames.Life360Trackers, ConnectionState.Connected, DemoDataSource.Anchor),
        };

        return new DiagnosticsSnapshot(
            Schema: DiagnosticsSnapshot.CurrentSchema,
            Version: AddOnVersion(),
            Mode: "demo",
            UptimeSeconds: 3600,
            Zone: DemoDataSource.ZoneId,
            ZoneDataOk: true,
            Circuits: new DiagnosticsSnapshot.CircuitCounts(0, 0),
            Connections: connections,
            Counts: new DiagnosticsSnapshot.EntityCounts(DemoCast.Members.Count, DemoCast.Vehicles.Count, DemoPlaces.Drawn.Count),
            Ha: new DiagnosticsSnapshot.HaCounters("connected", Reconnects: 0, LastMessageUtc: DemoDataSource.Anchor, WatchedEntities: 24, MessagesPerMinute: 12),
            Ingestion: new DiagnosticsSnapshot.IngestionCounters(EventsPerMinute: 12, QueueDepth: 0, Dropped: 0),
            Db: new DiagnosticsSnapshot.DbCounters(SchemaVersion: 4, UncleanShutdownAtStart: false, SizeBytes: 0, WriterQueueDepth: 0, LastCommitUtc: DemoDataSource.Anchor),
            Members: members,
            Warnings: []);
    }

    // The informational version is "<Version>+<source revision>": the add-on version is the part before the plus.
    private static string AddOnVersion()
    {
        var informational = typeof(DemoDiagnostics).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrEmpty(informational))
        {
            return "0.0.0-dev";
        }

        var plus = informational.IndexOf('+', StringComparison.Ordinal);
        return plus < 0 ? informational : informational[..plus];
    }
}
