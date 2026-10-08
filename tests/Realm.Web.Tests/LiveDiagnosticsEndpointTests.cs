using System.Net;
using System.Text.Json;
using Realm.Domain;
using Realm.Infrastructure.Diagnostics;
using Xunit;

namespace Realm.Web.Tests;

/// <summary>
/// <c>GET diagnostics.json</c> in Live (03 sections 2.11 and 7.5): the Live host registers the snapshot builder of the Infrastructure project, so the file answers
/// 200 with <c>"mode": "live"</c> where it used to be a 404. The test host points Home Assistant at an address that refuses at once and the database at a temp file,
/// so the connection is never up here; the tests pin the shape of 2.11, that the web host's own circuit counts are filled in, that a host whose options were
/// refused says so (R3-07), and that the file holds states, counts and codes only (D18, D38). The conditions of each warning code are pinned in
/// <c>DiagnosticsSnapshotBuilderTests</c>, where the clock and the state can be set.
/// </summary>
public sealed class LiveDiagnosticsEndpointTests
{
    private const string FictionalToken = "fictional-test-token-0013";

    private static readonly string[] TopLevel =
    [
        "schema", "version", "mode", "uptimeSeconds", "zone", "zoneDataOk", "circuits", "connections", "counts", "ha", "ingestion", "db", "members", "warnings",
    ];

    private static readonly string[] EightCodes =
    [
        WarningCodes.HaUnavailable,
        WarningCodes.HaAuthFailed,
        WarningCodes.HaWebsocketDownOver60s,
        WarningCodes.IngestDrops,
        WarningCodes.WriterQueueOver80Pct,
        WarningCodes.UncleanShutdown,
        WarningCodes.ZoneDataMissing,
        WarningCodes.PayloadSchemaMismatch,
    ];

    [Fact(DisplayName = "[X-09] GET diagnostics.json returns the schema of 2.11 in Live with mode live")]
    public async Task InLive_TheFileHasTheSchemaOfSection2_11_WithModeLive()
    {
        using var document = await GetAsync(LiveSettings());
        var root = document.RootElement;

        Assert.Equal(TopLevel, root.EnumerateObject().Select(property => property.Name));
        Assert.Equal(1, root.GetProperty("schema").GetInt32());
        Assert.Equal("live", root.GetProperty("mode").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("version").GetString()));
        Assert.True(root.GetProperty("uptimeSeconds").GetInt64() >= 0);
        Assert.Equal("UTC", root.GetProperty("zone").GetString());   // nothing has been discovered yet, so the zone is the default

