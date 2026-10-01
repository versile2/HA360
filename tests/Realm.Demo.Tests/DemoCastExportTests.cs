using System.Text.Json;
using Xunit;

namespace Realm.Demo.Tests;

// The cast table of 02 section 9.2 is typed out once below, as the oracle: a changed or added string in DemoCast,
// DemoPlaces or the exporter fails here (O-1: the wagon's lore is "The King's Wagon"). The demo cast is fictional
// (D17, D28), so every string the export carries must be one of these.
public class DemoCastExportTests
{
    // id, name, lore, colour, kind, sort order, person user id, phone capable, Life360 address, static label.
    private static readonly (string Id, string Name, string Lore, string Color, string Kind, int SortOrder, string? PersonUserId, bool PhoneCapable, string? Address, string? StaticLabel)[] ExpectedMembers =
    [
        ("king", "Alden", "The King", "#E8BC4E", "live", 0, "demo-user-1", true, null, null),
        ("queen", "Briar", "The Queen", "#C792EA", "live", 1, "demo-user-2", false, "I-35", null),
        ("jester", "Cass", "The Royal Jester", "#5CC8FF", "live", 2, null, false, "48 Larkspur Lane, Millbrook, TX", null),
        ("cryptid", "Dara", "The Court Cryptid", "#FF8FB1", "live", 3, null, false, "Eastgate Avenue, Pinebrook, TX", null),
        ("prince", "Elio", "Prince of the Peaks", "#7EE0A5", "static", 4, null, false, null, "Home · Highmeadow"),
    ];

    // id, name, lore, glyph, sort order, placeholder, placeholder note.
    private static readonly (string Id, string Name, string Lore, string Glyph, int SortOrder, bool IsPlaceholder, string? PlaceholderNote)[] ExpectedVehicles =
    [
        ("wagon", "Ford Pickup", "The King's Wagon", "pickup", 0, false, null),
        ("chariot", "Hatchback", "The Queen's Chariot", "car", 1, true, "Awaiting the royal scribes (the maker's app)"),
    ];

    // id, HA zone name, display name, subtitle, kind, lat, lon, radius in metres, drawn. The two "Work" zones and the two
    // "Rollerdome" zones share an HA name; the display name gets " (2)" on the second (02 section 1.9).
    private static readonly (string Id, string ZoneName, string Name, string Subtitle, string Kind, double Lat, double Lon, double RadiusM, bool Drawn)[] ExpectedPlaces =
    [
        ("home", "Hearth Haven", "Hearth Haven", "Home", "home", 31.0990, -97.3410, 100.0, true),
        ("jester_hall", "The Jester's Hall", "The Jester's Hall", "Cass's house", "family", 31.1040, -97.3560, 100.0, true),
        ("work", "Work", "Work", "The Counting House", "work", 31.1530, -97.4080, 150.0, true),
        ("work_2", "Work", "Work (2)", "The Counting House", "work", 31.1534, -97.4084, 150.0, true),
        ("park", "Park", "Park", "The Commons", "park", 31.0720, -97.3290, 200.0, true),
        ("orrin", "Orrin's", "Orrin's", "Orrin's Stronghold", "family", 31.0420, -97.3880, 120.0, true),
        ("mara", "Mara's", "Mara's", "Mara's Manor", "family", 31.1450, -97.2960, 120.0, true),
        ("skate_one", "Rollerdome", "Rollerdome", "The Tourney Grounds", "fun", 31.1760, -97.4700, 200.0, true),
        ("skate_two", "Rollerdome", "Rollerdome (2)", "The Tourney Grounds", "fun", 31.2210, -97.5140, 200.0, true),
        ("queen_office", "Briar's Office", "Briar's Office", "The Queen's Counting House", "work", 31.0200, -97.5200, 150.0, true),
        ("derby", "Derby Hall", "Derby Hall", "The Joust", "fun", 31.2550, -97.3400, 250.0, true),
        ("cemetery", "Hollow Cemetery", "Hollow Cemetery", "The Quiet Fields", "cemetery", 31.0300, -97.2900, 150.0, true),
        ("vet", "Vet Clinic", "Vet Clinic", "The Beast Healer", "vet", 31.1180, -97.4350, 100.0, true),
        ("wheels", "Wheel Hall", "Wheel Hall", "The Wheeled Hall", "fun", 31.1900, -97.3300, 200.0, true),
        ("approach", "(arrival zone, never shown)", "(arrival zone, never shown)", "n/a", "other", 31.0990, -97.3410, 32187.0, false),
    ];

    private const string ExpectedChariotNote = "Awaiting the royal scribes (the maker's app)";

    private static JsonElement Export() => JsonDocument.Parse(DemoCastExporter.ToJson()).RootElement;

