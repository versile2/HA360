namespace Realm.Domain;

/// <summary>One driver's count of an event type this week and in the comparison window.</summary>
public record EventDriverCount(
    string MemberId,
    int? Count,
    int? ComparatorCount);
