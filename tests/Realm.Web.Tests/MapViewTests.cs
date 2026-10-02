using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Realm.Demo;
using Realm.Domain;
using Realm.Web.Components.Map;
using Realm.Web.Map;
using Xunit;

namespace Realm.Web.Tests;

/// <summary>
/// <see cref="MapView"/> as the Location page uses it (03 sections 3.4 and 4.7, 01 sections 2.2 and 4.13): what a tap the script reports becomes, and the flights. The script is a hand-written
/// <see cref="IJSRuntime"/> that records every call of the module, so the assertions are about the calls the component makes (<c>flyToMember</c>, <c>fitPlace</c>, <c>setSelection</c>) and
/// the callbacks it raises, never about the map. The component is driven through <see cref="IMapEventHandler"/>, the face <c>MapCallbacks</c> gives the script, from a thread that is not the renderer's, as the
/// script's calls arrive. A repeat tap on the selected entity is a camera-only flight (D89, R1-04): the page's state does not change, so only <see cref="MapView"/> can run it.
/// </summary>
public sealed class MapViewTests : ComponentTestBase
{
    private static readonly EntityRef King = new(EntityKind.Member, DemoCast.King.Id);
    private static readonly EntityRef Queen = new(EntityKind.Member, DemoCast.Queen.Id);
    private static readonly EntityRef Wagon = new(EntityKind.Vehicle, DemoCast.Wagon.Id);
    private static readonly EntityRef Hearth = new(EntityKind.Place, DemoPlaces.Home.Id);

    private readonly RecordingJs _js = new();
    private readonly List<EntityRef> _pinTaps = [];
    private readonly List<string[]> _bubbleTaps = [];
    private int _mapTaps;
    private int _followEnds;
    private int _cameras;

    public MapViewTests()
    {
        Services.AddLogging();
        Services.AddSingleton<IJSRuntime>(_js);
    }

    // ---- a tap reaches the page, and only a repeat tap flies ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task APinTap_OnAnEntityThatIsNotSelected_ReachesThePage_AndRunsNoFlightOfItsOwn()
    {
        var cut = RenderMap(selection: null);

        await Handler(cut).PinTapAsync("member", King.Id);

        Assert.Equal(King, Assert.Single(_pinTaps));
        Assert.Empty(Calls("flyToMember"));
        Assert.Empty(Calls("setSelection"));   // the page's new Selection parameter does that, not the tap
    }

    [Fact]
    public async Task APinTap_OnAnotherEntityThanTheSelected_RunsNoFlightEither()
    {
        var cut = RenderMap(selection: Queen);
        var flights = Calls("flyToMember").Count;

        await Handler(cut).PinTapAsync("member", King.Id);

        Assert.Equal(King, Assert.Single(_pinTaps));
        Assert.Equal(flights, Calls("flyToMember").Count);
    }

    [Fact(DisplayName = "[R1-04] A repeat tap on the selected member's pin runs the selection flight again, camera only")]
    public async Task APinTap_OnTheSelectedMember_FliesToItAgain_WithFollow_AndSendsNoSelection()
    {
        var cut = RenderMap(selection: Queen);
        var selections = Calls("setSelection").Count;
        Assert.Single(Calls("flyToMember"));   // the flight that came with the selection

        await Handler(cut).PinTapAsync("member", Queen.Id);

        Assert.Equal(Queen, Assert.Single(_pinTaps));   // the page still hears every tap
        Assert.Equal(2, Calls("flyToMember").Count);
        var repeat = Calls("flyToMember")[1];
        Assert.Equal(Queen.Id, repeat.Args[0]);
        Assert.Equal(selections, Calls("setSelection").Count);
    }

    [Fact]
    public async Task APinTap_OnTheSelectedVehicle_FliesToItAgain()
    {
        var cut = RenderMap(selection: Wagon);
        Assert.Single(Calls("flyToVehicle"));

        await Handler(cut).PinTapAsync("vehicle", Wagon.Id);

        Assert.Equal(Wagon, Assert.Single(_pinTaps));
        Assert.Equal(2, Calls("flyToVehicle").Count);
        Assert.Equal(Wagon.Id, Calls("flyToVehicle")[1].Args[0]);
    }

