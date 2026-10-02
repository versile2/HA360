using Realm.Domain;

namespace Realm.Infrastructure.Ingestion;

/// <summary>What <see cref="RealmStateHydrator"/> read for one member: the pointers and times that seed fusion and the heartbeat, and the fixes to replay through the detector.</summary>
/// <param name="ReplayFixes">The stored fixes from <see cref="RealmStateHydrator.ReplayStart"/> to the hydration instant, oldest first.</param>
internal sealed record Hydration(
    RawFix? Life360,
    RawFix? Companion,
    IReadOnlyList<DateTimeOffset> Life360Times,
    IReadOnlyList<DateTimeOffset> CompanionTimes,
    IReadOnlyList<RawFix> ReplayFixes);
