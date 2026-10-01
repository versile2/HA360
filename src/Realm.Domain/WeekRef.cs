namespace Realm.Domain;

/// <summary>One week chip. Start and End are local to HA's zone; End is the display end (the last whole second of the week).</summary>
/// <param name="Offset">0 is the current week, 1 the week before, and so on.</param>
public record WeekRef(
    int Offset,
    DateTimeOffset Start,
    DateTimeOffset End);