    [Fact]
    public async Task AZoneTap_OnTheSelectedPlace_FitsItAgain()
    {
        var cut = RenderMap(selection: Hearth);
        Assert.Single(Calls("fitPlace"));

        await Handler(cut).PinTapAsync("place", Hearth.Id);

        Assert.Equal(Hearth, Assert.Single(_pinTaps));
        Assert.Equal(2, Calls("fitPlace").Count);
        Assert.Equal(Hearth.Id, Calls("fitPlace")[1].Args[0]);
    }

    [Fact]
    public async Task APinTap_OfAKindTheMapDoesNotKnow_IsIgnored()
    {
        var cut = RenderMap(selection: Queen);
        var flights = Calls("flyToMember").Count;

        await Handler(cut).PinTapAsync("hamlet", Queen.Id);

        Assert.Empty(_pinTaps);
        Assert.Equal(flights, Calls("flyToMember").Count);
    }

    // ---- a bubble (D84) -------------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task ABubbleTap_OfOneMemberWhoIsNotSelected_ReachesThePage_WithoutAFlight()
    {
        var cut = RenderMap(selection: Queen);
        var flights = Calls("flyToMember").Count;

        await Handler(cut).BubbleTapAsync([King.Id]);

        Assert.Equal([King.Id], Assert.Single(_bubbleTaps));
        Assert.Equal(flights, Calls("flyToMember").Count);
    }

    [Fact(DisplayName = "[R1-04] A repeat tap on the selected member's bubble runs the selection flight again")]
    public async Task ABubbleTap_OfTheSelectedMember_FliesToThatMemberAgain()
    {
        var cut = RenderMap(selection: Queen);
        Assert.Single(Calls("flyToMember"));

        await Handler(cut).BubbleTapAsync([Queen.Id]);

        Assert.Equal([Queen.Id], Assert.Single(_bubbleTaps));
        Assert.Equal(2, Calls("flyToMember").Count);
        Assert.Equal(Queen.Id, Calls("flyToMember")[1].Args[0]);
    }

    [Fact]
    public async Task ABubbleTap_OfACluster_OnlyAnnounces_EvenWhenOneOfThemIsSelected()
    {
        var cut = RenderMap(selection: Queen);
        var flights = Calls("flyToMember").Count;

        await Handler(cut).BubbleTapAsync([King.Id, Queen.Id]);

        Assert.Equal([King.Id, Queen.Id], Assert.Single(_bubbleTaps));
        Assert.Equal(flights, Calls("flyToMember").Count);   // the script fitted the camera itself
    }

    [Fact]
    public async Task ABubbleTap_OfTheSelectedVehicleId_IsNotARepeat_BecauseABubbleIsAlwaysAMember()
    {
        var cut = RenderMap(selection: Wagon);
        var flights = Calls("flyToVehicle").Count;

        await Handler(cut).BubbleTapAsync([Wagon.Id]);

        Assert.Equal([Wagon.Id], Assert.Single(_bubbleTaps));
        Assert.Equal(flights, Calls("flyToVehicle").Count);
        Assert.Empty(Calls("flyToMember"));
    }

    // ---- the empty map, the follow and the camera -------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task AnEmptyMapTap_ReachesThePage_AndDoesNotFly()
    {
        var cut = RenderMap(selection: Queen);
        var flights = Calls("flyToMember").Count;

        await Handler(cut).MapTapAsync();

        Assert.Equal(1, _mapTaps);
        Assert.Equal(flights, Calls("flyToMember").Count);
    }

    [Fact(DisplayName = "[R1-14] The end of Follow reaches the page")]
    public async Task TheEndOfFollow_ReachesThePage()
    {
        var cut = RenderMap(selection: Queen);

        await Handler(cut).FollowEndedAsync();
        await Handler(cut).FollowEndedAsync();

        Assert.Equal(2, _followEnds);
    }

    [Fact]
    public async Task ASettledCamera_ReachesThePage()
    {
        var cut = RenderMap(selection: null);

        await Handler(cut).CameraChangedAsync(HomeCamera());

        Assert.Equal(1, _cameras);
    }

