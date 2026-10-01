namespace Realm.Domain;

/// <summary>One transition of a phone sensor, stored at the time Home Assistant changed it.</summary>
/// <param name="IsOn">True for on, false for off, null for unavailable or unknown.</param>
public record PhoneSignal(
    DateTimeOffset Ts,
    PhoneSignalKind Kind,
    bool? IsOn);
