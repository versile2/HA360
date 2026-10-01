using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Realm.Domain;

namespace Realm.Infrastructure.Options;

/// <summary>
/// The options loader (02 section 3.4), driven by one table: every flat add-on option key, its .NET configuration path and its default.
/// <see cref="Flatten"/> turns the Supervisor's <c>options.json</c> into configuration data at those paths (the job of the options configuration
/// provider, 03 section 2.1); <see cref="Bind"/> reads <see cref="RealmOptions"/> back from any <see cref="IConfiguration"/>, so an environment
/// variable that is layered over the file wins. A key that is absent reads as its default: the optional <c>me_fallback_member</c> (R-068) as empty,
/// a list as no entries. A value of the wrong type throws a <see cref="FormatException"/> that names the key, never the value.
/// </summary>
public static class OptionsBinding
{
    private const double MphToMps = 0.44704;
    private const double MilesToMetres = 1609.344;

    // 02 section 3.4. tools/ci/option-bindings.json carries the same table for the repository guards; OptionsBindingTests compares the two.
    private static readonly IReadOnlyList<OptionBinding> Rows =
    [
        new("log_level", "Logging:LogLevel:Default", OptionKind.Text, "Information"),
        new("ui_stale_after_minutes", "Ui:StaleAfterMinutes", OptionKind.Integer, "30"),
        new("ui_offline_after_hours", "Ui:OfflineAfterHours", OptionKind.Integer, "24"),
        new("ui_vehicle_stale_after_minutes", "Ui:VehicleStaleAfterMinutes", OptionKind.Integer, "45"),
        new("ui_low_battery_percent", "Ui:LowBatteryPercent", OptionKind.Integer, "15"),
        new("ui_poor_accuracy_meters", "Ui:PoorAccuracyMeters", OptionKind.Integer, "500"),
        new("ui_default_view_radius_km", "Ui:DefaultViewRadiusKm", OptionKind.Integer, "40"),
        new("ui_max_zone_radius_km", "Ui:MaxZoneRadiusKm", OptionKind.Number, "5"),
        new("ui_far_away_km", "Ui:FarAwayKm", OptionKind.Integer, "80"),
        new("ui_history_tokens", "Realm:Ui:HistoryTokens", OptionKind.Flag, "true"),
        new("features_temp_bubble", "Features:TempBubble", OptionKind.Flag, "false"),
        new("features_add_rows", "Features:AddRows", OptionKind.Flag, "false"),
        new("fusion_stale_grace_minutes", "Fusion:StaleGraceMinutes", OptionKind.Integer, "10"),
        new("trips_start_speed_mph", "Trips:StartSpeedMps", OptionKind.Number, "15", MphToMps),
        new("trips_stop_merge_seconds", "Trips:StopMergeSeconds", OptionKind.Integer, "180"),
        new("trips_min_distance_miles", "Trips:MinDistanceM", OptionKind.Number, "0.3", MilesToMetres),
        new("trips_min_duration_seconds", "Trips:MinDurationS", OptionKind.Integer, "120"),
        new("driving_week_start", "Driving:WeekStart", OptionKind.Text, "monday"),
        new("driving_speeding_mph", "Driving:SpeedingMps", OptionKind.Number, "80", MphToMps),
        new("driving_speeding_min_seconds", "Driving:SpeedingMinS", OptionKind.Integer, "30"),
        new("driving_phone_min_seconds", "Driving:PhoneMinS", OptionKind.Integer, "10"),
        new("retention_fix_days", "Retention:FixDays", OptionKind.Integer, "120"),
        new("backfill_days", "Backfill:Days", OptionKind.Integer, "10"),
        new("privacy_log_positions", "Privacy:LogPositions", OptionKind.Flag, "false"),
        new("demo_mode", "Demo:Mode", OptionKind.Flag, "false"),
        new("allow_demo_param", "Demo:AllowParam", OptionKind.Flag, "false"),
        new("me_fallback_member", "Me:Fallback", OptionKind.Text, string.Empty, Optional: true),
        new("ignore_entities", "Ignore:Entities", OptionKind.TextList, string.Empty),
        new("members", "Members", OptionKind.ObjectList, string.Empty),
        new("vehicles", "Vehicles", OptionKind.ObjectList, string.Empty),
        new("places", "Places", OptionKind.ObjectList, string.Empty),
    ];

    private static readonly Dictionary<string, OptionBinding> ByKey = Rows.ToDictionary(row => row.Key, StringComparer.Ordinal);