    [Fact(DisplayName = "[X-07] the camera is read from the script when the page asks, never as a side effect of rendering, and there is none once the map is gone")]
    public async Task GetCameraAsync_ReadsTheCameraFromTheScript_AndAfterTheMapIsGoneThereIsNone()
    {
        var cut = RenderMap(selection: null);
        Assert.Empty(Calls("getCamera"));

        var camera = await cut.Instance.GetCameraAsync();

        Assert.NotNull(camera);
        Assert.Equal(RecenterState.Away, camera.Recenter);
        Assert.Single(Calls("getCamera"));
        Assert.Equal(0, _cameras);

        await cut.Instance.DisposeAsync();

        Assert.Null(await cut.Instance.GetCameraAsync());
        Assert.Single(Calls("getCamera"));
    }

    // ---- the way back from Driving (R1-12) -------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void ANewMap_StartsWithTheDefaultCamera_AndFliesToTheSelectionItCameWith()
    {
        RenderMap(selection: Queen);

        Assert.Single(Calls("fitDefault"));
        var options = Assert.IsType<MapInitOptions>(Assert.Single(Calls("init")).Args[0]);
        Assert.Null(options.RestoreCamera);
        Assert.Single(Calls("setSelection"));
        Assert.Single(Calls("flyToMember"));
    }

    [Fact(DisplayName = "[R1-12] A map that comes back starts at the camera the person left, with no default fit and no first flight, and the selection is still shown")]
    public void ARestoredMap_StartsAtTheCameraTheyLeft_WithoutTheDefaultFitOrTheFirstFlight()
    {
        var camera = HomeCamera();

        RenderMap(selection: Queen, restore: camera);

        var options = Assert.IsType<MapInitOptions>(Assert.Single(Calls("init")).Args[0]);
        Assert.Same(camera, options.RestoreCamera);
        Assert.Empty(Calls("fitDefault"));
        Assert.Single(Calls("setSelection"));   // the glow is set
        Assert.Empty(Calls("flyToMember"));   // the camera stays where it was
    }

    [Fact]
    public void ARestoredMap_FliesToTheSelectionsThatComeAfterIt()
    {
        var cut = RenderMap(selection: Queen, restore: HomeCamera());
        Assert.Empty(Calls("flyToMember"));

        cut.Render(parameters => parameters.Add(map => map.Selection, King));

        Assert.Equal(2, Calls("setSelection").Count);
        Assert.Equal(King.Id, Assert.Single(Calls("flyToMember")).Args[0]);
    }

    [Fact]
    public async Task ARestoredMap_StillFliesAgainOnARepeatTap()
    {
        var cut = RenderMap(selection: Queen, restore: HomeCamera());

        await Handler(cut).PinTapAsync("member", Queen.Id);

        Assert.Equal(Queen.Id, Assert.Single(Calls("flyToMember")).Args[0]);
    }

    [Fact]
    public void TheRestoreCamera_IsReadOnce_WhenTheMapIsCreated()
    {
        var cut = RenderMap(selection: null, restore: HomeCamera());
        var inits = Calls("init").Count;

        cut.Render(parameters => parameters.Add(map => map.RestoreCamera, null));

        Assert.Equal(inits, Calls("init").Count);
        Assert.Empty(Calls("fitDefault"));
    }

    // ---- the chip follows the selection (01 section 4.4, review R1-03) --------------------------------------------------------------------------------------------

    [Fact(DisplayName = "[AC-17b] A new selection sends the members and the vehicles again, and the chip is the selected one's")]
    public void ANewSelection_ResendsTheSectionsThatCarryTheChips_WithTheChipOfTheSelected()
    {
        var cut = RenderMapWithData(selection: null);
        Assert.Equal([DemoCast.King.Id], ChipOwners(LastMembers()));
        Assert.DoesNotContain(LastVehicles().Vehicles, vehicle => vehicle.Chip is not null);
        var sent = Calls("upsertMembers").Count;

        cut.Render(parameters => parameters.Add(map => map.Selection, Queen));

        Assert.Equal(sent + 1, Calls("upsertMembers").Count);
        var members = LastMembers();
        Assert.Equal([DemoCast.Queen.Id], ChipOwners(members));
        Assert.Equal("Driving · 54 mph", members.Members.Single(item => item.Id == DemoCast.Queen.Id).Chip);

        cut.Render(parameters => parameters.Add(map => map.Selection, Wagon));

        Assert.Empty(ChipOwners(LastMembers()));   // the king is not selected, so his chip is gone as well
        Assert.Equal("Parked · Engine off", LastVehicles().Vehicles.Single(vehicle => vehicle.Id == DemoCast.Wagon.Id).Chip);

        cut.Render(parameters => parameters.Add(map => map.Selection, null));

        Assert.Equal([DemoCast.King.Id], ChipOwners(LastMembers()));   // nothing selected: mine again
        Assert.All(LastVehicles().Vehicles, vehicle => Assert.Null(vehicle.Chip));
    }

