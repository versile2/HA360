namespace Realm.Domain;

/// <summary>The phone-use events of one trip. PhoneCount is null (not 0) when phone use cannot be measured for the trip.</summary>
public record PhoneUseResult(
    int? PhoneCount,
    IReadOnlyList<PhoneUseEvent> Events);