    /// <summary>The binding table of 02 section 3.4, in the order of <c>config.yaml</c>.</summary>
    public static IReadOnlyList<OptionBinding> Table => Rows;

    /// <summary>Every option at its default: what <see cref="Bind"/> reads from an empty configuration.</summary>
    public static RealmOptions Defaults => Bind(new ConfigurationBuilder().Build());

    /// <summary>
    /// Maps the <c>options.json</c> object onto the configuration paths of the table: every scalar key gets a value (its default when absent, the
    /// unit factor applied), a list becomes indexed entries (<c>Members:0:DisplayName</c>). Keys that are not in the table are ignored.
    /// </summary>
    public static IReadOnlyDictionary<string, string?> Flatten(JsonElement options)
    {
        if (options.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException("The options file must hold one JSON object");
        }

        var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var binding in Rows)
        {
            var present = options.TryGetProperty(binding.Key, out var value) && value.ValueKind != JsonValueKind.Null;
            switch (binding.Kind)
            {
                case OptionKind.TextList:
                    if (present)
                    {
                        FlattenTextList(binding, value, data);
                    }

                    break;
                case OptionKind.ObjectList:
                    if (present)
                    {
                        FlattenObjectList(binding, value, data);
                    }

                    break;
                default:
                    data[binding.Path] = Scale(binding, present ? ScalarText(binding, value) : binding.Default);
                    break;
            }
        }