    [Fact]
    public void ARenderWithTheSameSelection_SendsNoPayloadAgain()
    {
        var cut = RenderMapWithData(selection: Queen);
        var members = Calls("upsertMembers").Count;
        var vehicles = Calls("upsertVehicles").Count;

        cut.Render(parameters => parameters.Add(map => map.Selection, new EntityRef(EntityKind.Member, DemoCast.Queen.Id)));   // equal by value, not the same instance

        Assert.Equal(members, Calls("upsertMembers").Count);
        Assert.Equal(vehicles, Calls("upsertVehicles").Count);
    }

    [Fact]
    public void TheMinute_ResendsTheVehiclesOnlyWhileAVehicleIsSelected()
    {
        // "Last heard 1 hr ago" is the one time-based chip of a vehicle; with nothing selected a quiet map sends nothing on the minute.
        var cut = RenderMapWithData(selection: null);
        var vehicles = Calls("upsertVehicles").Count;
        cut.Render(parameters => parameters.Add(map => map.Now, DemoDataSource.Anchor.AddMinutes(1)));
        Assert.Equal(vehicles, Calls("upsertVehicles").Count);

        cut.Render(parameters => parameters.Add(map => map.Selection, Wagon));
        vehicles = Calls("upsertVehicles").Count;
        cut.Render(parameters => parameters.Add(map => map.Now, DemoDataSource.Anchor.AddMinutes(2)));

        Assert.Equal(vehicles + 1, Calls("upsertVehicles").Count);
    }

    // ---- a dropped circuit -------------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task ARepeatTap_AfterTheMapWasDisposed_FliesNowhere()
    {
        var cut = RenderMap(selection: Queen);
        var flights = Calls("flyToMember").Count;
        await cut.Instance.DisposeAsync();

        await Handler(cut).PinTapAsync("member", Queen.Id);

        Assert.Equal(Queen, Assert.Single(_pinTaps));
        Assert.Equal(flights, Calls("flyToMember").Count);
    }

    // ---- the recentre button (01 section 4.11, AC-20) -----------------------------------------------------------------------------------------------------------

    [Fact(DisplayName = "[AC-20] RecenterAsync asks the script to recenter once per call, and neither sends a selection nor flies to one")]
    public async Task RecenterAsync_AsksTheScript_AndLeavesTheSelectionAlone()
    {
        var cut = RenderMap(selection: King);
        var selections = Calls("setSelection").Count;
        var flights = Calls("flyToMember").Count;
        Assert.Empty(Calls("recenter"));

        await cut.InvokeAsync(() => cut.Instance.RecenterAsync());
        await cut.InvokeAsync(() => cut.Instance.RecenterAsync());

        Assert.Equal(2, Calls("recenter").Count);
        Assert.Empty(Calls("recenter")[0].Args);
        Assert.Equal(selections, Calls("setSelection").Count);
        Assert.Equal(flights, Calls("flyToMember").Count);
    }

    [Fact]
    public async Task RecenterAsync_AfterTheMapIsGone_DoesNothing()
    {
        var cut = RenderMap(selection: null);
        await cut.Instance.DisposeAsync();

        await cut.Instance.RecenterAsync();

        Assert.Empty(Calls("recenter"));
    }

    // ---- helpers -------------------------------------------------------------------------------------------------------------------------------------------------------

    private static IMapEventHandler Handler(IRenderedComponent<MapView> cut) => cut.Instance;

