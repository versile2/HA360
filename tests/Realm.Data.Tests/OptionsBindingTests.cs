using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Realm.Domain;
using Realm.Infrastructure.Options;
using Xunit;

namespace Realm.Data.Tests;

// The options loader (02 sections 3.1 to 3.5): the binding table, the defaults, an absent optional key, the example options.json, and the cross-field validation.
public class OptionsBindingTests
{
    // The /data/options.json of 02 section 3.5 (fictional cast); the middle dot of the prince's pin is written as a JSON escape.
    private const string Example = """
        {
          "log_level": "information",
          "ui_stale_after_minutes": 30,
          "ui_offline_after_hours": 24,
          "ui_vehicle_stale_after_minutes": 45,
          "ui_low_battery_percent": 15,
          "ui_poor_accuracy_meters": 500,
          "ui_default_view_radius_km": 40,
          "ui_max_zone_radius_km": 5,
          "ui_far_away_km": 80,
          "ui_history_tokens": true,
          "features_temp_bubble": false,
          "features_add_rows": false,
          "fusion_stale_grace_minutes": 10,
          "trips_start_speed_mph": 15,
          "trips_stop_merge_seconds": 180,
          "trips_min_distance_miles": 0.3,
          "trips_min_duration_seconds": 120,
          "driving_week_start": "monday",
          "driving_speeding_mph": 80,
          "driving_speeding_min_seconds": 30,
          "driving_phone_min_seconds": 10,
          "retention_fix_days": 120,
          "backfill_days": 10,
          "privacy_log_positions": false,
          "demo_mode": false,
          "allow_demo_param": false,
          "ignore_entities": ["device_tracker.alden_tablet", "device_tracker.192_168_0_50"],
          "members": [
            { "id": "king", "display_name": "Alden", "lore_title": "The King", "color": "#E8BC4E", "sort_order": 0,
              "person": "person.alden", "life360_tracker": "device_tracker.life360_alden",
              "companion_tracker": "device_tracker.alden_phone", "avatar": "auto" },
            { "id": "queen", "display_name": "Briar", "lore_title": "The Queen", "color": "#C792EA", "sort_order": 1,
              "person": "person.briar", "life360_tracker": "device_tracker.life360_briar",
              "companion_tracker": "device_tracker.briar_iphone" },
            { "id": "jester", "display_name": "Cass", "lore_title": "The Royal Jester", "color": "#5CC8FF", "sort_order": 2,
              "life360_tracker": "device_tracker.life360_cass" },
            { "id": "cryptid", "display_name": "Dara", "lore_title": "The Court Cryptid", "color": "#FF8FB1", "sort_order": 3,
              "life360_tracker": "device_tracker.life360_dara" },
            { "id": "prince", "display_name": "Elio", "lore_title": "Prince of the Peaks", "color": "#7EE0A5", "sort_order": 4,
              "kind": "static", "in_driving_report": false, "static_label": "Home · Highmeadow",
              "static_address": "1 Example Rd, Highmeadow, ST 00000", "static_latitude": 38.5000,
              "static_longitude": -98.5000, "static_show_address": false }
          ],
          "vehicles": [
            { "id": "wagon", "name": "Ford Pickup", "lore_title": "The King's Wagon", "glyph": "pickup",
              "integration": "fordpass", "entity_prefix": "fordpass_demo", "sort_order": 0 },
            { "id": "chariot", "name": "Hatchback", "lore_title": "The Queen's Chariot", "glyph": "car",
              "integration": "none", "placeholder_note": "Awaiting the royal scribes (the maker's app)", "sort_order": 1 }
          ],
          "places": [
            { "zone": "zone.home", "display_name": "Hearth Haven", "subtitle": "Home", "kind": "home" },
            { "zone": "zone.jester_hall", "display_name": "The Jester's Hall", "subtitle": "Cass's house", "kind": "family" },
            { "zone": "zone.work", "subtitle": "The Counting House", "kind": "work" },
            { "zone": "zone.work_2", "subtitle": "The Counting House", "kind": "work" },
            { "zone": "zone.skate_one", "display_name": "Rollerdome", "subtitle": "The Tourney Grounds", "kind": "fun" }
          ]
        }
        """;