        return data;
    }

    /// <summary>Reads the typed options from the paths of the table; a path that is absent reads as the default of its row.</summary>
    public static RealmOptions Bind(IConfiguration configuration)
    {
        return new RealmOptions
        {
            LogLevel = ReadLogLevel(configuration),
            UiStaleAfterMinutes = ReadInteger(configuration, "ui_stale_after_minutes"),
            UiOfflineAfterHours = ReadInteger(configuration, "ui_offline_after_hours"),
            UiVehicleStaleAfterMinutes = ReadInteger(configuration, "ui_vehicle_stale_after_minutes"),
            UiLowBatteryPercent = ReadInteger(configuration, "ui_low_battery_percent"),
            UiPoorAccuracyMeters = ReadInteger(configuration, "ui_poor_accuracy_meters"),
            UiDefaultViewRadiusKm = ReadInteger(configuration, "ui_default_view_radius_km"),
            UiMaxZoneRadiusKm = ReadNumber(configuration, "ui_max_zone_radius_km"),
            UiFarAwayKm = ReadInteger(configuration, "ui_far_away_km"),
            UiHistoryTokens = ReadFlag(configuration, "ui_history_tokens"),
            FeaturesTempBubble = ReadFlag(configuration, "features_temp_bubble"),
            FeaturesAddRows = ReadFlag(configuration, "features_add_rows"),
            FusionStaleGraceMinutes = ReadInteger(configuration, "fusion_stale_grace_minutes"),
            TripsStartSpeedMps = ReadNumber(configuration, "trips_start_speed_mph"),
            TripsStopMergeSeconds = ReadInteger(configuration, "trips_stop_merge_seconds"),
            TripsMinDistanceM = ReadNumber(configuration, "trips_min_distance_miles"),
            TripsMinDurationSeconds = ReadInteger(configuration, "trips_min_duration_seconds"),
            DrivingWeekStart = ReadWeekStart(configuration),
            DrivingSpeedingMps = ReadNumber(configuration, "driving_speeding_mph"),
            DrivingSpeedingMinSeconds = ReadInteger(configuration, "driving_speeding_min_seconds"),
            DrivingPhoneMinSeconds = ReadInteger(configuration, "driving_phone_min_seconds"),
            RetentionFixDays = ReadInteger(configuration, "retention_fix_days"),
            BackfillDays = ReadInteger(configuration, "backfill_days"),
            PrivacyLogPositions = ReadFlag(configuration, "privacy_log_positions"),
            DemoMode = ReadFlag(configuration, "demo_mode"),
            AllowDemoParam = ReadFlag(configuration, "allow_demo_param"),
            MeFallbackMember = ReadText(configuration, "me_fallback_member").Trim(),
            IgnoreEntities = ReadStrings(configuration, "ignore_entities"),
            Members = ReadItems(configuration, "members", BindMember),
            Vehicles = ReadItems(configuration, "vehicles", BindVehicle),
            Places = ReadItems(configuration, "places", BindPlace),
        };
    }

    /// <summary>Loads the text of an <c>options.json</c>: <see cref="Flatten"/> and <see cref="Bind"/> in one step.</summary>
    public static RealmOptions Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(Flatten(document.RootElement)).Build();
        return Bind(configuration);
    }

    private static string ScalarText(OptionBinding binding, JsonElement value)
    {
        return binding.Kind switch
        {
            OptionKind.Text when value.ValueKind == JsonValueKind.String => value.GetString() ?? string.Empty,
            OptionKind.Integer when value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var whole) => whole.ToString(CultureInfo.InvariantCulture),
            OptionKind.Number when value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) => number.ToString("R", CultureInfo.InvariantCulture),
            OptionKind.Flag when value.ValueKind is JsonValueKind.True or JsonValueKind.False => value.GetBoolean() ? "true" : "false",
            _ => throw WrongType(binding),
        };
    }

    // The unit factor of the row, applied to the default and to a value from the file; a value already at the path was scaled when it was flattened.
    private static string Scale(OptionBinding binding, string text)
    {
        if (binding.Factor is not { } factor)
        {
            return text;
        }

        var value = double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
        return (value * factor).ToString("R", CultureInfo.InvariantCulture);
    }

    private static void FlattenTextList(OptionBinding binding, JsonElement value, Dictionary<string, string?> data)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            throw WrongType(binding);
        }

        var index = 0;
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                throw WrongType(binding);
            }

            data[Indexed(binding.Path, index)] = item.GetString();
            index++;
        }
    }

    private static void FlattenObjectList(OptionBinding binding, JsonElement value, Dictionary<string, string?> data)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            throw WrongType(binding);
        }

        var index = 0;
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                throw WrongType(binding);
            }

            foreach (var property in item.EnumerateObject())
            {
                var text = property.Value.ValueKind switch
                {
                    JsonValueKind.String => property.Value.GetString(),
                    JsonValueKind.Number => property.Value.GetRawText(),
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    JsonValueKind.Null => null,
                    _ => throw WrongType(binding),
                };
                if (text is not null)
                {
                    data[Indexed(binding.Path, index) + ":" + PascalCase(property.Name)] = text;
                }
            }

            index++;
        }
    }

    // 02 section 3.4: "keys PascalCased", so life360_tracker is Life360Tracker.
    private static string PascalCase(string snake)
    {
        var builder = new StringBuilder(snake.Length);
        var upper = true;
        foreach (var letter in snake)
        {
            if (letter == '_')
            {
                upper = true;
                continue;
            }

            builder.Append(upper ? char.ToUpperInvariant(letter) : letter);
            upper = false;
        }

        return builder.ToString();
    }

    private static string Indexed(string path, int index)
    {
        return string.Create(CultureInfo.InvariantCulture, $"{path}:{index}");
    }

    private static FormatException WrongType(OptionBinding binding)
    {
        return new FormatException($"The option {binding.Key} has the wrong type of value");
    }

    private static FormatException NotValid(string path)
    {
        return new FormatException($"The option at {path} is not a valid value");
    }

    private static string ReadText(IConfiguration configuration, string key)
    {
        var binding = ByKey[key];
        return configuration[binding.Path] ?? Scale(binding, binding.Default);
    }

    private static int ReadInteger(IConfiguration configuration, string key)
    {
        return int.TryParse(ReadText(configuration, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw NotValid(ByKey[key].Path);
    }

    private static double ReadNumber(IConfiguration configuration, string key)
    {
        return double.TryParse(ReadText(configuration, key), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw NotValid(ByKey[key].Path);
    }

    private static bool ReadFlag(IConfiguration configuration, string key)
    {
        return bool.TryParse(ReadText(configuration, key), out var value) ? value : throw NotValid(ByKey[key].Path);
    }

    private static LogLevel ReadLogLevel(IConfiguration configuration)
    {
        return Enum.TryParse<LogLevel>(ReadText(configuration, "log_level"), ignoreCase: true, out var level)
            ? level
            : throw NotValid(ByKey["log_level"].Path);
    }

    private static DayOfWeek ReadWeekStart(IConfiguration configuration)
    {
        var text = ReadText(configuration, "driving_week_start");
        if (string.Equals(text, "monday", StringComparison.OrdinalIgnoreCase))
        {
            return DayOfWeek.Monday;
        }

        return string.Equals(text, "sunday", StringComparison.OrdinalIgnoreCase)
            ? DayOfWeek.Sunday
            : throw NotValid(ByKey["driving_week_start"].Path);
    }

    private static string[] ReadStrings(IConfiguration configuration, string key)
    {
        return configuration.GetSection(ByKey[key].Path).GetChildren()
            .Select(section => section.Value)
            .OfType<string>()
            .Select(value => value.Trim())
            .Where(value => value.Length > 0)
            .ToArray();
    }

    private static T[] ReadItems<T>(IConfiguration configuration, string key, Func<IConfigurationSection, T> bind)
    {
        return configuration.GetSection(ByKey[key].Path).GetChildren().Select(bind).ToArray();
    }

    private static MemberOption BindMember(IConfigurationSection section)
    {
        var kind = Optional(section, "Kind");
        return new MemberOption(
            Id: Required(section, "Id"),
            DisplayName: Required(section, "DisplayName"),
            LoreTitle: Optional(section, "LoreTitle"),
            Kind: kind is null || string.Equals(kind, "live", StringComparison.OrdinalIgnoreCase)
                ? MemberKind.Live
                : string.Equals(kind, "static", StringComparison.OrdinalIgnoreCase) ? MemberKind.Static : throw NotValid(section.Path + ":Kind"),
            Person: Optional(section, "Person"),
            Life360Tracker: Optional(section, "Life360Tracker"),
            CompanionTracker: Optional(section, "CompanionTracker"),
            Avatar: Optional(section, "Avatar")?.ToLowerInvariant() ?? "auto",
            Color: Optional(section, "Color"),
            SortOrder: OptionalInteger(section, "SortOrder"),
            InDrivingReport: OptionalFlag(section, "InDrivingReport", true),
            StaticLabel: Optional(section, "StaticLabel"),
            StaticAddress: Optional(section, "StaticAddress"),
            StaticLatitude: OptionalNumber(section, "StaticLatitude"),
            StaticLongitude: OptionalNumber(section, "StaticLongitude"),
            StaticShowAddress: OptionalFlag(section, "StaticShowAddress", false));
    }

    private static VehicleOption BindVehicle(IConfigurationSection section)
    {
        var glyph = Optional(section, "Glyph");
        return new VehicleOption(
            Id: Required(section, "Id"),
            Name: Required(section, "Name"),
            LoreTitle: Optional(section, "LoreTitle"),
            Glyph: glyph is null || string.Equals(glyph, "car", StringComparison.OrdinalIgnoreCase)
                ? VehicleGlyph.Car
                : string.Equals(glyph, "pickup", StringComparison.OrdinalIgnoreCase) ? VehicleGlyph.Pickup : throw NotValid(section.Path + ":Glyph"),
            Integration: Optional(section, "Integration")?.ToLowerInvariant() ?? "none",
            EntityPrefix: Optional(section, "EntityPrefix"),
            PlaceholderNote: Optional(section, "PlaceholderNote"),
            SortOrder: OptionalInteger(section, "SortOrder"));
    }

    private static PlaceOption BindPlace(IConfigurationSection section)
    {
        var kind = Optional(section, "Kind");
        return new PlaceOption(
            Zone: Required(section, "Zone"),
            DisplayName: Optional(section, "DisplayName"),
            Subtitle: Optional(section, "Subtitle"),
            Kind: kind is null ? PlaceKind.Other : Enum.TryParse<PlaceKind>(kind, ignoreCase: true, out var placeKind) ? placeKind : throw NotValid(section.Path + ":Kind"),
            Hidden: OptionalFlag(section, "Hidden", false));
    }

    private static string Required(IConfigurationSection section, string name)
    {
        return section[name]?.Trim() ?? string.Empty;
    }

    // A blank optional text is the same as an absent one ("blank = discover", 02 section 3.2).
    private static string? Optional(IConfigurationSection section, string name)
    {
        var text = section[name]?.Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    private static int? OptionalInteger(IConfigurationSection section, string name)
    {
        var text = Optional(section, name);
        if (text is null)
        {
            return null;
        }

        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : throw NotValid(section.Path + ":" + name);
    }

    private static double? OptionalNumber(IConfigurationSection section, string name)
    {
        var text = Optional(section, name);
        if (text is null)
        {
            return null;
        }

        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : throw NotValid(section.Path + ":" + name);
    }

    private static bool OptionalFlag(IConfigurationSection section, string name, bool fallback)
    {
        var text = Optional(section, name);
        if (text is null)
        {
            return fallback;
        }

        return bool.TryParse(text, out var value) ? value : throw NotValid(section.Path + ":" + name);
    }
}
