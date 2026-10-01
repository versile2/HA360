using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Realm.Domain;
using Realm.Web.Map;

namespace Realm.Web.Components.Map;

/// <summary>
/// Owns the map's element and the interop with <c>realmMap.js</c> (03 section 3.4). The data arrives as view-model lists; this component turns
/// them into the payloads of 03 section 4.5 through <see cref="MapPayloadFactory"/> and sends only the sections whose input changed
/// (<see cref="object.ReferenceEquals"/> on the immutable lists; the "minute" of the chip is part of the members' input). All script work
/// happens in <see cref="OnAfterRenderAsync"/>, which only runs in the circuit: prerendering renders the element and nothing else. The module
/// is imported once, the map is created by an idempotent <c>init</c>, and <see cref="DisposeAsync"/> tears the map down and releases the
/// <c>DotNetObjectReference</c>. Selection, the flights and the layers popover are S8's: this slice has no <c>Selection</c> parameter.
/// </summary>
public sealed partial class MapView : IMapEventHandler, IAsyncDisposable
{
    /// <summary>The id of the map's element, which <c>init</c> looks up.</summary>
    public const string ContainerId = "realm-map";

    /// <summary>What the page shows when the browser cannot create a WebGL 2 context.</summary>
    public const string WebGlMessage = "The map needs WebGL 2, which this browser does not provide.";

    // The inputs of each payload, compared as they were when last sent. The lists compare by reference (they are immutable and the session
    // replaces them), the records by value.
    private readonly record struct MembersKey(IReadOnlyList<MemberVm> Members, IReadOnlyList<PlaceVm> Places, string? MeId, long Minute, MapPayloadOptions Options);

    private readonly record struct VehiclesKey(IReadOnlyList<VehicleVm> Vehicles, IReadOnlyList<PlaceVm> Places);

    private readonly record struct ZonesKey(IReadOnlyList<PlaceVm> Places, bool Show, MapPayloadOptions Options);

    private readonly record struct TargetsKey(IReadOnlyList<MemberVm> Members, IReadOnlyList<VehicleVm> Vehicles, IReadOnlyList<PlaceVm> Places, string? MeId, MapPayloadOptions Options);

    private MapInterop? _interop;
    private bool _ready;
    private bool _disposed;
    private bool _webGlUnavailable;

    // The last value sent of each section (null: not sent yet) and the per-circuit version counters (03 section 4.2).
    private MembersKey? _members;
    private VehiclesKey? _vehicles;
    private ZonesKey? _zones;
    private TargetsKey? _targets;
    private MapLayout? _layout;
    private Padding? _padding;
    private string? _style;
    private bool? _reducedMotion;
    private int _membersVersion;
    private int _vehiclesVersion;
    private int _zonesVersion;
    private int _targetsVersion;

    /// <summary>The people, in sort order.</summary>
    [Parameter]
    public IReadOnlyList<MemberVm> Members { get; set; } = [];

    /// <summary>The vehicles, in sort order.</summary>
    [Parameter]
    public IReadOnlyList<VehicleVm> Vehicles { get; set; } = [];

    /// <summary>The drawn zones (the data layer already left the arrival zone out; the factory excludes a larger radius again).</summary>
    [Parameter]
    public IReadOnlyList<PlaceVm> Places { get; set; } = [];

    /// <summary>The viewer's member id (S8 passes <c>RealmUiState.MeId</c>); null or unknown means the first live member.</summary>
    [Parameter]
    public string? MeId { get; set; }

    /// <summary>The session's instant (<c>IRealmSession.Time</c>), for the "Here for" chip; the component never reads a clock.</summary>
    [Parameter]
    public DateTimeOffset Now { get; set; }

    /// <summary>A style id of <see cref="MapStyleIds"/>. A change switches the style in place.</summary>
    [Parameter]
    public string StyleId { get; set; } = MapStyleIds.Night;

    /// <summary>The "Show places" switch.</summary>
    [Parameter]
    public bool ShowZones { get; set; } = true;

    /// <summary>What the layout contributes to the map padding; S7 passes the real one.</summary>
    [Parameter]
    public MapLayout Layout { get; set; } = MapLayout.Compact;

