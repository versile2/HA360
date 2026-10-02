using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.JSInterop;
using Realm.Demo;
using Realm.Web.Components.Map;
using Realm.Web.Map;
using Xunit;

namespace Realm.Web.Tests;

/// <summary>
/// <see cref="MapInterop"/> and <see cref="MapCallbacks"/> against a fake <see cref="IJSRuntime"/> (03 sections 4.1, 4.3 and 4.7): the module
/// path is the plain relative URL of D62, each setter calls its export with the payload, nothing is sent after the dispose, a dropped circuit is
/// not an error, and the callbacks are exactly the names <c>realmMap.js</c> calls. No browser, no MapLibre.
/// </summary>
public sealed class MapInteropTests
{
    private static readonly MapInitOptions Init = new(
        "realm-map", MapStyleIds.DemoOffline, [-85.341, 31.099], 12, ReducedMotion: false, TestHooks: true, MapStrings.Default, MapFeatures.All);

    [Fact]
    public async Task CreateAsync_ImportsTheModuleByThePlainRelativePath()
    {
        var js = new FakeJs();

        await using var interop = await MapInterop.CreateAsync(js, new MapCallbacks(new RecordingHandler()));

        var call = Assert.Single(js.Runtime);
        Assert.Equal("import", call.Identifier);
        Assert.Equal(["./js/realmMap.js"], call.Args);
        Assert.False(MapInterop.ModulePath.StartsWith('/'));
        Assert.DoesNotContain("_content", MapInterop.ModulePath);
    }

    [Fact]
    public async Task CreateAsync_WhenTheImportFails_ThrowsAndLeavesNothingBehind()
    {
        var js = new FakeJs { ImportError = new JSException("no such module") };

        await Assert.ThrowsAsync<JSException>(async () => await MapInterop.CreateAsync(js, new MapCallbacks(new RecordingHandler())));
    }

    [Fact]
    public async Task InitAsync_PassesTheOptionsAndTheCallbackReference_AndReturnsTheReadyInfo()
    {
        var js = new FakeJs();
        await using var interop = await MapInterop.CreateAsync(js, new MapCallbacks(new RecordingHandler()));

        var ready = await interop.InitAsync(Init);

        Assert.Equal(new ReadyInfo("test", MapInterop.PayloadSchema, "6.11.2"), ready);
        var call = Assert.Single(js.Module, call => call.Identifier == "init");
        Assert.Same(Init, call.Args[0]);
        Assert.IsType<DotNetObjectReference<MapCallbacks>>(call.Args[1]);
    }

    [Fact]
    public async Task EverySetter_CallsItsExport_WithThePayload()
    {
        var js = new FakeJs();
        await using var interop = await MapInterop.CreateAsync(js, new MapCallbacks(new RecordingHandler()));
        await using var session = new DemoRealmSessionFactory().Create(null);
        var snapshot = session.Current;
        var options = MapPayloadOptions.Default;
        var members = MapPayloadFactory.Members(snapshot.Members, snapshot.Places, null, session.Time.GetUtcNow(), options, 1);
        var vehicles = MapPayloadFactory.Vehicles(snapshot.Vehicles, snapshot.Places, 1);
        var zones = MapPayloadFactory.Zones(snapshot.Places, true, options, 1);
        var targets = MapPayloadFactory.Targets(snapshot.Members, snapshot.Vehicles, snapshot.Places, null, options, 1)!;
        var layout = MapPayloadFactory.Layout(MapLayout.Compact);
        var padding = new Padding(1, 2, 3, 4);

        await interop.SetLayoutAsync(layout);
        await interop.SetPaddingAsync(padding);
        await interop.SetPaddingAsync(null);
        await interop.UpsertMembersAsync(members);
        await interop.UpsertVehiclesAsync(vehicles);
        await interop.SetZonesAsync(zones);
        await interop.SetDefaultTargetsAsync(targets);
        await interop.FitDefaultAsync(animate: false);
        await interop.ResizeAsync();
        await interop.SetReducedMotionAsync(true);
        var style = await interop.SetStyleAsync(MapStyleIds.Night);

        Assert.Equal(
            ["setLayout", "setPadding", "setPadding", "upsertMembers", "upsertVehicles", "setZones", "setDefaultTargets", "fitDefault", "resize", "setReducedMotion", "setStyle"],
            js.Module.Select(call => call.Identifier));
        Assert.Same(layout, js.Module[0].Args[0]);
        Assert.Same(padding, js.Module[1].Args[0]);
        Assert.Null(js.Module[2].Args[0]);
        Assert.Same(members, js.Module[3].Args[0]);
        Assert.Same(vehicles, js.Module[4].Args[0]);
        Assert.Same(zones, js.Module[5].Args[0]);
        Assert.Same(targets, js.Module[6].Args[0]);
        Assert.Equal("""{"animate":false}""", JsonSerializer.Serialize(js.Module[7].Args[0], MapJson.Options));
        Assert.Empty(js.Module[8].Args);
        Assert.Equal(true, js.Module[9].Args[0]);
        Assert.Equal([MapStyleIds.Night], js.Module[10].Args);
        Assert.Equal(new StyleResult(MapStyleIds.Night, true, null), style);
    }