    private static string? StringOrNull(JsonElement element, string property)
    {
        var value = element.GetProperty(property);
        return value.ValueKind == JsonValueKind.Null ? null : value.GetString();
    }

    // ---- DemoCast and DemoPlaces against the table of 02 section 9.2 ----------------------------------------

    [Fact]
    public void The_cast_is_the_five_members_of_the_spec_in_order()
    {
        Assert.Equal("king,queen,jester,cryptid,prince", string.Join(",", DemoCast.Members.Select(m => m.Id)));

        foreach (var expected in ExpectedMembers)
        {
            var member = DemoCast.Members.Single(m => m.Id == expected.Id);

            Assert.Equal(expected.Name, member.Name);
            Assert.Equal(expected.Lore, member.Lore);
            Assert.Equal(expected.Color, member.Color);
            Assert.Equal(expected.Kind, member.Kind.ToString().ToLowerInvariant());
            Assert.Equal(expected.SortOrder, member.SortOrder);
            Assert.Equal(expected.PersonUserId, member.PersonUserId);
            Assert.Equal(expected.PhoneCapable, member.PhoneCapable);
            Assert.Equal(expected.Address, member.Address);
            Assert.Equal(expected.StaticLabel, member.StaticLabel);
        }
    }

    // The two demo user ids of 02 section 9.2 (the king and the queen); nobody else has one.
    [Fact]
    public void Only_the_king_and_the_queen_have_a_person_user_id()
    {
        Assert.Equal("demo-user-1", DemoCast.King.PersonUserId);
        Assert.Equal("demo-user-2", DemoCast.Queen.PersonUserId);
        Assert.Equal("king,queen", string.Join(",", DemoCast.Members.Where(m => m.PersonUserId is not null).Select(m => m.Id)));
    }

    // Phone-use data in the default fixture exists for the one phone-capable driver (D27).
    [Fact]
    public void Only_the_king_is_phone_capable()
    {
        Assert.Equal("king", Assert.Single(DemoCast.Members, m => m.PhoneCapable).Id);
    }

    [Fact]
    public void The_vehicles_are_the_two_of_the_spec_and_the_wagon_lore_is_the_kings_wagon()
    {
        Assert.Equal("wagon,chariot", string.Join(",", DemoCast.Vehicles.Select(v => v.Id)));

        // O-1: "The King's Wagon", never "The Steward's Wagon".
        Assert.Equal("The King's Wagon", DemoCast.Wagon.Lore);

        foreach (var expected in ExpectedVehicles)
        {
            var vehicle = DemoCast.Vehicles.Single(v => v.Id == expected.Id);

            Assert.Equal(expected.Name, vehicle.Name);
            Assert.Equal(expected.Lore, vehicle.Lore);
            Assert.Equal(expected.Glyph, vehicle.Glyph.ToString().ToLowerInvariant());
            Assert.Equal(expected.SortOrder, vehicle.SortOrder);
            Assert.Equal(expected.IsPlaceholder, vehicle.IsPlaceholder);
            Assert.Equal(expected.PlaceholderNote, vehicle.PlaceholderNote);
        }
    }

    [Fact]
    public void The_chariot_note_is_the_string_of_the_spec()
    {
        Assert.Equal(ExpectedChariotNote, DemoCast.ChariotNote);
        Assert.Equal(DemoCast.ChariotNote, DemoCast.Chariot.PlaceholderNote);
    }

    [Fact]
    public void The_places_are_the_fifteen_zones_of_the_spec_in_order_and_fourteen_are_drawn()
    {
        Assert.Equal(string.Join(",", ExpectedPlaces.Select(p => p.Id)), string.Join(",", DemoPlaces.All.Select(p => p.Id)));
        Assert.Equal(15, DemoPlaces.All.Count);
        Assert.Equal(14, DemoPlaces.Drawn.Count);
        Assert.Equal(string.Join(",", ExpectedPlaces.Where(p => p.Drawn).Select(p => p.Id)), string.Join(",", DemoPlaces.Drawn.Select(p => p.Id)));

        foreach (var expected in ExpectedPlaces)
        {
            var place = DemoPlaces.All.Single(p => p.Id == expected.Id);

            Assert.Equal(expected.ZoneName, place.ZoneName);
            Assert.Equal(expected.Name, place.Name);
            Assert.Equal(expected.Subtitle, place.Subtitle);
            Assert.Equal(expected.Kind, place.Kind.ToString().ToLowerInvariant());
            Assert.Equal(expected.Lat, place.Lat);
            Assert.Equal(expected.Lon, place.Lon);
            Assert.Equal(expected.RadiusM, place.RadiusM);
        }
    }