    /// <summary>A forced padding (tests, the aside host); null is the measured mode.</summary>
    [Parameter]
    public Padding? ForcedPadding { get; set; }

    /// <summary>The thresholds of 01 section 4.1.</summary>
    [Parameter]
    public MapPayloadOptions Options { get; set; } = MapPayloadOptions.Default;

    /// <summary>The user prefers reduced motion (S7's shell interop reports it).</summary>
    [Parameter]
    public bool ReducedMotion { get; set; }

    /// <summary>Install <c>window.__realm</c>: a Demo session, or <c>Realm:TestHooks</c> (03 section 4.10).</summary>
    [Parameter]
    public bool TestHooks { get; set; }

    /// <summary>A member pin, a vehicle pin or a zone was tapped.</summary>
    [Parameter]
    public EventCallback<EntityRef> OnPinTap { get; set; }

    /// <summary>The empty map was tapped.</summary>
    [Parameter]
    public EventCallback OnMapTap { get; set; }

    /// <summary>A camera move settled.</summary>
    [Parameter]
    public EventCallback<CameraState> OnCameraChanged { get; set; }

    /// <summary>A style did not load; the value is the style id that was asked for. The script has already kept the last good style.</summary>
    [Parameter]
    public EventCallback<string> OnStyleFailed { get; set; }

    [Inject]
    private IJSRuntime JS { get; set; } = default!;

