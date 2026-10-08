using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Realm.Infrastructure.Options;
using Xunit;

namespace Realm.Data.Tests;

// The options loader (02 sections 3.1 to 3.5): the binding table of the six add-on options (D114), the defaults, the old options that are ignored, and the cross-field validation.
public class OptionsBindingTests
{
    // The /data/options.json of 0.2.0.
    private const string Example = """
        {
          "demo_mode": false,
          "allow_demo_param": false,
          "log_level": "information",
          "driving_week_start": "monday",
          "driving_speeding_mph": 80,
          "retention_fix_days": 120
        }
        """;

    // The /data/options.json an install of 0.1 left behind: every option that 0.2.0 removed is still in it.
    private const string OldOptions = """
        {
          "log_level": "debug",
          "ui_stale_after_minutes": 31,
          "ui_offline_after_hours": 25,
          "ui_vehicle_stale_after_minutes": 46,
          "ui_history_tokens": false,
          "features_temp_bubble": true,
          "fusion_stale_grace_minutes": 11,
          "trips_start_speed_mph": 20,
          "driving_week_start": "sunday",
          "driving_speeding_mph": 70,
          "retention_fix_days": 121,
          "backfill_days": 11,
          "privacy_log_positions": true,
          "demo_mode": true,
          "allow_demo_param": true,
          "me_fallback_member": "queen",
          "ignore_entities": ["device_tracker.alden_tablet"],
          "members": [ { "id": "king", "display_name": "Alden", "life360_tracker": "device_tracker.life360_alden" } ],
          "vehicles": [ { "id": "wagon", "name": "Ford Pickup", "integration": "fordpass", "entity_prefix": "fordpass_demo" } ],
          "places": [ { "zone": "zone.home", "display_name": "Hearth Haven" } ]
        }
        """;

