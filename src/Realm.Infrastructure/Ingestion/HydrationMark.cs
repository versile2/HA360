namespace Realm.Infrastructure.Ingestion;

/// <summary>
/// What the database held for one member when this run of the add-on started, noted before anything the live stream stores: the point where the stored
/// history ends and the live data begins. The backfill fills the time before it (02 section 8.1: <c>from = max(now - backfill_days, lastStoredTs - 5 min)</c>
/// reads the last stored fix of the previous run, not one the live feed has just written), and the live detector owns every trip after it.
/// </summary>
/// <param name="HydratedAtUtc">The instant the member was hydrated: the replay ran up to it.</param>
/// <param name="Life360LatestUtc">The newest stored Life360 fix, null when there was none.</param>
/// <param name="CompanionLatestUtc">The newest stored companion fix, null when there was none.</param>
public sealed record HydrationMark(DateTimeOffset HydratedAtUtc, DateTimeOffset? Life360LatestUtc, DateTimeOffset? CompanionLatestUtc);
