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
/// <c>DotNetObjectReference</c>. A change of <see cref="Selection"/> is mirrored onto the map with <c>setSelection</c> and one flight command (03 section 3.4); who
/// decides the selection (pins, rows, bubbles) is the page's, and the layers popover is S10's.
/// </summary>
public sealed partial class MapView : IMapEventHandler, IAsyncDisposable
{
    /// <summary>The id of the map's element, which <c>init</c> looks up.</summary>
    public const string ContainerId = "realm-map";

    /// <summary>What the page shows when the browser cannot create a WebGL 2 context.</summary>
    public const string WebGlMessage = "The map needs WebGL 2, which this browser does not provide.";

    // The inputs of each payload, compared as they were when last sent. The lists compare by reference (they are immutable and the session
    // replaces them), the records by value.
    // The selection is part of both keys: the chip follows it (01 section 4.4), so a new selection resends the sections that carry the chips. The vehicles' key has the minute only while a vehicle is
    // selected (its "Last heard 1 hr ago" is the one time-based chip there), so a quiet map does not resend them every minute.
    private readonly record struct MembersKey(IReadOnlyList<MemberVm> Members, IReadOnlyList<PlaceVm> Places, string? MeId, long Minute, MapPayloadOptions Options, EntityRef? Selection);

    private readonly record struct VehiclesKey(IReadOnlyList<VehicleVm> Vehicles, IReadOnlyList<PlaceVm> Places, EntityRef? Selection, long Minute);

    private readonly record struct ZonesKey(IReadOnlyList<PlaceVm> Places, bool Show, MapPayloadOptions Options);

    private readonly record struct TargetsKey(IReadOnlyList<MemberVm> Members, IReadOnlyList<VehicleVm> Vehicles, IReadOnlyList<PlaceVm> Places, string? MeId, MapPayloadOptions Options);

    private MapInterop? _interop;
    private bool _ready;
    private bool _disposed;
    private bool _webGlUnavailable;
    private bool _keepCamera;

    // The last value sent of each section (null: not sent yet) and the per-circuit version counters (03 section 4.2).
    private MembersKey? _members;
    private VehiclesKey? _vehicles;
    private ZonesKey? _zones;
    private TargetsKey? _targets;
    private MapLayout? _layout;
    private Padding? _padding;
    private string? _style;
    private EntityRef? _selection;
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

    /// <summary>The session's zone (<c>IRealmSession.Zone</c>), for the relative times of the chips that are a day old or more; UTC until the page passes it.</summary>
    [Parameter]
    public TimeZoneInfo Zone { get; set; } = TimeZoneInfo.Utc;

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

    /// <summary>
    /// The selected member, vehicle or place (D45), decided by the page. When it changes the map is told (<c>setSelection</c>) and flies to it with the Peek padding;
    /// a change to null only clears the glow.
    /// </summary>
    [Parameter]
    public EntityRef? Selection { get; set; }

    /// <summary>A member pin, a vehicle pin or a zone was tapped.</summary>
    [Parameter]
    public EventCallback<EntityRef> OnPinTap { get; set; }

    /// <summary>An edge bubble was tapped: the ids of its members, one for a single-member bubble (a selection, D84), two or more for a cluster (the script has fitted the camera).</summary>
    [Parameter]
    public EventCallback<IReadOnlyList<string>> OnBubbleTap { get; set; }

    /// <summary>The empty map was tapped.</summary>
    [Parameter]
    public EventCallback OnMapTap { get; set; }

    /// <summary>
    /// A settled camera whose recentre state is not the one the page holds (<c>Default</c> at the start): a pan that took the view away from the default one, a recentre that brought it back,
    /// "me alone". The script says nothing about a gesture that leaves the state as it was, because each report re-renders the page (<c>[X-07]</c>); a page that wants the position asks
    /// for it with <see cref="GetCameraAsync"/>.
    /// </summary>
    [Parameter]
    public EventCallback<CameraState> OnCameraChanged { get; set; }

    /// <summary>The camera stopped following the selected driving member (a gesture, a bubble tap, a recentre, a new selection, or the member stopped driving): the page drops its mirror of Follow.</summary>
    [Parameter]
    public EventCallback OnFollowEnded { get; set; }

    /// <summary>
    /// The camera to start from when the Location page comes back (01 section 2.2, R1-12): read once, when the map is created. The map then starts there without animation, the default
    /// camera fit does not run and neither does the first selection flight (the glow is still set, the camera stays where the person left it). Null starts the default camera.
    /// </summary>
    [Parameter]
    public CameraState? RestoreCamera { get; set; }

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
        EntityOf(kind, id) is { } entity ? InvokeAsync(() => TapAsync(entity)) : Task.CompletedTask;

    Task IMapEventHandler.MapTapAsync() => InvokeAsync(() => OnMapTap.InvokeAsync());

    // D84: one id is the same selection as that member's pin. A cluster only announces (the script fitted the camera), so it can never be a repeat tap.
    Task IMapEventHandler.BubbleTapAsync(IReadOnlyList<string> ids) =>
        InvokeAsync(async () =>
        {
            var again = ids.Count == 1 && Selection is { Kind: EntityKind.Member } selected && selected.Id == ids[0] ? selected : null;
            await OnBubbleTap.InvokeAsync(ids);
            await FlyAgainAsync(again);
        });

    Task IMapEventHandler.CameraChangedAsync(CameraState camera) => InvokeAsync(() => OnCameraChanged.InvokeAsync(camera));

    Task IMapEventHandler.FollowEndedAsync() => InvokeAsync(() => OnFollowEnded.InvokeAsync());

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

