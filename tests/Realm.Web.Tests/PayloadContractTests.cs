using System.Text.Json;
using Realm.Demo;
using Realm.Web.Map;
using Xunit;

namespace Realm.Web.Tests;

/// <summary>
/// The C# half of the map interop contract (03 section 4.5). It serializes the payloads <see cref="MapPayloadFactory"/> builds from the Demo
/// snapshot (fictional, 02 section 9.3) with the same options as the interop and writes them to <c>tests/contract/payloads/</c>, one file per
/// shape and named after it, so that <c>tests/contract/payloadShape.mjs</c> can check the wire format: the <c>dotnet</c> job uploads the folder,
/// and the <c>node --test</c> of <c>tests/contract</c> and the <c>e2e</c> job (S6a) validate every file in it. The folder is git-ignored; the
/// files are regenerated on every run and are deterministic (the Demo clock is frozen).
/// </summary>
public sealed class PayloadContractTests
{
    private static readonly JsonSerializerOptions Indented = new(MapJson.Options) { WriteIndented = true };

    [Fact]
    public async Task Goldens_AreWrittenForEveryShape_AndAreDeterministic()
    {
        var directory = Path.Combine(FindRepositoryRoot(), "tests", "contract", "payloads");
        Directory.CreateDirectory(directory);

        var first = await BuildGoldensAsync();
        var second = await BuildGoldensAsync();

        Assert.Equal(first, second);
        Assert.Equal(
            ["camera.json", "default-targets.json", "layout-expanded.json", "layout.json", "members.json", "style-result.json", "vehicles.json", "zones-hidden.json", "zones.json"],
            first.Keys.Order(StringComparer.Ordinal));
        foreach (var (name, json) in first)
        {
            File.WriteAllText(Path.Combine(directory, name), json + "\n");
        }
    }

    [Fact]
    public async Task Members_AreCamelCase_WithStringEnums_AndRelativeAvatarUrls()
    {
        var members = (await BuildGoldensAsync())["members.json"];

        using var document = JsonDocument.Parse(members);
        var root = document.RootElement;
        var king = root.GetProperty("members").EnumerateArray().Single(member => member.GetProperty("id").GetString() == "king");

        Assert.Equal(JsonValueKind.Number, root.GetProperty("version").ValueKind);
        Assert.Equal("king", root.GetProperty("meId").GetString());
        Assert.Equal("atPlace", king.GetProperty("status").GetString());
        Assert.Equal("Here for 3 hrs, 33 mins", king.GetProperty("chip").GetString());
        Assert.Equal(JsonValueKind.Number, king.GetProperty("lat").ValueKind);
        Assert.Equal(JsonValueKind.Number, king.GetProperty("ring").GetProperty("widthPx").ValueKind);
        Assert.Equal(JsonValueKind.False, king.GetProperty("ring").GetProperty("dashed").ValueKind);
        Assert.All(
            root.GetProperty("members").EnumerateArray().Where(member => member.GetProperty("avatarUrl").ValueKind == JsonValueKind.String),
            member => Assert.False(member.GetProperty("avatarUrl").GetString()!.StartsWith('/')));
    }