    // The duplicate rule of 02 section 1.9 as AC-29 needs it: Home Assistant names two zones "Work" and two "Rollerdome".
    [Fact]
    public void Two_work_zones_and_two_rollerdome_zones_share_a_zone_name_and_differ_in_display_name()
    {
        Assert.Equal("Work", DemoPlaces.Work.ZoneName);
        Assert.Equal("Work", DemoPlaces.Work2.ZoneName);
        Assert.Equal("Work", DemoPlaces.Work.Name);
        Assert.Equal("Work (2)", DemoPlaces.Work2.Name);
        Assert.Equal("Rollerdome", DemoPlaces.SkateOne.ZoneName);
        Assert.Equal("Rollerdome", DemoPlaces.SkateTwo.ZoneName);
        Assert.Equal("Rollerdome", DemoPlaces.SkateOne.Name);
        Assert.Equal("Rollerdome (2)", DemoPlaces.SkateTwo.Name);
    }

    // The arrival zone is the 20 mile circle around home that is never drawn or listed.
    [Fact]
    public void The_arrival_zone_is_centred_on_home_and_never_drawn()
    {
        Assert.Equal(DemoPlaces.Home.Lat, DemoPlaces.Approach.Lat);
        Assert.Equal(DemoPlaces.Home.Lon, DemoPlaces.Approach.Lon);
        Assert.Equal(32187.0, DemoPlaces.Approach.RadiusM);
        Assert.DoesNotContain(DemoPlaces.Approach, DemoPlaces.Drawn);
    }

    // ---- the export -----------------------------------------------------------------------------------------

    [Fact]
    public void The_export_has_five_members_two_vehicles_the_chariot_note_and_fifteen_places()
    {
        var root = Export();

        Assert.Equal(5, root.GetProperty("members").GetArrayLength());
        Assert.Equal(2, root.GetProperty("vehicles").GetArrayLength());
        Assert.Equal(15, root.GetProperty("places").GetArrayLength());
        Assert.Equal(ExpectedChariotNote, root.GetProperty("chariotNote").GetString());
    }

    [Fact]
    public void The_export_carries_every_member_with_the_same_ids_and_strings_as_the_cast()
    {
        var exported = Export().GetProperty("members").EnumerateArray().ToList();

        Assert.Equal(string.Join(",", ExpectedMembers.Select(m => m.Id)), string.Join(",", exported.Select(m => m.GetProperty("id").GetString())));

        foreach (var expected in ExpectedMembers)
        {
            var member = exported.Single(m => m.GetProperty("id").GetString() == expected.Id);

            Assert.Equal("id,name,lore,color,kind,sortOrder,personUserId,phoneCapable,address,staticLabel", string.Join(",", member.EnumerateObject().Select(p => p.Name)));
            Assert.Equal(expected.Name, member.GetProperty("name").GetString());
            Assert.Equal(expected.Lore, member.GetProperty("lore").GetString());
            Assert.Equal(expected.Color, member.GetProperty("color").GetString());
            Assert.Equal(expected.Kind, member.GetProperty("kind").GetString());
            Assert.Equal(expected.SortOrder, member.GetProperty("sortOrder").GetInt32());
            Assert.Equal(expected.PersonUserId, StringOrNull(member, "personUserId"));
            Assert.Equal(expected.PhoneCapable, member.GetProperty("phoneCapable").GetBoolean());
            Assert.Equal(expected.Address, StringOrNull(member, "address"));
            Assert.Equal(expected.StaticLabel, StringOrNull(member, "staticLabel"));
        }
    }

    [Fact]
    public void The_export_carries_both_demo_user_ids()
    {
        var members = Export().GetProperty("members").EnumerateArray().ToList();

        Assert.Equal("demo-user-1", StringOrNull(members.Single(m => m.GetProperty("id").GetString() == "king"), "personUserId"));
        Assert.Equal("demo-user-2", StringOrNull(members.Single(m => m.GetProperty("id").GetString() == "queen"), "personUserId"));
    }

    [Fact]
    public void The_export_carries_every_vehicle_and_the_wagon_lore_is_the_kings_wagon()
    {
        var exported = Export().GetProperty("vehicles").EnumerateArray().ToList();

        foreach (var expected in ExpectedVehicles)
        {
            var vehicle = exported.Single(v => v.GetProperty("id").GetString() == expected.Id);

            Assert.Equal("id,name,lore,glyph,sortOrder,isPlaceholder,placeholderNote", string.Join(",", vehicle.EnumerateObject().Select(p => p.Name)));
            Assert.Equal(expected.Name, vehicle.GetProperty("name").GetString());
            Assert.Equal(expected.Lore, vehicle.GetProperty("lore").GetString());
            Assert.Equal(expected.Glyph, vehicle.GetProperty("glyph").GetString());
            Assert.Equal(expected.SortOrder, vehicle.GetProperty("sortOrder").GetInt32());
            Assert.Equal(expected.IsPlaceholder, vehicle.GetProperty("isPlaceholder").GetBoolean());
            Assert.Equal(expected.PlaceholderNote, StringOrNull(vehicle, "placeholderNote"));
        }

        Assert.Equal("The King's Wagon", StringOrNull(exported.Single(v => v.GetProperty("id").GetString() == "wagon"), "lore"));
    }