    public static TheoryData<string> Keys
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var row in OptionsBinding.Table)
            {
                data.Add(row.Key);
            }

            return data;
        }
    }

    [Fact]
    public void There_are_exactly_six_options()
    {
        Assert.Equal(
            new[] { "log_level", "driving_week_start", "driving_speeding_mph", "retention_fix_days", "demo_mode", "allow_demo_param" },
            OptionsBinding.Table.Select(row => row.Key).ToArray());
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
            Assert.Equal(defaultValue.ValueKind == JsonValueKind.String ? defaultValue.GetString() : defaultValue.GetRawText(), row.Default);
        }
    }

    [Fact]
    public void The_options_in_config_yaml_are_the_keys_of_the_table()
    {
        var yaml = File.ReadAllLines(FindRepoFile("realm/config.yaml"));
        var options = KeysUnder(yaml, "options:");
        var schema = KeysUnder(yaml, "schema:");

        Assert.Equal(OptionsBinding.Table.Select(row => row.Key).Order(StringComparer.Ordinal), options.Order(StringComparer.Ordinal));
        Assert.Equal(options.Order(StringComparer.Ordinal), schema.Order(StringComparer.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Keys))]
    public void A_key_is_bound_to_its_path_with_its_default_and_with_a_value(string key)
    {
        var row = OptionsBinding.Table.Single(candidate => candidate.Key == key);
        using var empty = JsonDocument.Parse("{}");
        var (json, text) = Sample(row);
        using var set = JsonDocument.Parse("{ \"" + key + "\": " + json + " }");

        AssertBound(row, row.Default, OptionsBinding.Flatten(empty.RootElement)[row.Path]);
        AssertBound(row, text, OptionsBinding.Flatten(set.RootElement)[row.Path]);
    }

    [Fact]
    public void An_empty_file_binds_every_default_of_the_table()
    {
        var options = OptionsBinding.Parse("{}");

        Assert.Equal(LogLevel.Information, options.LogLevel);
        Assert.Equal(DayOfWeek.Monday, options.DrivingWeekStart);
        Assert.Equal(35.7632, options.DrivingSpeedingMps, 4); // 80 mph
        Assert.Equal(120, options.RetentionFixDays);
        Assert.False(options.DemoMode);
        Assert.False(options.AllowDemoParam);

        var defaults = OptionsBinding.Defaults;
        Assert.Equal(options.DrivingWeekStart, defaults.DrivingWeekStart);
        Assert.True(OptionsValidator.Validate(defaults).IsValid);
    }

    // The values of the options that are gone are fixed in code (D114): the thresholds the Settings and the rules read.
    [Fact]
    public void The_removed_options_are_fixed_defaults()
    {
        var options = OptionsBinding.Defaults;

        Assert.Equal(30, options.UiStaleAfterMinutes);
        Assert.Equal(24, options.UiOfflineAfterHours);
        Assert.Equal(45, options.UiVehicleStaleAfterMinutes);
        Assert.Equal(15, options.UiLowBatteryPercent);
        Assert.Equal(500, options.UiPoorAccuracyMeters);
        Assert.Equal(40, options.UiDefaultViewRadiusKm);
        Assert.Equal(5.0, options.UiMaxZoneRadiusKm);
        Assert.Equal(80, options.UiFarAwayKm);
        Assert.True(options.UiHistoryTokens);
        Assert.Equal(10, options.FusionStaleGraceMinutes);
        Assert.Equal(6.7056, options.TripsStartSpeedMps, 4); // 15 mph
        Assert.Equal(180, options.TripsStopMergeSeconds);
        Assert.Equal(482.8032, options.TripsMinDistanceM, 4); // 0.3 mi
        Assert.Equal(120, options.TripsMinDurationSeconds);
        Assert.Equal(30, options.DrivingSpeedingMinSeconds);
        Assert.Equal(10, options.DrivingPhoneMinSeconds);
        Assert.Equal(10, options.BackfillDays);
        Assert.False(options.PrivacyLogPositions);
    }

    [Fact]
    public void Every_key_set_in_the_file_reaches_its_property()
    {
        var options = OptionsBinding.Parse(
            """
            { "log_level": "debug", "driving_week_start": "sunday", "driving_speeding_mph": 70, "retention_fix_days": 121, "demo_mode": true, "allow_demo_param": true }
            """);

        Assert.Equal(LogLevel.Debug, options.LogLevel);
        Assert.Equal(DayOfWeek.Sunday, options.DrivingWeekStart);
        Assert.Equal(31.2928, options.DrivingSpeedingMps, 4); // 70 mph x 0.44704
        Assert.Equal(121, options.RetentionFixDays);
        Assert.True(options.DemoMode);
        Assert.True(options.AllowDemoParam);
    }

    [Fact]
    public void The_options_json_of_0_2_0_loads_and_validates()
    {
        var options = OptionsBinding.Parse(Example);

        Assert.Equal(OptionsBinding.Defaults.DrivingSpeedingMps, options.DrivingSpeedingMps);
        var validation = OptionsValidator.Validate(options);
        Assert.True(validation.IsValid);
        Assert.Empty(validation.Errors);
        Assert.Empty(validation.Warnings);
    }

    [Fact]
    public void The_options_of_an_older_version_are_ignored_but_the_six_still_apply()
    {
        using var document = JsonDocument.Parse(OldOptions);

        var flat = OptionsBinding.Flatten(document.RootElement);
        var options = OptionsBinding.Parse(OldOptions);

        Assert.Equal(OptionsBinding.Table.Count, flat.Count);   // the paths of the table and nothing else
        Assert.Equal(LogLevel.Debug, options.LogLevel);
        Assert.Equal(DayOfWeek.Sunday, options.DrivingWeekStart);
        Assert.Equal(121, options.RetentionFixDays);
        Assert.True(options.DemoMode);
        Assert.Equal(30, options.UiStaleAfterMinutes);   // the removed option has no effect
        Assert.True(options.UiHistoryTokens);
        Assert.True(OptionsValidator.Validate(options).IsValid);
    }

    [Fact]
    public void A_key_that_is_not_in_the_table_is_ignored_whatever_its_value()
    {
        using var document = JsonDocument.Parse("""{ "log_level": "warning", "life360_use_rest": true, "bogus": 1, "members": { "id": "secret-king" }, "places": 7 }""");

        var flat = OptionsBinding.Flatten(document.RootElement);

        Assert.Equal(OptionsBinding.Table.Count, flat.Count);
        Assert.Equal("warning", flat["Logging:LogLevel:Default"]);
        Assert.False(flat.ContainsKey("Members:Id"));
    }

    [Fact]
    public void A_layer_over_the_options_file_wins()
    {
        using var document = JsonDocument.Parse("""{ "retention_fix_days": 100 }""");
        var flat = OptionsBinding.Flatten(document.RootElement);

        // The environment variable Retention__FixDays loads after the options file (03 section 2.1).
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(flat)
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Retention:FixDays"] = "200" })
            .Build();

        Assert.Equal(200, OptionsBinding.Bind(configuration).RetentionFixDays);
    }

    [Fact]
    public void A_value_of_the_wrong_type_names_the_key_and_never_the_value()
    {
        var text = Assert.Throws<FormatException>(() => OptionsBinding.Parse("""{ "retention_fix_days": "thirty-secret" }"""));
        var root = Assert.Throws<FormatException>(() => OptionsBinding.Parse("[1]"));

        Assert.Contains("retention_fix_days", text.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("thirty-secret", text.Message, StringComparison.Ordinal);
        Assert.Contains("JSON object", root.Message, StringComparison.Ordinal);

        // A configuration layer can hold a value the file never had: the same rule applies when it is read back.
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Retention:FixDays"] = "abc-secret" })
            .Build();
        var bound = Assert.Throws<FormatException>(() => OptionsBinding.Bind(configuration));
        Assert.Contains("Retention:FixDays", bound.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("abc-secret", bound.Message, StringComparison.Ordinal);
        var weekStart = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Driving:WeekStart"] = "tuesday" }).Build();
        Assert.Contains("Driving:WeekStart", Assert.Throws<FormatException>(() => OptionsBinding.Bind(weekStart)).Message, StringComparison.Ordinal); // monday or sunday only
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

    // The top-level keys under a heading of config.yaml, two-space indented.
    private static List<string> KeysUnder(string[] lines, string heading)
    {
        var keys = new List<string>();
        var inside = false;
        foreach (var line in lines)
        {
            if (line.StartsWith(heading, StringComparison.Ordinal))
            {
                inside = true;
                continue;
            }

            if (inside && line.Length > 0 && !char.IsWhiteSpace(line[0]))
            {
                break;
            }

            if (inside && line.StartsWith("  ", StringComparison.Ordinal) && line.Length > 2 && line[2] != ' ' && line[2] != '#')
            {
                keys.Add(line.Trim().Split(':')[0]);
            }
        }

        return keys;
    }

    // A JSON literal and the text the flattened path should hold for it: another value than the default, of the row's kind.
    private static (string Json, string Text) Sample(OptionBinding row)
    {
        return row.Kind switch
        {
            OptionKind.Integer => ("7", "7"),
            OptionKind.Number => ("90", "90"),
            OptionKind.Flag => row.Default == "true" ? ("false", "false") : ("true", "true"),
            _ => row.Key == "driving_week_start" ? ("\"sunday\"", "sunday") : ("\"debug\"", "debug"),
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
