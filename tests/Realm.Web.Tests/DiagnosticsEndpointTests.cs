using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Realm.Demo;
using Realm.Domain;
using Xunit;

namespace Realm.Web.Tests;

/// <summary>
/// <c>GET diagnostics.json</c> (03 sections 2.11 and 7.5): the one diagnostics surface of v1, served at a relative URL, in Demo from canned values with
/// <c>"mode": "demo"</c>. The tests pin the shape of 2.11 field by field and that the file holds states, counts and codes only: no coordinate, no address, no name,
/// no token. The circuit counts are the web host's own, filled in when the file is served.
/// </summary>
public sealed class DiagnosticsEndpointTests
{
    private static readonly string[] TopLevel =
    [
        "schema", "version", "mode", "uptimeSeconds", "zone", "zoneDataOk", "circuits", "connections", "counts", "ha", "ingestion", "db", "members", "warnings",
    ];

    [Fact(DisplayName = "[X-09] GET diagnostics.json returns the schema of 2.11 in Demo with mode demo and zoneDataOk true")]
    public async Task InDemo_TheFileHasTheSchemaOfSection2_11_WithModeDemoAndZoneDataOk()
    {
        using var document = await GetAsync();
        var root = document.RootElement;

        Assert.Equal(TopLevel, root.EnumerateObject().Select(property => property.Name));
        Assert.Equal(1, root.GetProperty("schema").GetInt32());
        Assert.Equal("demo", root.GetProperty("mode").GetString());
        Assert.True(root.GetProperty("zoneDataOk").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("version").GetString()));
        Assert.True(root.GetProperty("uptimeSeconds").GetInt64() >= 0);
        Assert.Equal(DemoDataSource.ZoneId, root.GetProperty("zone").GetString());

        Assert.Equal(["open", "disconnected"], Names(root, "circuits"));
        Assert.Equal(["members", "vehicles", "places"], Names(root, "counts"));
        Assert.Equal(["websocketState", "reconnects", "lastMessageUtc", "watchedEntities", "messagesPerMinute"], Names(root, "ha"));
        Assert.Equal(["eventsPerMinute", "queueDepth", "dropped"], Names(root, "ingestion"));
        Assert.Equal(["schemaVersion", "uncleanShutdownAtStart", "sizeBytes", "writerQueueDepth", "lastCommitUtc"], Names(root, "db"));
        Assert.Equal(JsonValueKind.Array, root.GetProperty("warnings").ValueKind);
    }

    [Fact]
    public async Task TheCounts_AreThoseOfTheDemoCast()
    {
        using var document = await GetAsync();
        var counts = document.RootElement.GetProperty("counts");

        Assert.Equal(DemoCast.Members.Count, counts.GetProperty("members").GetInt32());
        Assert.Equal(DemoCast.Vehicles.Count, counts.GetProperty("vehicles").GetInt32());
        Assert.Equal(DemoPlaces.Drawn.Count, counts.GetProperty("places").GetInt32());
    }

    [Fact]
    public async Task TheConnections_AreTheFourEntriesOfTheSettingsChips_WithTheirStateAsAWord()
    {
        using var document = await GetAsync();
        var connections = document.RootElement.GetProperty("connections").EnumerateArray().ToArray();

        Assert.Equal(
            [ConnectionNames.HomeAssistant, ConnectionNames.Life360Trackers, ConnectionNames.FordPass, ConnectionNames.VehiclePlaceholder],
            connections.Select(connection => connection.GetProperty("name").GetString()));
        Assert.Equal(["connected", "connected", "connected", "notConnected"], connections.Select(connection => connection.GetProperty("state").GetString()));
        Assert.All(connections, connection => Assert.Equal(["name", "state", "lastSyncUtc"], connection.EnumerateObject().Select(property => property.Name)));
    }

    [Fact]
    public async Task TheMembers_AreTheOptionSlugsWithAFreshnessAndAThreshold_AndNothingElse()
    {
        using var document = await GetAsync();
        var members = document.RootElement.GetProperty("members").EnumerateArray().ToArray();

        Assert.Equal(DemoCast.Members.Select(member => member.Id), members.Select(member => member.GetProperty("id").GetString()));
        Assert.All(members, member => Assert.Equal(["id", "freshness", "staleAfterMinutes"], member.EnumerateObject().Select(property => property.Name)));
        Assert.Equal("static", members.Single(member => member.GetProperty("id").GetString() == DemoCast.Prince.Id).GetProperty("freshness").GetString());
    }

    [Fact]
    public async Task TheFile_HoldsNoLocationNoAddressNoNameAndNoToken()
    {
        using var host = await RealmTestHost.StartAsync();
        using var client = host.CreateClient();

        using var response = await client.GetAsync("diagnostics.json");
        await RealmTestHost.EnsureSuccessAsync(host, response);
        var text = await response.Content.ReadAsStringAsync();

        foreach (var forbidden in new[] { "\"lat\"", "\"lon\"", "latitude", "longitude", "address", "battery", "token", "entity" })
        {
            Assert.DoesNotContain(forbidden, text, StringComparison.OrdinalIgnoreCase);
        }

        foreach (var member in DemoCast.Members)
        {
            Assert.DoesNotContain(member.Name, text, StringComparison.Ordinal);
            if (member.Address is not null)
            {
                Assert.DoesNotContain(member.Address, text, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public async Task TheFile_IsJson_AndIsNeverCached()
    {
        await using var host = await RealmTestHost.StartAsync();
        using var client = host.CreateClient();

        using var response = await client.GetAsync("diagnostics.json");

        await RealmTestHost.EnsureSuccessAsync(host, response);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    [Fact]
    public async Task TheCircuitCounts_AreTheWebHostsOwn_NotWhatTheDiagnosticsPortSays()
    {
        // A port that claims 7 open and 7 disconnected circuits: the host serves what its own circuit handler counts, which is none in a test that opens no circuit.
        var port = new CannedDiagnostics(new DemoDiagnostics().GetSnapshot() with { Circuits = new DiagnosticsSnapshot.CircuitCounts(7, 7) });
        await using var host = await RealmTestHost.StartAsync(configureServices: services => services.AddSingleton<IDiagnostics>(port));
        using var client = host.CreateClient();

        using var response = await client.GetAsync("diagnostics.json");
        await RealmTestHost.EnsureSuccessAsync(host, response);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        var circuits = document.RootElement.GetProperty("circuits");
        Assert.Equal(0, circuits.GetProperty("open").GetInt32());
        Assert.Equal(0, circuits.GetProperty("disconnected").GetInt32());
        Assert.Equal(1, port.Calls);
    }

    private static string[] Names(JsonElement root, string property) => root.GetProperty(property).EnumerateObject().Select(member => member.Name).ToArray();

    private static async Task<JsonDocument> GetAsync()
    {
        await using var host = await RealmTestHost.StartAsync();
        using var client = host.CreateClient();

        using var response = await client.GetAsync("diagnostics.json");

        await RealmTestHost.EnsureSuccessAsync(host, response);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private sealed class CannedDiagnostics(DiagnosticsSnapshot snapshot) : IDiagnostics
    {
        public int Calls { get; private set; }

        public DiagnosticsSnapshot GetSnapshot()
        {
            Calls++;
            return snapshot;
        }
    }
}
