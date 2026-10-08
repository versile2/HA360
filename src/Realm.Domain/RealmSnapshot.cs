namespace Realm.Domain;

/// <summary>Immutable picture of the realm at one instant. Field order is part of the contract: build it with named arguments.</summary>
/// <param name="Zone">HA's IANA time zone id; every time is shown in it.</param>
/// <param name="StatsVersion">Bumped whenever a trip is closed and written, so the Driving page knows when to refetch.</param>
/// <param name="WeekStart">The add-on option driving_week_start.</param>
/// <param name="RetentionFixDays">The add-on option retention_fix_days (100 to 400): decides which long periods the Driving report offers (6 months needs 185, a year 366).</param>
/// <param name="Connections">Exactly four entries: HomeAssistant, Life360Trackers, FordPass, VehiclePlaceholder.</param>
public record RealmSnapshot(
    DateTimeOffset ServerNowUtc,
    int StatsVersion,
    DayOfWeek WeekStart,
    IReadOnlyList<MemberVm> Members,
    IReadOnlyList<VehicleVm> Vehicles,
    IReadOnlyList<PlaceVm> Places,
    IReadOnlyList<ConnectionVm> Connections,
    string Zone,
    UnitSystem UnitSystem,
    int RetentionFixDays = 100);