    [Fact]
    public async Task DisposeAsync_TearsDownTheMap_ReleasesTheModuleAndTheReference_OnlyOnce()
    {
        var js = new FakeJs();
        var interop = await MapInterop.CreateAsync(js, new MapCallbacks(new RecordingHandler()));
        await interop.InitAsync(Init);
        var reference = (DotNetObjectReference<MapCallbacks>)js.Module.Single(call => call.Identifier == "init").Args[1]!;
        Assert.NotNull(reference.Value);

        await interop.DisposeAsync();
        await interop.DisposeAsync();

        Assert.Equal(1, js.Module.Count(call => call.Identifier == "dispose"));
        Assert.Equal(1, js.ModuleDisposed);
        Assert.Throws<ObjectDisposedException>(() => reference.Value);
    }

    [Fact]
    public async Task AfterTheDispose_NothingIsSentToTheScript()
    {
        var js = new FakeJs();
        var interop = await MapInterop.CreateAsync(js, new MapCallbacks(new RecordingHandler()));
        await interop.DisposeAsync();
        var calls = js.Module.Count;

        Assert.Null(await interop.InitAsync(Init));
        Assert.Null(await interop.SetStyleAsync(MapStyleIds.Day));
        await interop.ResizeAsync();
        await interop.FitDefaultAsync(animate: true);
        await interop.SetPaddingAsync(null);

        Assert.Equal(calls, js.Module.Count);
    }

    [Fact]
    public async Task ADroppedCircuit_IsNotAnError_AndTheReferenceIsStillReleased()
    {
        var js = new FakeJs { Disconnected = true };
        var interop = await MapInterop.CreateAsync(js, new MapCallbacks(new RecordingHandler()));

        Assert.Null(await interop.InitAsync(Init));
        Assert.Null(await interop.SetStyleAsync(MapStyleIds.Night));
        await interop.ResizeAsync();
        await interop.SetReducedMotionAsync(false);
        await interop.DisposeAsync();

        var reference = (DotNetObjectReference<MapCallbacks>)js.Module.Single(call => call.Identifier == "init").Args[1]!;
        Assert.Throws<ObjectDisposedException>(() => reference.Value);
    }

    // ---- the callbacks -----------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Callbacks_ForwardEachCallToTheHandler()
    {
        var handler = new RecordingHandler();
        var callbacks = new MapCallbacks(handler);
        var camera = new CameraState([-85.3, 31.1], 12, [[-85.4, 31.0], [-85.2, 31.2]], Animated: false, LastDurationMs: 0, RecenterState.Away, UserInitiated: true);

        await callbacks.OnReady(new ReadyInfo("1", 1, "6.11.2"));
        await callbacks.OnPinTap("member", "king");
        await callbacks.OnMapTap();
        await callbacks.OnCameraChanged(camera);
        await callbacks.OnStyleResult(new StyleResult(MapStyleIds.Day, false, "offline"));
        await callbacks.OnWebGlUnavailable();
        await callbacks.OnError("setZones", "boom");

        Assert.Equal(
            ["ready 1", "pinTap member king", "mapTap", "camera 12 Away True", "style day False offline", "webGlUnavailable", "error setZones boom"],
            handler.Events);
    }

