namespace Realm.Domain;

/// <summary>The week's top speed: the winner, where and when, and every driver's own top speed.</summary>
public record TopSpeedStat(
    string MemberId,
    double SpeedMps,
    DateTimeOffset AtUtc,
    string? Street,
    IReadOnlyList<DriverTopSpeed> Drivers);
