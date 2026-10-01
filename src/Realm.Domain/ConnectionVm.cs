namespace Realm.Domain;

/// <summary>Name is one of HomeAssistant, Life360Trackers, FordPass, VehiclePlaceholder; a UI that meets any other name ignores it.</summary>
public record ConnectionVm(
    string Name,
    ConnectionState State,
    DateTimeOffset? LastSyncUtc);