    [Fact]
    public void Callbacks_AreTheJsInvokableNamesOfTheScript_AndTheScriptCallsNothingElse()
    {
        var invokable = typeof(MapCallbacks)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(method => (Method: method, Attribute: method.GetCustomAttribute<JSInvokableAttribute>()))
            .ToList();
        var script = File.ReadAllText(Path.Combine(PayloadContractTests.FindRepositoryRoot(), "src", "Realm.Web", "wwwroot", "js", "realmMap.js"));
        var called = Regex.Matches(script, "'(On[A-Z][A-Za-z]+)'").Select(match => match.Groups[1].Value).ToHashSet();

        Assert.All(invokable, entry =>
        {
            Assert.NotNull(entry.Attribute);
            Assert.Null(entry.Attribute.Identifier);   // the C# method name is the name the script uses
        });
        Assert.Equal(
            ["OnCameraChanged", "OnError", "OnMapTap", "OnPinTap", "OnReady", "OnStyleResult", "OnWebGlUnavailable"],
            invokable.Select(entry => entry.Method.Name).Order(StringComparer.Ordinal));
        Assert.Empty(called.Except(invokable.Select(entry => entry.Method.Name)));
        Assert.Contains("OnReady", called);
    }

    // ---- fakes -----------------------------------------------------------------------------------------------------------------------------------------

    private sealed record Call(string Identifier, object?[] Args);

    private sealed class FakeJs : IJSRuntime
    {
        public List<Call> Runtime { get; } = [];

        public List<Call> Module { get; } = [];

        public int ModuleDisposed { get; private set; }

        public Exception? ImportError { get; init; }

        public bool Disconnected { get; init; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            Runtime.Add(new Call(identifier, args ?? []));
            if (ImportError is not null)
            {
                throw ImportError;
            }

            return new ValueTask<TValue>((TValue)(object)new FakeModule(this));
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            InvokeAsync<TValue>(identifier, args);

        public void Disposed() => ModuleDisposed++;
    }

    private sealed class FakeModule(FakeJs js) : IJSObjectReference
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            js.Module.Add(new Call(identifier, args ?? []));
            if (js.Disconnected)
            {
                throw new JSDisconnectedException("The circuit is gone.");
            }

            object? result = identifier switch
            {
                "init" => new ReadyInfo("test", MapInterop.PayloadSchema, "6.11.2"),
                "setStyle" => new StyleResult((string)args![0]!, true, null),
                _ => null,
            };
            return new ValueTask<TValue>((TValue)result!);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            InvokeAsync<TValue>(identifier, args);

        public ValueTask DisposeAsync()
        {
            js.Disposed();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingHandler : IMapEventHandler
    {
        public List<string> Events { get; } = [];

        public Task ReadyAsync(ReadyInfo info) => Record($"ready {info.PayloadSchema}");

        public Task PinTapAsync(string kind, string id) => Record($"pinTap {kind} {id}");

        public Task MapTapAsync() => Record("mapTap");

        public Task CameraChangedAsync(CameraState camera) => Record($"camera {camera.Zoom} {camera.Recenter} {camera.UserInitiated}");

        public Task StyleResultAsync(StyleResult result) => Record($"style {result.StyleId} {result.Ok} {result.Error}");

        public Task WebGlUnavailableAsync() => Record("webGlUnavailable");

        public Task ErrorAsync(string area, string message) => Record($"error {area} {message}");

        private Task Record(string text)
        {
            Events.Add(text);
            return Task.CompletedTask;
        }
    }
}
