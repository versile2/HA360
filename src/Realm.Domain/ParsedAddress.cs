namespace Realm.Domain;

/// <summary>A Life360 address split into the parts the UI shows.</summary>
/// <param name="City">Null when the address has none.</param>
/// <param name="Region">A US state code or name; null when the address has none.</param>
/// <param name="FullAddress">The cleaned original, without a trailing country.</param>
public record ParsedAddress(
    string Street,
    string? City,
    string? Region,
    string FullAddress);