    public static TheoryData<string> ScalarKeys
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var row in OptionsBinding.Table.Where(row => row.Kind is not (OptionKind.TextList or OptionKind.ObjectList)))
            {
                data.Add(row.Key);
            }

            return data;
        }
    }

    [Fact]
    public void The_table_equals_option_bindings_json_in_keys_paths_defaults_factors_and_optional_flags()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(FindRepoFile("tools/ci/option-bindings.json")));
        var keys = document.RootElement.GetProperty("keys").EnumerateArray().Select(key => key.GetString() ?? string.Empty).ToArray();
        var bindings = document.RootElement.GetProperty("bindings");

        Assert.Equal(keys, OptionsBinding.Table.Select(row => row.Key).ToArray());
        Assert.Equal(keys.Length, bindings.EnumerateObject().Count());
        foreach (var row in OptionsBinding.Table)
        {
            var expected = bindings.GetProperty(row.Key);
            Assert.Equal(expected.GetProperty("path").GetString(), row.Path);
            Assert.Equal(expected.TryGetProperty("factor", out var factor) ? factor.GetDouble() : (double?)null, row.Factor);
            Assert.Equal(expected.TryGetProperty("optional", out var optional) && optional.GetBoolean(), row.Optional);

            var defaultValue = expected.GetProperty("default");
            if (row.Kind is OptionKind.TextList or OptionKind.ObjectList)
            {
                Assert.Equal(JsonValueKind.Array, defaultValue.ValueKind);
                Assert.Equal(0, defaultValue.GetArrayLength());
            }
            else
            {
                Assert.Equal(defaultValue.ValueKind == JsonValueKind.String ? defaultValue.GetString() : defaultValue.GetRawText(), row.Default);
            }
        }
    }

    [Fact]
    public void Only_one_key_is_bound_under_Realm_and_only_one_is_optional()
    {
        Assert.Equal(new[] { "ui_history_tokens" }, OptionsBinding.Table.Where(row => row.Path.StartsWith("Realm:", StringComparison.Ordinal)).Select(row => row.Key).ToArray());
        Assert.Equal(new[] { "me_fallback_member" }, OptionsBinding.Table.Where(row => row.Optional).Select(row => row.Key).ToArray());
    }

    [Theory]
    [MemberData(nameof(ScalarKeys))]
    public void A_scalar_key_is_bound_to_its_path_with_its_default_and_with_a_value(string key)
    {
        var row = OptionsBinding.Table.Single(candidate => candidate.Key == key);
        using var empty = JsonDocument.Parse("{}");
        var (json, text) = Sample(row);
        using var set = JsonDocument.Parse("{ \"" + key + "\": " + json + " }");

        AssertBound(row, row.Default, OptionsBinding.Flatten(empty.RootElement)[row.Path]);
        AssertBound(row, text, OptionsBinding.Flatten(set.RootElement)[row.Path]);
    }

    [Fact]
    public void Lists_are_bound_to_indexed_paths_with_pascal_cased_keys()
    {
        using var document = JsonDocument.Parse(
            """
            {
              "ignore_entities": ["device_tracker.alden_tablet", "device_tracker.192_168_0_50"],
              "members": [
                { "id": "king", "life360_tracker": "device_tracker.life360_alden", "static_latitude": 38.5, "in_driving_report": false, "lore_title": null },
                { "id": "queen" }
              ]
            }
            """);

        var data = OptionsBinding.Flatten(document.RootElement);

        Assert.Equal("device_tracker.192_168_0_50", data["Ignore:Entities:1"]);
        Assert.Equal("king", data["Members:0:Id"]);
        Assert.Equal("device_tracker.life360_alden", data["Members:0:Life360Tracker"]);
        Assert.Equal("38.5", data["Members:0:StaticLatitude"]);
        Assert.Equal("false", data["Members:0:InDrivingReport"]);
        Assert.False(data.ContainsKey("Members:0:LoreTitle")); // null is absent
        Assert.Equal("queen", data["Members:1:Id"]);
        Assert.False(data.ContainsKey("Vehicles:0:Id"));
    }

    [Fact]
    public void An_absent_optional_key_reads_as_empty()
    {
        using var document = JsonDocument.Parse("{}");

        var flat = OptionsBinding.Flatten(document.RootElement);
        var options = OptionsBinding.Parse("{}");

        Assert.Equal(string.Empty, flat["Me:Fallback"]);
        Assert.Equal(string.Empty, options.MeFallbackMember);
        Assert.Equal(string.Empty, OptionsBinding.Bind(new ConfigurationBuilder().Build()).MeFallbackMember);
        Assert.Equal("queen", OptionsBinding.Parse("""{ "me_fallback_member": "queen" }""").MeFallbackMember);
        Assert.Equal(string.Empty, OptionsBinding.Parse("""{ "me_fallback_member": null }""").MeFallbackMember);
    }

    [Fact]
    public void An_empty_file_binds_every_default_of_the_table()
    {
        var options = OptionsBinding.Parse("{}");

        Assert.Equal(LogLevel.Information, options.LogLevel);
        Assert.Equal(30, options.UiStaleAfterMinutes);
        Assert.Equal(24, options.UiOfflineAfterHours);
        Assert.Equal(45, options.UiVehicleStaleAfterMinutes);
        Assert.Equal(15, options.UiLowBatteryPercent);
        Assert.Equal(500, options.UiPoorAccuracyMeters);
        Assert.Equal(40, options.UiDefaultViewRadiusKm);
        Assert.Equal(5.0, options.UiMaxZoneRadiusKm);
        Assert.Equal(80, options.UiFarAwayKm);
        Assert.True(options.UiHistoryTokens);
        Assert.False(options.FeaturesTempBubble);
        Assert.False(options.FeaturesAddRows);
        Assert.Equal(10, options.FusionStaleGraceMinutes);
        Assert.Equal(6.7056, options.TripsStartSpeedMps, 4); // 15 mph
        Assert.Equal(180, options.TripsStopMergeSeconds);
        Assert.Equal(482.8032, options.TripsMinDistanceM, 4); // 0.3 mi
        Assert.Equal(120, options.TripsMinDurationSeconds);
        Assert.Equal(DayOfWeek.Monday, options.DrivingWeekStart);
        Assert.Equal(35.7632, options.DrivingSpeedingMps, 4); // 80 mph
        Assert.Equal(30, options.DrivingSpeedingMinSeconds);
        Assert.Equal(10, options.DrivingPhoneMinSeconds);
        Assert.Equal(120, options.RetentionFixDays);
        Assert.Equal(10, options.BackfillDays);
        Assert.False(options.PrivacyLogPositions);
        Assert.False(options.DemoMode);
        Assert.False(options.AllowDemoParam);
        Assert.Equal(string.Empty, options.MeFallbackMember);
        Assert.Empty(options.IgnoreEntities);
        Assert.Empty(options.Members);
        Assert.Empty(options.Vehicles);
        Assert.Empty(options.Places);

        var defaults = OptionsBinding.Defaults;
        Assert.Equal(options.TripsMinDistanceM, defaults.TripsMinDistanceM);
        Assert.Equal(options.UiStaleAfterMinutes, defaults.UiStaleAfterMinutes);
        Assert.Equal(options.DrivingWeekStart, defaults.DrivingWeekStart);
        Assert.True(OptionsValidator.Validate(defaults).IsValid);
    }

    [Fact]
    public void Every_scalar_key_set_in_the_file_reaches_its_property()
    {
        var options = OptionsBinding.Parse(
            """
            {
              "log_level": "debug", "ui_stale_after_minutes": 31, "ui_offline_after_hours": 25, "ui_vehicle_stale_after_minutes": 46,
              "ui_low_battery_percent": 16, "ui_poor_accuracy_meters": 501, "ui_default_view_radius_km": 41, "ui_max_zone_radius_km": 5.5,
              "ui_far_away_km": 81, "ui_history_tokens": false, "features_temp_bubble": true, "features_add_rows": true,
              "fusion_stale_grace_minutes": 11, "trips_start_speed_mph": 20, "trips_stop_merge_seconds": 181, "trips_min_distance_miles": 0.5,
              "trips_min_duration_seconds": 121, "driving_week_start": "sunday", "driving_speeding_mph": 70, "driving_speeding_min_seconds": 31,
              "driving_phone_min_seconds": 11, "retention_fix_days": 121, "backfill_days": 11, "privacy_log_positions": true,
              "demo_mode": true, "allow_demo_param": true, "me_fallback_member": "queen"
            }
            """);

        Assert.Equal(LogLevel.Debug, options.LogLevel);
        Assert.Equal(31, options.UiStaleAfterMinutes);
        Assert.Equal(25, options.UiOfflineAfterHours);
        Assert.Equal(46, options.UiVehicleStaleAfterMinutes);
        Assert.Equal(16, options.UiLowBatteryPercent);
        Assert.Equal(501, options.UiPoorAccuracyMeters);
        Assert.Equal(41, options.UiDefaultViewRadiusKm);
        Assert.Equal(5.5, options.UiMaxZoneRadiusKm);
        Assert.Equal(81, options.UiFarAwayKm);
        Assert.False(options.UiHistoryTokens);
        Assert.True(options.FeaturesTempBubble);
        Assert.True(options.FeaturesAddRows);
        Assert.Equal(11, options.FusionStaleGraceMinutes);
        Assert.Equal(8.9408, options.TripsStartSpeedMps, 4); // 20 mph x 0.44704
        Assert.Equal(181, options.TripsStopMergeSeconds);
        Assert.Equal(804.672, options.TripsMinDistanceM, 4); // 0.5 mi x 1609.344
        Assert.Equal(121, options.TripsMinDurationSeconds);
        Assert.Equal(DayOfWeek.Sunday, options.DrivingWeekStart);
        Assert.Equal(31.2928, options.DrivingSpeedingMps, 4); // 70 mph x 0.44704
        Assert.Equal(31, options.DrivingSpeedingMinSeconds);
        Assert.Equal(11, options.DrivingPhoneMinSeconds);
        Assert.Equal(121, options.RetentionFixDays);
        Assert.Equal(11, options.BackfillDays);
        Assert.True(options.PrivacyLogPositions);
        Assert.True(options.DemoMode);
        Assert.True(options.AllowDemoParam);
        Assert.Equal("queen", options.MeFallbackMember);
    }

    [Fact]
    public void The_options_json_example_of_02_3_5_loads_and_validates()
    {
        var options = OptionsBinding.Parse(Example);

        Assert.Equal(new[] { "king", "queen", "jester", "cryptid", "prince" }, options.Members.Select(member => member.Id).ToArray());
        Assert.Equal(new[] { "wagon", "chariot" }, options.Vehicles.Select(vehicle => vehicle.Id).ToArray());
        Assert.Equal(
            new[] { "zone.home", "zone.jester_hall", "zone.work", "zone.work_2", "zone.skate_one" },
            options.Places.Select(place => place.Zone).ToArray());
        Assert.Equal(new[] { "device_tracker.alden_tablet", "device_tracker.192_168_0_50" }, options.IgnoreEntities.ToArray());
        Assert.Equal(string.Empty, options.MeFallbackMember); // me_fallback_member is absent in the example (R-068)

        var king = options.Members[0];
        Assert.Equal("Alden", king.DisplayName);
        Assert.Equal("The King", king.LoreTitle);
        Assert.Equal("#E8BC4E", king.Color);
        Assert.Equal(0, king.SortOrder);
        Assert.Equal("person.alden", king.Person);
        Assert.Equal("device_tracker.life360_alden", king.Life360Tracker);
        Assert.Equal("device_tracker.alden_phone", king.CompanionTracker);
        Assert.Equal("auto", king.Avatar);
        Assert.Equal(MemberKind.Live, king.Kind);
        Assert.True(king.InDrivingReport);
        Assert.False(king.StaticShowAddress);

        var jester = options.Members[2];
        Assert.Null(jester.Person);
        Assert.Null(jester.CompanionTracker);
        Assert.Equal("device_tracker.life360_cass", jester.Life360Tracker);

        var prince = options.Members[4];
        Assert.Equal(MemberKind.Static, prince.Kind);
        Assert.False(prince.InDrivingReport);
        Assert.Equal("Home · Highmeadow", prince.StaticLabel);
        Assert.Equal("1 Example Rd, Highmeadow, ST 00000", prince.StaticAddress);
        Assert.Equal(38.5, prince.StaticLatitude);
        Assert.Equal(-98.5, prince.StaticLongitude);
        Assert.False(prince.StaticShowAddress);

        var wagon = options.Vehicles[0];
        Assert.Equal("Ford Pickup", wagon.Name);
        Assert.Equal(VehicleGlyph.Pickup, wagon.Glyph);
        Assert.Equal("fordpass", wagon.Integration);
        Assert.Equal("fordpass_demo", wagon.EntityPrefix);
        var chariot = options.Vehicles[1];
        Assert.Equal(VehicleGlyph.Car, chariot.Glyph);
        Assert.Equal("none", chariot.Integration);
        Assert.Null(chariot.EntityPrefix);
        Assert.Equal("Awaiting the royal scribes (the maker's app)", chariot.PlaceholderNote);

        Assert.Equal("Hearth Haven", options.Places[0].DisplayName);
        Assert.Equal(PlaceKind.Home, options.Places[0].Kind);
        Assert.Null(options.Places[2].DisplayName); // zone.work has no display_name
        Assert.Equal("The Counting House", options.Places[2].Subtitle);
        Assert.Equal(PlaceKind.Work, options.Places[3].Kind);
        Assert.Equal(PlaceKind.Fun, options.Places[4].Kind);
        Assert.False(options.Places[4].Hidden);

        var validation = OptionsValidator.Validate(options);
        Assert.True(validation.IsValid);
        Assert.Empty(validation.Errors);
        Assert.Empty(validation.Warnings);

        // The values the table says are unchanged by the example: the options file only repeats the defaults.
        Assert.Equal(OptionsBinding.Defaults.TripsStartSpeedMps, options.TripsStartSpeedMps);
        Assert.Equal(OptionsBinding.Defaults.DrivingSpeedingMps, options.DrivingSpeedingMps);
    }

    [Fact]
    public void The_example_flattens_to_the_paths_of_the_table()
    {
        using var document = JsonDocument.Parse(Example);

        var data = OptionsBinding.Flatten(document.RootElement);

        Assert.Equal("information", data["Logging:LogLevel:Default"]);
        Assert.Equal("30", data["Ui:StaleAfterMinutes"]);
        Assert.Equal("true", data["Realm:Ui:HistoryTokens"]);
        Assert.Equal("monday", data["Driving:WeekStart"]);
        Assert.Equal(482.8032, double.Parse(data["Trips:MinDistanceM"] ?? string.Empty, CultureInfo.InvariantCulture), 4);
        Assert.Equal("device_tracker.alden_tablet", data["Ignore:Entities:0"]);
        Assert.Equal("#C792EA", data["Members:1:Color"]);
        Assert.Equal("38.5000", data["Members:4:StaticLatitude"]);
        Assert.Equal("fordpass_demo", data["Vehicles:0:EntityPrefix"]);
        Assert.Equal("zone.skate_one", data["Places:4:Zone"]);
        Assert.Equal(string.Empty, data["Me:Fallback"]);
    }

    [Fact]
    public void A_layer_over_the_options_file_wins_and_a_key_that_is_not_in_the_table_is_ignored()
    {
        using var document = JsonDocument.Parse("""{ "ui_history_tokens": true, "life360_use_rest": true, "bogus": 1 }""");
        var flat = OptionsBinding.Flatten(document.RootElement);
        Assert.False(flat.ContainsKey("Life360:UseRest"));
        Assert.False(flat.ContainsKey("Bogus"));
        Assert.Equal(OptionsBinding.Table.Count(row => row.Kind is not (OptionKind.TextList or OptionKind.ObjectList)), flat.Count); // the scalar paths of the table and nothing else

        // The environment variable Realm__Ui__HistoryTokens loads after the options file (03 section 2.1).
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(flat)
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Realm:Ui:HistoryTokens"] = "false" })
            .Build();

        Assert.False(OptionsBinding.Bind(configuration).UiHistoryTokens);
    }

    [Fact]
    public void A_value_of_the_wrong_type_names_the_key_and_never_the_value()
    {
        var text = Assert.Throws<FormatException>(() => OptionsBinding.Parse("""{ "ui_stale_after_minutes": "thirty-secret" }"""));
        var list = Assert.Throws<FormatException>(() => OptionsBinding.Parse("""{ "members": { "id": "secret-king" } }"""));
        var root = Assert.Throws<FormatException>(() => OptionsBinding.Parse("[1]"));

        Assert.Contains("ui_stale_after_minutes", text.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("thirty-secret", text.Message, StringComparison.Ordinal);
        Assert.Contains("members", list.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-king", list.Message, StringComparison.Ordinal);
        Assert.Contains("JSON object", root.Message, StringComparison.Ordinal);

        // A configuration layer can hold a value the file never had: the same rule applies when it is read back.
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Ui:StaleAfterMinutes"] = "abc-secret" })
            .Build();
        var bound = Assert.Throws<FormatException>(() => OptionsBinding.Bind(configuration));
        Assert.Contains("Ui:StaleAfterMinutes", bound.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("abc-secret", bound.Message, StringComparison.Ordinal);
        var weekStart = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Driving:WeekStart"] = "tuesday" }).Build();
        Assert.Contains("Driving:WeekStart", Assert.Throws<FormatException>(() => OptionsBinding.Bind(weekStart)).Message, StringComparison.Ordinal); // monday or sunday only
    }

    [Fact]
    public void Duplicate_ids_refuse_to_start_and_the_message_names_the_key_not_the_id()
    {
        var options = OptionsBinding.Defaults with
        {
            Members = [Member("secret_king"), Member("queen"), Member("secret_king")],
            Vehicles = [Vehicle("secret_wagon"), Vehicle("secret_wagon")],
        };

        var validation = OptionsValidator.Validate(options);

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Errors, error => error.StartsWith("members[2].id", StringComparison.Ordinal));
        Assert.Contains(validation.Errors, error => error.StartsWith("vehicles[1].id", StringComparison.Ordinal));
        Assert.Equal(2, validation.Errors.Count);
        Assert.DoesNotContain(validation.Errors, error => error.Contains("secret", StringComparison.Ordinal));
        Assert.Empty(validation.Warnings);
    }

    [Fact]
    public void A_fordpass_vehicle_needs_an_entity_prefix()
    {
        var options = OptionsBinding.Defaults with
        {
            Vehicles =
            [
                Vehicle("wagon", "fordpass", prefix: "fordpass_demo"),
                Vehicle("lorry", "fordpass", prefix: null),
                Vehicle("cart", "fordpass", prefix: "  "),
                Vehicle("chariot", "none", prefix: null),
            ],
        };

        var validation = OptionsValidator.Validate(options);

        Assert.Equal(
            new[]
            {
                "vehicles[1].entity_prefix: required when integration is fordpass",
                "vehicles[2].entity_prefix: required when integration is fordpass",
            },
            validation.Errors.ToArray());
    }

    [Theory]
    [InlineData(1, 59, true)]
    [InlineData(1, 60, false)]
    [InlineData(1, 61, false)]
    [InlineData(24, 30, true)]
    [InlineData(2, 120, false)]
    [InlineData(2, 119, true)]
    public void The_offline_threshold_must_be_above_the_stale_threshold(int offlineHours, int staleMinutes, bool valid)
    {
        var options = OptionsBinding.Defaults with { UiOfflineAfterHours = offlineHours, UiStaleAfterMinutes = staleMinutes };

        var validation = OptionsValidator.Validate(options);

        Assert.Equal(valid, validation.IsValid);
        if (!valid)
        {
            var error = Assert.Single(validation.Errors);
            Assert.StartsWith("ui_offline_after_hours", error, StringComparison.Ordinal);
            Assert.Contains("ui_stale_after_minutes", error, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_static_member_without_both_coordinates_and_an_unknown_fallback_member_warn_and_start_goes_on()
    {
        var options = OptionsBinding.Defaults with
        {
            Members =
            [
                Member("king"),
                Member("prince", MemberKind.Static, latitude: 38.5, longitude: null),
                Member("princess", MemberKind.Static, latitude: null, longitude: -98.5),
                Member("elder", MemberKind.Static, latitude: 38.5, longitude: -98.5),
            ],
            MeFallbackMember = "ghost",
        };

        var validation = OptionsValidator.Validate(options);

        Assert.True(validation.IsValid);
        Assert.Equal(3, validation.Warnings.Count);
        Assert.StartsWith("members[1].static_latitude", validation.Warnings[0], StringComparison.Ordinal);
        Assert.StartsWith("members[2].static_latitude", validation.Warnings[1], StringComparison.Ordinal);
        Assert.StartsWith("me_fallback_member", validation.Warnings[2], StringComparison.Ordinal);
        Assert.DoesNotContain(validation.Warnings, warning => warning.Contains("ghost", StringComparison.Ordinal));
        Assert.DoesNotContain(OptionsValidator.Validate(options with { MeFallbackMember = "king" }).Warnings, warning => warning.StartsWith("me_fallback_member", StringComparison.Ordinal));
    }

    private static MemberOption Member(string id, MemberKind kind = MemberKind.Live, double? latitude = null, double? longitude = null)
    {
        return new MemberOption(id, id, null, kind, null, null, null, "auto", null, null, true, null, null, latitude, longitude, false);
    }

    private static VehicleOption Vehicle(string id, string integration = "none", string? prefix = null)
    {
        return new VehicleOption(id, id, null, VehicleGlyph.Car, integration, prefix, null, null);
    }

    // A JSON literal and the text the flattened path should hold for it: another value than the default, of the row's kind.
    private static (string Json, string Text) Sample(OptionBinding row)
    {
        return row.Kind switch
        {
            OptionKind.Integer => ("7", "7"),
            OptionKind.Number => ("2.5", "2.5"),
            OptionKind.Flag => row.Default == "true" ? ("false", "false") : ("true", "true"),
            _ => ("\"sample-text\"", "sample-text"),
        };
    }

    // A row with a unit factor holds the converted number at its path; any other row holds the text as given.
    private static void AssertBound(OptionBinding row, string source, string? bound)
    {
        if (row.Factor is { } factor)
        {
            Assert.Equal(double.Parse(source, CultureInfo.InvariantCulture) * factor, double.Parse(bound ?? string.Empty, CultureInfo.InvariantCulture), 6);
        }
        else
        {
            Assert.Equal(source, bound);
        }
    }

    private static string FindRepoFile(string relativePath)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException("Not found in any folder above the test binaries: " + relativePath);
    }
}