    [Inject]
    private ILogger<MapView> Logger { get; set; } = default!;

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_disposed || _webGlUnavailable)
        {
            return;
        }

        try
        {
            if (firstRender)
            {
                await InitializeAsync();
            }
            else
            {
                await SyncAsync();
            }
        }
        catch (JSDisconnectedException)
        {
            // The circuit dropped mid-call; the map goes with it.
        }
    }

    /// <summary>Tears the map down and releases the script's reference to this component.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        var interop = _interop;
        _interop = null;
        if (interop is not null)
        {
            await interop.DisposeAsync();
        }
    }

    // ---- the script calls into the callbacks below (03 section 4.7), through MapCallbacks ---------------------------------------------

    Task IMapEventHandler.ReadyAsync(ReadyInfo info) => Task.CompletedTask;

    Task IMapEventHandler.PinTapAsync(string kind, string id) =>
        kind switch
        {
            "member" => InvokeAsync(() => OnPinTap.InvokeAsync(new EntityRef(EntityKind.Member, id))),
            "vehicle" => InvokeAsync(() => OnPinTap.InvokeAsync(new EntityRef(EntityKind.Vehicle, id))),
            "place" => InvokeAsync(() => OnPinTap.InvokeAsync(new EntityRef(EntityKind.Place, id))),
            _ => Task.CompletedTask,
        };

    Task IMapEventHandler.MapTapAsync() => InvokeAsync(() => OnMapTap.InvokeAsync());

    Task IMapEventHandler.CameraChangedAsync(CameraState camera) => InvokeAsync(() => OnCameraChanged.InvokeAsync(camera));

    Task IMapEventHandler.StyleResultAsync(StyleResult result) =>
        result.Ok ? Task.CompletedTask : InvokeAsync(() => OnStyleFailed.InvokeAsync(result.StyleId));

    Task IMapEventHandler.WebGlUnavailableAsync() =>
        InvokeAsync(() =>
        {
            _webGlUnavailable = true;
            StateHasChanged();
        });

    Task IMapEventHandler.ErrorAsync(string area, string message)
    {
        LogScriptError(Logger, area, message);
        return Task.CompletedTask;
    }

    // ---- first render: import, init, send everything --------------------------------------------------------------------------------------

    private async Task InitializeAsync()
    {
        var interop = await MapInterop.CreateAsync(JS, new MapCallbacks(this));
        if (_disposed)
        {
            await interop.DisposeAsync();
            return;
        }

        _interop = interop; // from here DisposeAsync can release it, but nothing is sent until init has finished (the script drops a setter before then)
        var ready = await interop.InitAsync(InitOptions());
        if (ready is not null && ready.PayloadSchema != MapInterop.PayloadSchema)
        {
            LogPayloadSchemaMismatch(Logger, ready.PayloadSchema, MapInterop.PayloadSchema);
        }

        if (_disposed || _webGlUnavailable)
        {
            return;
        }

        _style = StyleId;
        _reducedMotion = ReducedMotion;
        _ready = true;
        await SyncAsync();

        // The first load does not animate (01 section 4.9). The script keeps the request until it has targets and a size.
        await interop.FitDefaultAsync(animate: false);
    }

    private MapInitOptions InitOptions()
    {
        var targets = MapPayloadFactory.Targets(Members, Vehicles, Places, MeId, Options, version: 0);
        return new MapInitOptions(
            ContainerId,
            StyleId,
            Center: CenterOf(targets),
            Zoom: 11,
            ReducedMotion,
            TestHooks,
            MapStrings.Default,
            MapFeatures.All);
    }

    // Only the starting point until the first fit: me, else the middle of the default view, else the world.
    private static double[] CenterOf(DefaultTargets? targets) =>
        targets is null ? [0, 0]
        : targets.Me is { } me ? [me.Center[0], me.Center[1]]
        : [(targets.Default.Bounds[0][0] + targets.Default.Bounds[1][0]) / 2, (targets.Default.Bounds[0][1] + targets.Default.Bounds[1][1]) / 2];

    // ---- every render: send what changed ------------------------------------------------------------------------------------------------

    // Each section compares its input with the last one sent and stores the new one before it awaits, so two overlapping calls never send the
    // same section twice and the versions stay in order.
    private async Task SyncAsync()
    {
        if (!_ready || _interop is not { } interop || _disposed || _webGlUnavailable)
        {
            return;
        }

        if (_style != StyleId)
        {
            _style = StyleId;
            await interop.SetStyleAsync(StyleId);
        }

        if (_reducedMotion != ReducedMotion)
        {
            _reducedMotion = ReducedMotion;
            await interop.SetReducedMotionAsync(ReducedMotion);
        }

        if (_layout != Layout)
        {
            _layout = Layout;
            await interop.SetLayoutAsync(MapPayloadFactory.Layout(Layout));
        }

        if (_padding != ForcedPadding)
        {
            _padding = ForcedPadding;
            await interop.SetPaddingAsync(ForcedPadding);
        }

        var zones = new ZonesKey(Places, ShowZones, Options);
        if (_zones != zones)
        {
            _zones = zones;
            await interop.SetZonesAsync(MapPayloadFactory.Zones(Places, ShowZones, Options, ++_zonesVersion));
        }

        var members = new MembersKey(Members, Places, MeId, Now.ToUnixTimeSeconds() / 60, Options);
        if (_members != members)
        {
            _members = members;
            await interop.UpsertMembersAsync(MapPayloadFactory.Members(Members, Places, MeId, Now, Options, ++_membersVersion));
        }

        var vehicles = new VehiclesKey(Vehicles, Places);
        if (_vehicles != vehicles)
        {
            _vehicles = vehicles;
            await interop.UpsertVehiclesAsync(MapPayloadFactory.Vehicles(Vehicles, Places, ++_vehiclesVersion));
        }

        var targets = new TargetsKey(Members, Vehicles, Places, MeId, Options);
        if (_targets != targets)
        {
            _targets = targets;
            if (MapPayloadFactory.Targets(Members, Vehicles, Places, MeId, Options, ++_targetsVersion) is { } payload)
            {
                await interop.SetDefaultTargetsAsync(payload);
            }
        }
    }

    [LoggerMessage(EventId = 5101, Level = LogLevel.Warning, Message = "The map script reported an error in {Area}: {Message}")]
    private static partial void LogScriptError(ILogger logger, string area, string message);

    [LoggerMessage(EventId = 5102, Level = LogLevel.Warning, Message = "payload_schema_mismatch: the script speaks payload schema {Script} but the server speaks {Server}; a stale cached realmMap.js is the likely cause")]
    private static partial void LogPayloadSchemaMismatch(ILogger logger, int script, int server);
}