    // The apostrophes and the middle dot reach the file as written, so a grep or a JSON reader finds the literal strings.
    [Fact]
    public void The_export_text_keeps_apostrophes_and_the_middle_dot_unescaped()
    {
        var json = DemoCastExporter.ToJson();

        Assert.Contains("\"The King's Wagon\"", json);
        Assert.Contains("\"Awaiting the royal scribes (the maker's app)\"", json);
        Assert.Contains("\"Home · Highmeadow\"", json);
        Assert.DoesNotContain("Steward", json);
    }

    [Fact]
    public void The_export_carries_every_place_and_marks_fourteen_as_drawn()
    {
        var exported = Export().GetProperty("places").EnumerateArray().ToList();

        Assert.Equal(string.Join(",", ExpectedPlaces.Select(p => p.Id)), string.Join(",", exported.Select(p => p.GetProperty("id").GetString())));
        Assert.Equal(14, exported.Count(p => p.GetProperty("drawn").GetBoolean()));

        foreach (var expected in ExpectedPlaces)
        {
            var place = exported.Single(p => p.GetProperty("id").GetString() == expected.Id);

            Assert.Equal("id,zoneName,name,subtitle,kind,lat,lon,radiusM,drawn", string.Join(",", place.EnumerateObject().Select(p => p.Name)));
            Assert.Equal(expected.ZoneName, place.GetProperty("zoneName").GetString());
            Assert.Equal(expected.Name, place.GetProperty("name").GetString());
            Assert.Equal(expected.Subtitle, place.GetProperty("subtitle").GetString());
            Assert.Equal(expected.Kind, place.GetProperty("kind").GetString());
            Assert.Equal(expected.Lat, place.GetProperty("lat").GetDouble());
            Assert.Equal(expected.Lon, place.GetProperty("lon").GetDouble());
            Assert.Equal(expected.RadiusM, place.GetProperty("radiusM").GetDouble());
            Assert.Equal(expected.Drawn, place.GetProperty("drawn").GetBoolean());
        }
    }

    // D17, D28: the public repository carries only the fictional cast, so every string value in the export is one of the
    // strings of the table above (or one of the fixed keywords of its enumerations).
    [Fact]
    public void Every_string_in_the_export_belongs_to_the_fictional_cast()
    {
        var allowed = new HashSet<string>(StringComparer.Ordinal) { ExpectedChariotNote };
        foreach (var m in ExpectedMembers)
        {
            allowed.UnionWith([m.Id, m.Name, m.Lore, m.Color, m.Kind]);
            allowed.UnionWith(new[] { m.PersonUserId, m.Address, m.StaticLabel }.OfType<string>());
        }

        foreach (var v in ExpectedVehicles)
        {
            allowed.UnionWith([v.Id, v.Name, v.Lore, v.Glyph]);
            allowed.UnionWith(new[] { v.PlaceholderNote }.OfType<string>());
        }

        foreach (var p in ExpectedPlaces)
        {
            allowed.UnionWith([p.Id, p.ZoneName, p.Name, p.Subtitle, p.Kind]);
        }

        var strays = new List<string>();
        CollectStrings(Export(), strays);

        Assert.DoesNotContain(strays, s => !allowed.Contains(s));
        Assert.True(strays.Count > 0);
    }

    // Property names are not values; everything else that is a string is.
    private static void CollectStrings(JsonElement element, List<string> into)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                into.Add(element.GetString() ?? string.Empty);
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    CollectStrings(item, into);
                }

                break;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    CollectStrings(property.Value, into);
                }

                break;
        }
    }

    // The CLI mode writes the file for the TypeScript tests: the same JSON, UTF-8 without a byte-order mark, in a folder
    // that may not exist yet.
    [Fact]
    public void Writing_the_export_creates_the_folder_and_writes_the_json_without_a_byte_order_mark()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "demo-cast-export-test");
        var path = Path.Combine(root, "nested", "demo-cast.json");
        try
        {
            DemoCastExporter.WriteTo(path);

            var bytes = File.ReadAllBytes(path);
            Assert.Equal((byte)'{', bytes[0]);
            Assert.Equal(DemoCastExporter.ToJson() + "\n", System.Text.Encoding.UTF8.GetString(bytes));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
