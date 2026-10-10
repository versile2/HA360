using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Realm.Infrastructure.Options;

/// <summary>
/// The options loader (02 section 3.4), driven by one table: the seven flat add-on option keys (D114; `allow_add` joined in 0.2.2, D120), their .NET configuration paths and their defaults.
/// <see cref="Flatten"/> turns the Supervisor's <c>options.json</c> into configuration data at those paths (the job of the options configuration
/// provider, 03 section 2.1); <see cref="Bind"/> reads <see cref="RealmOptions"/> back from any <see cref="IConfiguration"/>, so an environment
/// variable that is layered over the file wins. A key that is absent reads as its default. A key that is not in the table, such as an option of an older
/// version that is still in the user's <c>options.json</c>, is ignored whatever its value. A value of the wrong type throws a <see cref="FormatException"/>
/// that names the key, never the value.
/// </summary>
public static class OptionsBinding
{
    private const double MphToMps = 0.44704;

    // 02 section 3.4. tools/ci/option-bindings.json carries the same table for the repository guards; OptionsBindingTests compares the two.
    private static readonly IReadOnlyList<OptionBinding> Rows =
    [
        new("log_level", "Logging:LogLevel:Default", OptionKind.Text, "Information"),
        new("driving_week_start", "Driving:WeekStart", OptionKind.Text, "monday"),
        new("driving_speeding_mph", "Driving:SpeedingMps", OptionKind.Number, "80", MphToMps),
        new("retention_fix_days", "Retention:FixDays", OptionKind.Integer, "100"),
        new("demo_mode", "Demo:Mode", OptionKind.Flag, "false"),
        new("allow_demo_param", "Demo:AllowParam", OptionKind.Flag, "false"),
        new("allow_add", "Ui:AllowAdd", OptionKind.Flag, "true"),
    ];

    private static readonly Dictionary<string, OptionBinding> ByKey = Rows.ToDictionary(row => row.Key, StringComparer.Ordinal);

    /// <summary>The binding table of 02 section 3.4, in the order of <c>config.yaml</c>.</summary>
    public static IReadOnlyList<OptionBinding> Table => Rows;

    /// <summary>Every option at its default: what <see cref="Bind"/> reads from an empty configuration.</summary>
    public static RealmOptions Defaults => Bind(new ConfigurationBuilder().Build());

    /// <summary>
    /// Maps the <c>options.json</c> object onto the configuration paths of the table: every key gets a value (its default when absent, the
    /// unit factor applied). Keys that are not in the table are ignored.
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
            data[binding.Path] = Scale(binding, present ? ScalarText(binding, value) : binding.Default);
        }

        return data;
    }

    /// <summary>Reads the typed options from the paths of the table; a path that is absent reads as the default of its row.</summary>
    public static RealmOptions Bind(IConfiguration configuration)
    {
        return new RealmOptions
        {
            LogLevel = ReadLogLevel(configuration),
            DrivingWeekStart = ReadWeekStart(configuration),
            DrivingSpeedingMps = ReadNumber(configuration, "driving_speeding_mph"),
            RetentionFixDays = ReadInteger(configuration, "retention_fix_days"),
            DemoMode = ReadFlag(configuration, "demo_mode"),
            AllowDemoParam = ReadFlag(configuration, "allow_demo_param"),
            AllowAdd = ReadFlag(configuration, "allow_add"),
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
}
