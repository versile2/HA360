namespace Realm.Domain;

/// <summary>Sums across report drivers. Meters is the sum of the exact distances, rounded only for display.</summary>
public record WeekTotals(
    int Drives,
    double Meters);
