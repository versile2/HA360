using System.Text.Json;
using Realm.Web.Hosting;

namespace Realm.Web;

/// <summary>
/// What the host needs before it is built (03 section 2.1). <see cref="Detect"/> reads only <c>demo_mode</c> from the
/// options file (04 G-12); the full options loader is S12's and the live wiring S13b's.
/// </summary>
/// <param name="Mode">Live or Demo.</param>
/// <param name="DetailedErrors">Circuit errors carry details (Demo mode and Development, 03 section 5.6).</param>
public sealed record RuntimeOptions(RealmMode Mode, bool DetailedErrors)
{
    private const string DefaultOptionsPath = "/data/options.json";

    /// <summary>
    /// Precedence (03 section 2.1): env <c>REALM_DATA_SOURCE</c> (<c>ha</c> or <c>demo</c>, the short form of
    /// <c>Realm:Mode</c>) beats the option <c>demo_mode</c> beats the token (<c>SUPERVISOR_TOKEN</c> or the dev
    /// <c>Realm:Ha:Token</c>): a token selects Live, no token selects Demo. Never throws, whatever the options file holds.
    /// </summary>
    public static RuntimeOptions Detect(IConfiguration configuration, IHostEnvironment environment)
    {
        var mode = DetectMode(configuration);
        return new RuntimeOptions(mode, mode == RealmMode.Demo || environment.IsDevelopment());
    }

    private static RealmMode DetectMode(IConfiguration configuration)
    {
        if (TryParseMode(configuration["REALM_DATA_SOURCE"], out var forced)
            || TryParseMode(configuration["Realm:Mode"], out forced))
        {
            return forced;
        }

        if (ReadDemoMode(OptionsPath(configuration)))
        {
            return RealmMode.Demo;
        }

        var hasToken = !string.IsNullOrEmpty(configuration["SUPERVISOR_TOKEN"])
            || !string.IsNullOrEmpty(configuration["Realm:Ha:Token"]);
        return hasToken ? RealmMode.Live : RealmMode.Demo;
    }

    private static bool TryParseMode(string? value, out RealmMode mode)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "demo":
                mode = RealmMode.Demo;
                return true;
            case "ha":
            case "live":
                mode = RealmMode.Live;
                return true;
            default:
                mode = default;
                return false;
        }
    }

    // Env REALM_OPTIONS is the short form of Realm:OptionsPath and overrides it (03 section 2.1, 02 section 3.4).
    private static string OptionsPath(IConfiguration configuration)
    {
        var fromEnvironment = configuration["REALM_OPTIONS"];
        if (!string.IsNullOrEmpty(fromEnvironment))
        {
            return fromEnvironment;
        }

        var configured = configuration["Realm:OptionsPath"];
        return string.IsNullOrEmpty(configured) ? DefaultOptionsPath : configured;
    }

    private static bool ReadDemoMode(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            using var stream = File.OpenRead(path);
            using var document = JsonDocument.Parse(stream);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("demo_mode", out var value)
                && value.ValueKind == JsonValueKind.True;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // An unreadable or malformed options file means "no demo_mode"; the host must still start.
            return false;
        }
    }
}