    // A camera over the demo home zone, built from the fixture and never from a literal (D82); the zoom is not a position.
    private static CameraState HomeCamera()
    {
        double[] center = [DemoPlaces.Home.Lon, DemoPlaces.Home.Lat];
        return new CameraState(center, 14, [center, center], Animated: false, LastDurationMs: 0, RecenterState.Away, UserInitiated: true);
    }

    private IReadOnlyList<RecordedCall> Calls(string identifier) => [.. _js.Module.Where(call => call.Identifier == identifier)];

    // Renders the map the way the page does and waits until it has started: the module imported, init answered and the first fit (or the restore) done.
    private IRenderedComponent<MapView> RenderMap(EntityRef? selection, CameraState? restore = null)
    {
        var cut = Render<MapView>(parameters => parameters
            .Add(map => map.Selection, selection)
            .Add(map => map.RestoreCamera, restore)
            .Add(map => map.OnPinTap, EventCallback.Factory.Create<EntityRef>(this, entity => { _pinTaps.Add(entity); }))
            .Add(map => map.OnBubbleTap, EventCallback.Factory.Create<IReadOnlyList<string>>(this, ids => { _bubbleTaps.Add([.. ids]); }))
            .Add(map => map.OnMapTap, EventCallback.Factory.Create(this, () => { _mapTaps++; }))
            .Add(map => map.OnCameraChanged, EventCallback.Factory.Create<CameraState>(this, _ => { _cameras++; }))
            .Add(map => map.OnFollowEnded, EventCallback.Factory.Create(this, () => { _followEnds++; })));
        cut.WaitForAssertion(() =>
        {
            Assert.Contains(_js.Module, call => call.Identifier == "init");
            Assert.True(restore is not null || _js.Module.Any(call => call.Identifier == "fitDefault"), "The default fit runs last on a fresh start.");
        });
        return cut;
    }

    private static List<string> ChipOwners(MembersPayload payload) => [.. payload.Members.Where(item => item.Chip is not null).Select(item => item.Id)];

    private MembersPayload LastMembers() => Assert.IsType<MembersPayload>(Calls("upsertMembers")[^1].Args[0]);

    private VehiclesPayload LastVehicles() => Assert.IsType<VehiclesPayload>(Calls("upsertVehicles")[^1].Args[0]);

    // The map with the Demo's people, vehicles and places and the Demo's frozen clock, so the payloads carry real chips.
    private IRenderedComponent<MapView> RenderMapWithData(EntityRef? selection)
    {
        var snapshot = new DemoRealmSessionFactory().Create(null).Current;
        var cut = Render<MapView>(parameters => parameters
            .Add(map => map.Members, snapshot.Members)
            .Add(map => map.Vehicles, snapshot.Vehicles)
            .Add(map => map.Places, snapshot.Places)
            .Add(map => map.Now, DemoDataSource.Anchor)
            .Add(map => map.Selection, selection)
            .Add(map => map.OnPinTap, EventCallback.Factory.Create<EntityRef>(this, entity => { _pinTaps.Add(entity); })));
        cut.WaitForAssertion(() =>
        {
            Assert.Contains(_js.Module, call => call.Identifier == "upsertMembers");
            Assert.Contains(_js.Module, call => call.Identifier == "upsertVehicles");
        });
        return cut;
    }

    private sealed record RecordedCall(string Identifier, object?[] Args);

    // The script, answered by hand: the module of realmMap.js records its calls, init says the payload schema is the server's, and everything else returns nothing.
    private sealed class RecordingJs : IJSRuntime
    {
        public List<RecordedCall> Module { get; } = [];

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            identifier == "import" ? new ValueTask<TValue>((TValue)(object)new RecordingModule(Module)) : new ValueTask<TValue>(default(TValue)!);   // null-forgiving: a runtime call other than the import has no result to read here

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            InvokeAsync<TValue>(identifier, args);
    }

    private sealed class RecordingModule(List<RecordedCall> calls) : IJSObjectReference
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            calls.Add(new RecordedCall(identifier, args ?? []));

            object? result = identifier switch
            {
                "init" => new ReadyInfo("test", MapInterop.PayloadSchema, "6.11.2"),
                "getCamera" => HomeCamera(),
                _ => null,
            };
            return new ValueTask<TValue>((TValue)result!);   // null-forgiving: a void call's result is never read
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            InvokeAsync<TValue>(identifier, args);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
