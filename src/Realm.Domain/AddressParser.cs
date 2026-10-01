namespace Realm.Domain;

/// <summary>Splits a Life360 "street, region" free-text address into street, city and region (02 section 1.5).</summary>
public static class AddressParser
{
    private static readonly string[] Countries = ["USA", "United States"];

    // The 50 states and DC, upper-case codes.
    private static readonly HashSet<string> StateCodes = new(StringComparer.Ordinal)
    {
        "AL", "AK", "AZ", "AR", "CA", "CO", "CT", "DE", "DC", "FL", "GA", "HI", "ID", "IL", "IN", "IA", "KS", "KY",
        "LA", "ME", "MD", "MA", "MI", "MN", "MS", "MO", "MT", "NE", "NV", "NH", "NJ", "NM", "NY", "NC", "ND", "OH",
        "OK", "OR", "PA", "RI", "SC", "SD", "TN", "TX", "UT", "VT", "VA", "WA", "WV", "WI", "WY",
    };

    private static readonly HashSet<string> StateNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Alabama", "Alaska", "Arizona", "Arkansas", "California", "Colorado", "Connecticut", "Delaware",
        "District of Columbia", "Florida", "Georgia", "Hawaii", "Idaho", "Illinois", "Indiana", "Iowa", "Kansas",
        "Kentucky", "Louisiana", "Maine", "Maryland", "Massachusetts", "Michigan", "Minnesota", "Mississippi",
        "Missouri", "Montana", "Nebraska", "Nevada", "New Hampshire", "New Jersey", "New Mexico", "New York",
        "North Carolina", "North Dakota", "Ohio", "Oklahoma", "Oregon", "Pennsylvania", "Rhode Island",
        "South Carolina", "South Dakota", "Tennessee", "Texas", "Utah", "Vermont", "Virginia", "Washington",
        "West Virginia", "Wisconsin", "Wyoming",
    };

    /// <summary>
    /// Splits on ", ", drops a trailing "USA" or "United States", takes part 0 as the street and, when the last
    /// part is a US state code or name, that as the region and the parts between as the city.
    /// </summary>
    /// <returns>Null when there is no address text.</returns>
    public static ParsedAddress? Parse(string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return null;
        }

        var parts = address.Split(", ", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
        if (parts.Count > 0 && Countries.Contains(parts[^1], StringComparer.OrdinalIgnoreCase))
        {
            parts.RemoveAt(parts.Count - 1);
        }

        if (parts.Count == 0)
        {
            return null;
        }

        var fullAddress = string.Join(", ", parts);
        var last = parts[^1];
        if (parts.Count == 1 || !(StateCodes.Contains(last) || StateNames.Contains(last)))
        {
            return new ParsedAddress(parts[0], null, null, fullAddress);
        }

        var city = parts.Count > 2 ? string.Join(", ", parts.Skip(1).Take(parts.Count - 2)) : null;
        return new ParsedAddress(parts[0], city, last, fullAddress);
    }
}
