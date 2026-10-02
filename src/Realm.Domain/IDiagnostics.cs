namespace Realm.Domain;

/// <summary>
/// The source of <c>diagnostics.json</c> (03 section 2.11, 02 section 10.6): one snapshot of states, counts, versions and fixed warning codes.
/// Demo returns canned values; the Live implementation (S13c) is built from the counters the services already keep. The open-circuit counts are
/// not part of it: the web host owns the circuit handler and fills <see cref="DiagnosticsSnapshot.Circuits"/> when it serves the file.
/// </summary>
public interface IDiagnostics
{
    /// <summary>The snapshot at this moment. It never holds a coordinate, an address, a battery value, a token, a user id, an entity id or log text.</summary>
    DiagnosticsSnapshot GetSnapshot();
}