        Assert.Equal(["open", "disconnected"], Names(root, "circuits"));
        Assert.Equal(["members", "vehicles", "places"], Names(root, "counts"));
        Assert.Equal(["websocketState", "reconnects", "lastMessageUtc", "watchedEntities", "messagesPerMinute"], Names(root, "ha"));
        Assert.Equal(["eventsPerMinute", "queueDepth", "dropped"], Names(root, "ingestion"));
        Assert.Equal(["schemaVersion", "uncleanShutdownAtStart", "sizeBytes", "writerQueueDepth", "lastCommitUtc"], Names(root, "db"));
        Assert.Equal(JsonValueKind.Array, root.GetProperty("members").ValueKind);
        Assert.Equal(JsonValueKind.Array, root.GetProperty("warnings").ValueKind);
    }

    [Fact]
    public async Task TheFile_IsJson_AndIsNeverCached()
    {
        await using var host = await RealmTestHost.StartAsync(settings: LiveSettings());
        using var client = host.CreateClient();

        using var response = await client.GetAsync("diagnostics.json");

        await RealmTestHost.EnsureSuccessAsync(host, response);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    [Fact]
    public async Task TheDatabaseSection_ShowsTheSchemaThatStartedAndAFileThatExists_OnANewDatabase()
    {
        using var document = await GetAsync(LiveSettings());
        var db = document.RootElement.GetProperty("db");

        Assert.True(db.GetProperty("schemaVersion").GetInt32() >= 1);
        Assert.False(db.GetProperty("uncleanShutdownAtStart").GetBoolean());
        Assert.True(db.GetProperty("sizeBytes").GetInt64() > 0);
        Assert.Equal(0, db.GetProperty("writerQueueDepth").GetInt32());
    }

    [Fact]
    public async Task TheHomeAssistantSection_IsAWordForAConnectionThatIsNotUp_AndNeverAnAddress()
    {
        using var document = await GetAsync(LiveSettings());
        var ha = document.RootElement.GetProperty("ha");

        // The test host's Home Assistant refuses at once, so the websocket is being reached (connecting, then reconnecting), and is never connected or refused.
        Assert.Contains(ha.GetProperty("websocketState").GetString(), new[] { "connecting", "authenticating", "reconnecting" });
        Assert.Equal(JsonValueKind.Null, ha.GetProperty("lastMessageUtc").ValueKind);
        Assert.Equal(0, ha.GetProperty("messagesPerMinute").GetInt32());
    }

    [Fact]
    public async Task TheCircuitCounts_AreTheWebHostsOwn_AndNoneIsOpenInATestThatOpensNoCircuit()
    {
        using var document = await GetAsync(LiveSettings());
        var circuits = document.RootElement.GetProperty("circuits");

        Assert.Equal(0, circuits.GetProperty("open").GetInt32());
        Assert.Equal(0, circuits.GetProperty("disconnected").GetInt32());
    }

    [Fact]
    public async Task TheWarnings_AreTheFixedCodesOfSection2_11_AndOnlyThose()
    {
        using var document = await GetAsync(LiveSettings());
        var root = document.RootElement;
        var warnings = root.GetProperty("warnings").EnumerateArray().Select(warning => warning.GetString()).ToArray();

        Assert.All(warnings, code => Assert.Contains(code, EightCodes));
        Assert.Equal(warnings.Distinct(StringComparer.Ordinal).Count(), warnings.Length);

        // Whatever else the first moments of a host raise, the connection is not refused and nothing was shut down badly on a database that is new.
        Assert.DoesNotContain(WarningCodes.HaAuthFailed, warnings);
        Assert.DoesNotContain(WarningCodes.HaWebsocketDownOver60s, warnings);
        Assert.DoesNotContain(WarningCodes.UncleanShutdown, warnings);
        Assert.DoesNotContain(WarningCodes.IngestDrops, warnings);
        Assert.DoesNotContain(WarningCodes.WriterQueueOver80Pct, warnings);
        Assert.DoesNotContain(WarningCodes.PayloadSchemaMismatch, warnings);

        // The zone flag and its warning say the same thing, whether or not the machine of the test has zone data.
        Assert.Equal(!root.GetProperty("zoneDataOk").GetBoolean(), warnings.Contains(WarningCodes.ZoneDataMissing));
    }

    // R3-07: options that fail the validation of 02 section 3.3 start no service that talks to Home Assistant, so nothing would ever replace the first snapshot.
    // The state is built as not configured, which reads Unavailable at once, and the file says so instead of showing a reconnection that never ends.
    [Fact(DisplayName = "[R3-07] A host whose options were refused reports Home Assistant as unavailable and the websocket as not configured")]
    public async Task OptionsThatFailValidation_ShowHomeAssistantUnavailableAtOnce_AndTheWebsocketAsNotConfigured()
    {
        using var document = await GetAsync(RefusedSettings());
        var root = document.RootElement;

        var homeAssistant = root.GetProperty("connections").EnumerateArray()
            .Single(connection => connection.GetProperty("name").GetString() == ConnectionNames.HomeAssistant);
        Assert.Equal("unavailable", homeAssistant.GetProperty("state").GetString());
        Assert.Equal("notConfigured", root.GetProperty("ha").GetProperty("websocketState").GetString());

        var warnings = root.GetProperty("warnings").EnumerateArray().Select(warning => warning.GetString()).ToArray();
        Assert.Contains(WarningCodes.HaUnavailable, warnings);
        Assert.DoesNotContain(WarningCodes.HaAuthFailed, warnings);
        Assert.DoesNotContain(WarningCodes.HaWebsocketDownOver60s, warnings);
        Assert.Equal("live", root.GetProperty("mode").GetString());
    }

    [Fact]
    public async Task OptionsThatPassValidation_DoNotRaiseHomeAssistantUnavailable_InTheFirstMomentsOfAHost()
    {
        using var document = await GetAsync(LiveSettings());

        // Reconnecting for the first 15 s of an outage (02 section 1.8): a test is far quicker than that.
        var warnings = document.RootElement.GetProperty("warnings").EnumerateArray().Select(warning => warning.GetString()).ToArray();
        Assert.DoesNotContain(WarningCodes.HaUnavailable, warnings);
    }

    [Fact]
    public async Task TheFile_HoldsNoLocationNoAddressNoNameAndNoToken()
    {
        await using var host = await RealmTestHost.StartAsync(settings: LiveSettings());
        using var client = host.CreateClient();

        using var response = await client.GetAsync("diagnostics.json");
        await RealmTestHost.EnsureSuccessAsync(host, response);
        var text = await response.Content.ReadAsStringAsync();

        foreach (var forbidden in new[] { "\"lat\"", "\"lon\"", "latitude", "longitude", "address", "battery", "token", "entity", FictionalToken, "127.0.0.1" })
        {
            Assert.DoesNotContain(forbidden, text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task TheFileOfARefusedHost_HoldsNoTokenEither()
    {
        await using var host = await RealmTestHost.StartAsync(settings: RefusedSettings());
        using var client = host.CreateClient();

        using var response = await client.GetAsync("diagnostics.json");
        await RealmTestHost.EnsureSuccessAsync(host, response);
        var text = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain(FictionalToken, text, StringComparison.Ordinal);
        Assert.DoesNotContain("token", text, StringComparison.OrdinalIgnoreCase);
    }

    private static Dictionary<string, string?> LiveSettings() => new() { ["SUPERVISOR_TOKEN"] = FictionalToken };

    // The refusal of the existing composition tests: an option that cannot be read (02 section 3.3).
    private static Dictionary<string, string?> RefusedSettings()
    {
        var settings = LiveSettings();
        settings["Retention:FixDays"] = "not-a-number";
        return settings;
    }

    private static string[] Names(JsonElement root, string property) => root.GetProperty(property).EnumerateObject().Select(member => member.Name).ToArray();

    private static async Task<JsonDocument> GetAsync(IReadOnlyDictionary<string, string?> settings)
    {
        await using var host = await RealmTestHost.StartAsync(settings: settings);
        using var client = host.CreateClient();

        using var response = await client.GetAsync("diagnostics.json");

        await RealmTestHost.EnsureSuccessAsync(host, response);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }
}
