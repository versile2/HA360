namespace Realm.Domain;

/// <summary>A period of time, start inclusive and end exclusive.</summary>
public record TimeWindow(
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc);