    [Fact]
    public async Task Zones_AndTargets_HaveTheWireNamesOfTheScript()
    {
        var goldens = await BuildGoldensAsync();

        using var zones = JsonDocument.Parse(goldens["zones.json"]);
        using var targets = JsonDocument.Parse(goldens["default-targets.json"]);

        Assert.Equal(JsonValueKind.True, zones.RootElement.GetProperty("show").ValueKind);
        Assert.Equal(14, zones.RootElement.GetProperty("zones").GetArrayLength());
        Assert.Equal(["dark", "imagery", "light"], zones.RootElement.GetProperty("appearances").EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal(16, targets.RootElement.GetProperty("default").GetProperty("maxZoom").GetDouble());
        Assert.Equal(16, targets.RootElement.GetProperty("me").GetProperty("zoom").GetDouble());
        Assert.Equal(2, targets.RootElement.GetProperty("me").GetProperty("center").GetArrayLength());
    }

    // What realmMap.js sends comes back as JSON the records must read (03 section 4.3): camelCase names, numbers and strings.
    [Fact]
    public void WhatTheScriptReports_IsReadIntoTheRecords()
    {
        var camera = JsonSerializer.Deserialize<CameraState>(
            """{"center":[-85.341,31.099],"zoom":14.5,"bounds":[[-85.36,31.08],[-85.32,31.12]],"animated":true,"lastDurationMs":600,"recenter":"me","userInitiated":false}""",
            MapJson.Options)!;
        var style = JsonSerializer.Deserialize<StyleResult>("""{"styleId":"satellite","ok":false,"error":"offline"}""", MapJson.Options)!;
        var ready = JsonSerializer.Deserialize<ReadyInfo>("""{"jsVersion":"1.0.0","payloadSchema":1,"maplibre":"6.11.2"}""", MapJson.Options)!;

        Assert.Equal([-85.341, 31.099], camera.Center);
        Assert.Equal(14.5, camera.Zoom);
        Assert.Equal(2, camera.Bounds.Count);
        Assert.Equal(-85.32, camera.Bounds[1][0]);
        Assert.True(camera.Animated);
        Assert.Equal(600, camera.LastDurationMs);
        Assert.Equal(RecenterState.Me, camera.Recenter);
        Assert.False(camera.UserInitiated);
        Assert.Equal(new StyleResult(MapStyleIds.Satellite, false, "offline"), style);
        Assert.Equal(new ReadyInfo("1.0.0", 1, "6.11.2"), ready);
    }

    // ---- helpers ---------------------------------------------------------------------------------------------------------------------------------

    // The folder of Realm.slnx, found from the test assembly (bin/<configuration>/<tfm> sits inside the repository).
    internal static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Realm.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException($"Realm.slnx was not found in any parent folder of {AppContext.BaseDirectory}.");
    }

    // File name -> JSON. One entry per shape of payloadShape.mjs that C# writes or reads (SelectionPayload is S8's).
    private static async Task<Dictionary<string, string>> BuildGoldensAsync()
    {
        await using var session = new DemoRealmSessionFactory().Create(null);
        var snapshot = session.Current;
        var now = session.Time.GetUtcNow();
        var options = MapPayloadOptions.Default;
        var meId = MapPayloadFactory.ResolveMeId(snapshot.Members, null);

        var targets = MapPayloadFactory.Targets(snapshot.Members, snapshot.Vehicles, snapshot.Places, meId, options, 1)
            ?? throw new InvalidOperationException("The Demo snapshot has no default view.");

        return new Dictionary<string, object>
        {
            ["members.json"] = MapPayloadFactory.Members(snapshot.Members, snapshot.Places, meId, now, options, 1),
            ["vehicles.json"] = MapPayloadFactory.Vehicles(snapshot.Vehicles, snapshot.Places, 1),
            ["zones.json"] = MapPayloadFactory.Zones(snapshot.Places, true, options, 1),
            ["zones-hidden.json"] = MapPayloadFactory.Zones(snapshot.Places, false, options, 2),
            ["default-targets.json"] = targets,
            ["layout.json"] = MapPayloadFactory.Layout(MapLayout.Compact),
            ["layout-expanded.json"] = MapPayloadFactory.Layout(MapLayout.Expanded),

            // These two travel from the script to C#; they are written from the records so that their field names are checked too.
            ["camera.json"] = new CameraState([-85.341, 31.099], 14.5, [[-85.36, 31.08], [-85.32, 31.12]], Animated: true, LastDurationMs: 600, RecenterState.Default, UserInitiated: false),
            ["style-result.json"] = new StyleResult(MapStyleIds.Night, Ok: true, Error: null),
        }.ToDictionary(entry => entry.Key, entry => JsonSerializer.Serialize(entry.Value, entry.Value.GetType(), Indented));
    }
}