    private static EntityRef? EntityOf(string kind, string id) =>
        kind switch
        {
            "member" => new EntityRef(EntityKind.Member, id),
            "vehicle" => new EntityRef(EntityKind.Vehicle, id),
            "place" => new EntityRef(EntityKind.Place, id),
            _ => null,
        };

    // The page decides what a tap means (RealmUiState through the reducer). A tap on the entity that is already selected changes nothing there (D45, 01 section 4.13: "selected pin
    // again: nothing"), so no parameter changes and no flight would follow; the selection flight is run here instead, camera only (D89, R1-04). Whether it is a repeat is read before
    // the page handles the tap.
    private async Task TapAsync(EntityRef entity)
    {
        var again = Selection == entity ? entity : null;
        await OnPinTap.InvokeAsync(entity);
        await FlyAgainAsync(again);
    }

    /// <summary>
    /// The recentre button was tapped (01 section 4.11, AC-20): the script cycles away, the default view, "me alone", the default view. The selection is untouched. Nothing happens before the
    /// map is ready or after it is gone.
    /// </summary>
    public async Task RecenterAsync()
    {
        if (!_ready || _disposed || _webGlUnavailable || _interop is not { } interop)
        {
            return;
        }

        await interop.RecenterAsync();
    }

    /// <summary>
    /// The camera as the script sees it now, for the Location page to keep when it goes away (R1-12): a settled camera reaches <see cref="OnCameraChanged"/> only when the recentre state changes, so the
    /// position is read here, once, not reported per gesture. Null before the map is ready, after it is gone, without WebGL and when the circuit is gone.
    /// </summary>
    public async Task<CameraState?> GetCameraAsync()
    {
        if (!_ready || _disposed || _webGlUnavailable || _interop is not { } interop)
        {
            return null;
        }

        return await interop.GetCameraAsync();
    }

    private async Task FlyAgainAsync(EntityRef? entity)
    {
        if (entity is null || !_ready || _disposed || _webGlUnavailable || _interop is not { } interop)
        {
            return;
        }

        await FlyAsync(interop, entity);
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

        var restore = RestoreCamera; // read once: the map starts from it or from the default camera, whatever the page passes later
        _interop = interop; // from here DisposeAsync can release it, but nothing is sent until init has finished (the script drops a setter before then)
        var ready = await interop.InitAsync(InitOptions(restore));
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

        // R1-12: a map that starts from a restored camera stays there. The selection that came with the page is shown (setSelection) without the flight that would move the camera.
        _keepCamera = restore is not null;
        try
        {
            await SyncAsync();
        }
        finally
        {
            _keepCamera = false;
        }

        if (restore is null)
        {
            // The first load does not animate (01 section 4.9). The script keeps the request until it has targets and a size.
            await interop.FitDefaultAsync(animate: false);
        }
    }

    private MapInitOptions InitOptions(CameraState? restore)
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
            MapFeatures.All,
            restore);
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

        var minute = Now.ToUnixTimeSeconds() / 60;
        var members = new MembersKey(Members, Places, MeId, minute, Options, Selection);
        if (_members != members)
        {
            _members = members;
            await interop.UpsertMembersAsync(MapPayloadFactory.Members(Members, Places, MeId, Now, Options, ++_membersVersion, Selection, Zone));
        }

        var vehicles = new VehiclesKey(Vehicles, Places, Selection, Selection is { Kind: EntityKind.Vehicle } ? minute : 0);
        if (_vehicles != vehicles)
        {
            _vehicles = vehicles;
            await interop.UpsertVehiclesAsync(MapPayloadFactory.Vehicles(Vehicles, Places, ++_vehiclesVersion, Selection, Now, Zone));
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

        // Last, so the script already holds the payloads the flight looks the entity up in.
        if (_selection != Selection)
        {
            var selection = Selection;
            _selection = selection;
            await SendSelectionAsync(interop, selection, fly: !_keepCamera);
        }
    }

    // 03 section 3.4: whichever path changed the selection (a pin, a bubble or a row), the map gets setSelection and then one flight command. Follow is requested for a
    // member and the script honours it only for one that is driving with a fresh fix (01 section 4.14). `fly` is false only for the selection the page came back with
    // when the camera is restored (R1-12).
    private static async Task SendSelectionAsync(MapInterop interop, EntityRef? selection, bool fly)
    {
        await interop.SetSelectionAsync(selection, follow: selection?.Kind == EntityKind.Member);
        if (fly && selection is not null)
        {
            await FlyAsync(interop, selection);
        }
    }

    // The selection flight of an entity (01 section 4.13), with the Peek padding the script computes itself.
    private static async Task FlyAsync(MapInterop interop, EntityRef entity)
    {
        switch (entity.Kind)
        {
            case EntityKind.Member:
                await interop.FlyToMemberAsync(entity.Id, follow: true);
                break;
            case EntityKind.Vehicle:
                await interop.FlyToVehicleAsync(entity.Id);
                break;
            case EntityKind.Place:
                await interop.FitPlaceAsync(entity.Id);
                break;
        }
    }

    [LoggerMessage(EventId = 5101, Level = LogLevel.Warning, Message = "The map script reported an error in {Area}: {Message}")]
    private static partial void LogScriptError(ILogger logger, string area, string message);

    [LoggerMessage(EventId = 5102, Level = LogLevel.Warning, Message = "payload_schema_mismatch: the script speaks payload schema {Script} but the server speaks {Server}; a stale cached realmMap.js is the likely cause")]
    private static partial void LogPayloadSchemaMismatch(ILogger logger, int script, int server);
}
