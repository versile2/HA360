using Xunit;

namespace Realm.Domain.Tests;

// The examples of 02 section 1.5, then the cases it names in words: a US state name, no city, a USA suffix, one part.
public class AddressParserTests
{
    // "Street Name, Texas" gives a street and a region but no city; "Eastgate Avenue, Pinebrook, TX" gives all three.
    [Theory]
    [InlineData("Street Name, Texas", "Street Name", null, "Texas", "Street Name, Texas")]
    [InlineData("Eastgate Avenue, Pinebrook, TX", "Eastgate Avenue", "Pinebrook", "TX", "Eastgate Avenue, Pinebrook, TX")]
    [InlineData("48 Larkspur Lane, Millbrook, TX", "48 Larkspur Lane", "Millbrook", "TX", "48 Larkspur Lane, Millbrook, TX")]
    public void Examples_of_the_spec(string address, string street, string? city, string? region, string fullAddress)
    {
        var parsed = AddressParser.Parse(address);

        Assert.NotNull(parsed);
        Assert.Equal(street, parsed.Street);
        Assert.Equal(city, parsed.City);
        Assert.Equal(region, parsed.Region);
        Assert.Equal(fullAddress, parsed.FullAddress);
    }

    // The region is a two-letter state code or a state name; both are kept as written.
    [Theory]
    [InlineData("Maple Road, Springfield, Illinois", "Springfield", "Illinois")]
    [InlineData("Maple Road, Albany, New York", "Albany", "New York")]
    [InlineData("Maple Road, Springfield, IL", "Springfield", "IL")]
    [InlineData("Maple Road, Washington, DC", "Washington", "DC")]
    public void A_us_state_name_or_code_is_the_region(string address, string city, string region)
    {
        var parsed = AddressParser.Parse(address);

        Assert.NotNull(parsed);
        Assert.Equal("Maple Road", parsed.Street);
        Assert.Equal(city, parsed.City);
        Assert.Equal(region, parsed.Region);
    }

    // No city: the street and a region only, so the UI cannot show "{street} · {City}, {ST}".
    [Theory]
    [InlineData("Street Name, TX", "TX")]
    [InlineData("Street Name, Texas", "Texas")]
    public void Street_and_region_only_has_no_city(string address, string region)
    {
        var parsed = AddressParser.Parse(address);

        Assert.NotNull(parsed);
        Assert.Equal("Street Name", parsed.Street);
        Assert.Null(parsed.City);
        Assert.Equal(region, parsed.Region);
    }

    // A trailing USA or United States is dropped, and FullAddress is the cleaned original.
    [Theory]
    [InlineData("Eastgate Avenue, Pinebrook, TX, USA", "Pinebrook", "TX", "Eastgate Avenue, Pinebrook, TX")]
    [InlineData("Eastgate Avenue, Pinebrook, TX, United States", "Pinebrook", "TX", "Eastgate Avenue, Pinebrook, TX")]
    [InlineData("Street Name, Texas, USA", null, "Texas", "Street Name, Texas")]
    public void A_trailing_country_is_dropped(string address, string? city, string region, string fullAddress)
    {
        var parsed = AddressParser.Parse(address);

        Assert.NotNull(parsed);
        Assert.Equal(city, parsed.City);
        Assert.Equal(region, parsed.Region);
        Assert.Equal(fullAddress, parsed.FullAddress);
    }

    // One part is only a street, even when it looks like a place: the fixture's queen is on "I-35".
    [Theory]
    [InlineData("I-35")]
    [InlineData("Texas")]
    public void One_part_is_the_street(string address)
    {
        var parsed = AddressParser.Parse(address);

        Assert.NotNull(parsed);
        Assert.Equal(address, parsed.Street);
        Assert.Null(parsed.City);
        Assert.Null(parsed.Region);
        Assert.Equal(address, parsed.FullAddress);
    }

    // Part 0 is the street and the parts between it and the region are the city, joined by ", ".
    [Fact]
    public void Several_middle_parts_are_joined_into_the_city()
    {
        var parsed = AddressParser.Parse("Unit 4, Eastgate Avenue, Pinebrook, TX");

        Assert.NotNull(parsed);
        Assert.Equal("Unit 4", parsed.Street);
        Assert.Equal("Eastgate Avenue, Pinebrook", parsed.City);
        Assert.Equal("TX", parsed.Region);
    }

    [Fact]
    public void Surrounding_whitespace_is_trimmed()
    {
        var parsed = AddressParser.Parse("  Street Name,  Texas ");

        Assert.NotNull(parsed);
        Assert.Equal("Street Name", parsed.Street);
        Assert.Equal("Texas", parsed.Region);
        Assert.Equal("Street Name, Texas", parsed.FullAddress);
    }

    // 02 is silent when the last part is not a US state: the street is still part 0, and nothing else is guessed.
    [Theory]
    [InlineData("Rue Exemple, Paris", "Rue Exemple")]
    [InlineData("Main Street, ZZ", "Main Street")]
    public void A_last_part_that_is_not_a_us_state_gives_no_city_and_no_region(string address, string street)
    {
        var parsed = AddressParser.Parse(address);

        Assert.NotNull(parsed);
        Assert.Equal(street, parsed.Street);
        Assert.Null(parsed.City);
        Assert.Null(parsed.Region);
        Assert.Equal(address, parsed.FullAddress);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("USA")]
    public void No_address_text_gives_null(string? address)
    {
        Assert.Null(AddressParser.Parse(address));
    }
}
