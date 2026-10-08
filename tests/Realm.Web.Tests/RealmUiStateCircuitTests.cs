using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Realm.Demo;
using Realm.Domain;
using Realm.Web.Components;
using Realm.Web.Layout;
using Realm.Web.Map;
using Realm.Web.Pages;
using Realm.Web.Shell;
using Realm.Web.State;
using Xunit;

namespace Realm.Web.Tests;

/// <summary>
/// What S8c adds to the circuit's UI state (03 sections 3.6 and 3.7): one <see cref="RealmUiState"/> per circuit, so that the selection outlives the page (R2-03); the overlay close actions
/// the Back gesture runs; the camera kept for the way back from Driving (R1-12, 01 section 2.2) and the once-only start-up of the page; the history switch and its registration; and the
/// JavaScript callbacks of the history. The reducers themselves are tested in <c>SheetStateMachineTests</c> and <c>BackReducerTests</c>.
/// </summary>
public sealed class RealmUiStateCircuitTests
{
    private static readonly EntityRef Jester = new(EntityKind.Member, "jester");

    private static readonly DateTimeOffset Epoch = DateTimeOffset.UnixEpoch;

    private static RealmUiState NewUi() =>
        new() { Layout = new LayoutSnapshot(LayoutMode.Compact, false, 412, 915, new Padding(0, 0, 0, 0)) };

    // A camera made of numbers the Demo cast already has (the default view's bounds and the viewer's point), never invented ones.
    private static async Task<CameraState> CameraAsync(double zoom)
    {
        await using var session = FullCast.Session(null);
        var snapshot = session.Current;
        var targets = MapPayloadFactory.Targets(snapshot.Members, snapshot.Vehicles, snapshot.Places, null, MapPayloadOptions.Default, 1)
            ?? throw new InvalidOperationException("The Demo cast has default targets.");
        var me = targets.Me ?? throw new InvalidOperationException("The Demo cast has a viewer.");
        return new CameraState(me.Center, zoom, targets.Default.Bounds, Animated: false, LastDurationMs: 0, RecenterState.Away, UserInitiated: true);
    }

    // ---- the overlay close actions ------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Back_ClosesTheTopmostOverlayOnly_AndRunsItsCloseActionAfterTheLayerLeftTheStack()
    {
        var ui = NewUi();
        var order = new List<string>();
        ui.Changed += () => order.Add($"changed {ui.Overlays.Count}");
        ui.OpenOverlay(new OverlayLayer(OverlayKind.Settings), () => order.Add($"closed settings {ui.Overlays.Count}"));
        ui.OpenOverlay(new OverlayLayer(OverlayKind.Popover, "map-style"), () => order.Add($"closed popover {ui.Overlays.Count}"));
        order.Clear();

        Assert.Equal(BackStep.Overlay, ui.Back());

        Assert.Equal(["changed 1", "closed popover 1"], order);   // the stack is already short when the owner is asked, and the state has already said so
        Assert.Equal(new OverlayLayer(OverlayKind.Settings), Assert.Single(ui.Overlays));
    }

    [Fact]
    public void Escape_ClosesTheTopmostOverlay_AndRunsItsCloseAction()
    {
        var ui = NewUi();
        var closed = 0;
        ui.OpenOverlay(new OverlayLayer(OverlayKind.Dialog, "popup"), () => closed++);

        Assert.Equal(BackStep.Overlay, ui.Escape());

        Assert.Equal(1, closed);
        Assert.Empty(ui.Overlays);
    }

    [Fact]
    public void AnOverlayClosedByItsOwner_DoesNotRunItsCloseAction()
    {
        var ui = NewUi();
        var closed = 0;
        var layer = new OverlayLayer(OverlayKind.Dialog, "popup");
        ui.OpenOverlay(layer, () => closed++);

        Assert.True(ui.CloseOverlay(layer));

        Assert.Equal(0, closed);
        Assert.Equal(BackStep.Leave, ui.Back());   // nothing is left for Back to close
        Assert.Equal(0, closed);
    }

    [Fact]
    public void AnOverlayWithNoCloseAction_IsClosedByBackAllTheSame()
    {
        var ui = NewUi();
        ui.OpenOverlay(new OverlayLayer(OverlayKind.Settings));

        Assert.Equal(BackStep.Overlay, ui.Back());
        Assert.Empty(ui.Overlays);
    }

    // ---- the camera ---------------------------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task RecordCamera_KeepsTheCamera_AndRaisesNothing()
    {
        var ui = NewUi();
        var camera = await CameraAsync(12);
        var raised = 0;
        ui.Changed += () => raised++;
        Assert.Null(ui.LastCamera);

        ui.RecordCamera(camera, Epoch);

        Assert.Same(camera, ui.LastCamera);
        Assert.Equal(0, raised);   // a pan would otherwise re-render the page about eight times a second
    }

    [Fact(DisplayName = "[R1-12] The camera comes back within ten minutes and not after them")]
    public async Task CameraToRestore_IsTheLastCamera_UntilTenMinutesHavePassed()
    {
        var ui = NewUi();
        var camera = await CameraAsync(12);
        Assert.Equal(TimeSpan.FromMinutes(10), RealmUiState.CameraRestoreWindow);
        Assert.Null(ui.CameraToRestore(Epoch));   // nothing was ever reported

        ui.RecordCamera(camera, Epoch);

        Assert.Same(camera, ui.CameraToRestore(Epoch));
        Assert.Same(camera, ui.CameraToRestore(Epoch + TimeSpan.FromMinutes(3)));
        Assert.Same(camera, ui.CameraToRestore(Epoch + RealmUiState.CameraRestoreWindow));
        Assert.Null(ui.CameraToRestore(Epoch + RealmUiState.CameraRestoreWindow + TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public async Task RecordingTheCameraAgain_StartsTheTenMinutesAgain()
    {
        // The Location page stamps its last camera again when it goes away, so the window counts from the departure and not from the last pan.
        var ui = NewUi();
        var camera = await CameraAsync(12);
        ui.RecordCamera(camera, Epoch);

        ui.RecordCamera(ui.LastCamera ?? throw new InvalidOperationException("A camera was recorded."), Epoch + TimeSpan.FromMinutes(30));

        Assert.Same(camera, ui.CameraToRestore(Epoch + TimeSpan.FromMinutes(35)));
        Assert.Null(ui.CameraToRestore(Epoch + TimeSpan.FromMinutes(41)));
    }

    // ---- the start-up of the page ------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void TryBeginStartup_IsTrueOnceInACircuit()
    {
        var ui = NewUi();

        Assert.True(ui.TryBeginStartup());
        Assert.False(ui.TryBeginStartup());
        Assert.False(ui.TryBeginStartup());
        Assert.True(NewUi().TryBeginStartup());   // another circuit has its own start
    }

    [Fact]
    public void MeId_RaisesChanged_OnlyWhenItChanges()
    {
        var ui = NewUi();
        var raised = 0;
        ui.Changed += () => raised++;

        ui.MeId = DemoCast.King.Id;
        ui.MeId = DemoCast.King.Id;

        Assert.Equal(1, raised);
        Assert.Equal(DemoCast.King.Id, ui.MeId);
    }

    // ---- one state per circuit ---------------------------------------------------------------------------------------------------------------------------------------

    [Fact(DisplayName = "[R2-03] The state is scoped, so the selection survives the page that set it")]
    public void AddRealmUiState_RegistersOneStateAndOneSyncPerCircuit()
    {
        var services = new ServiceCollection().AddLogging().AddRealmUiState().AddRealmUiState();   // twice: the second call adds nothing
        Assert.Equal(ServiceLifetime.Scoped, Assert.Single(services, descriptor => descriptor.ServiceType == typeof(RealmUiState)).Lifetime);
        Assert.Equal(ServiceLifetime.Scoped, Assert.Single(services, descriptor => descriptor.ServiceType == typeof(HistorySync)).Lifetime);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        using var circuit = provider.CreateScope();
        var page = circuit.ServiceProvider.GetRequiredService<RealmUiState>();   // the Location page of the circuit
        page.Apply(new SheetEvent.PinTap(Jester));
        page.Layout = NewUi().Layout;
        page.SelectedWeek = 2;
        var driving = circuit.ServiceProvider.GetRequiredService<RealmUiState>();   // the Driving page, and then the Location page again

        Assert.Same(page, driving);
        Assert.Equal(Jester, driving.Selection);
        Assert.Equal(2, driving.SelectedWeek);
        Assert.Equal(driving.Depth, circuit.ServiceProvider.GetRequiredService<HistorySync>().TargetDepth);   // the sync follows this very state
        Assert.Same(circuit.ServiceProvider.GetRequiredService<HistorySync>(), circuit.ServiceProvider.GetRequiredService<HistorySync>());

        using var other = provider.CreateScope();   // another circuit
        Assert.NotSame(page, other.ServiceProvider.GetRequiredService<RealmUiState>());
        Assert.Null(other.ServiceProvider.GetRequiredService<RealmUiState>().Selection);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("true", true)]
    [InlineData("True", true)]
    [InlineData("false", false)]
    [InlineData("FALSE", false)]
    [InlineData("banana", true)]
    public void HistoryTokens_AreOnUnlessTheOptionIsAFalseBoolean(string? value, bool expected)
    {
        var settings = new Dictionary<string, string?>();
        if (value is not null)
        {
            settings[HistoryOptions.TokensKey] = value;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        Assert.Equal(expected, HistoryOptions.From(configuration).Tokens);
        Assert.Equal("Realm:Ui:HistoryTokens", HistoryOptions.TokensKey);
        Assert.True(HistoryOptions.From(null).Tokens);
        Assert.True(new HistoryOptions().Tokens);
    }

    [Fact]
    public void TheSwitch_ReachesTheSyncOfTheCircuit()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [HistoryOptions.TokensKey] = "false" }).Build();
        using var provider = new ServiceCollection().AddLogging().AddRealmUiState(configuration).BuildServiceProvider();
        using var circuit = provider.CreateScope();

        Assert.False(circuit.ServiceProvider.GetRequiredService<HistorySync>().Tokens);
    }

    [Theory]
    [InlineData(typeof(LocationPage))]
    [InlineData(typeof(DrivingPage))]
    public void ThePages_DoNotOwnTheState_TheyTakeTheCircuitsOne(Type page)
    {
        const BindingFlags all = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        var fields = page.GetFields(all).Where(field => field.FieldType == typeof(RealmUiState) && !field.IsDefined(typeof(CompilerGeneratedAttribute), false));
        var members = page.GetProperties(all).Where(property => property.PropertyType == typeof(RealmUiState)).ToList();

        Assert.Empty(fields);   // no "new RealmUiState()" kept in a field: a page created again would start from nothing
        var state = Assert.Single(members);
        Assert.True(state.IsDefined(typeof(InjectAttribute), false));
        Assert.Single(page.GetProperties(all), property => property.PropertyType == typeof(HistorySync) && property.IsDefined(typeof(InjectAttribute), false));
    }

    [Fact]
    public void TheLocationPage_TakesTheViewersSlugFromTheCascadeThatRoutesNames()
    {
        var property = typeof(LocationPage).GetProperty(nameof(LocationPage.ViewerMemberId));

        var cascade = Assert.IsType<CascadingParameterAttribute>(Assert.Single(property?.GetCustomAttributes(typeof(CascadingParameterAttribute), false) ?? []));
        Assert.Equal("ViewerMemberId", cascade.Name);
        Assert.Equal(Routes.ViewerMemberIdName, cascade.Name);
        Assert.Equal(typeof(string), property?.PropertyType);
    }

    // ---- the history callbacks of the script -------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task HistoryCallbacks_ForwardTheBackAndTheEscapeToTheSync()
    {
        var ui = NewUi();
        ui.Apply(new SheetEvent.PinTap(Jester));
        var sync = new HistorySync(ui);
        var port = new FakeHistoryPort(tokens: true);
        await sync.AttachAsync(port, HistorySurface.Location);
        var callbacks = new HistoryCallbacks(sync);

        await callbacks.OnHistoryBack(port.UserPop());
        Assert.Equal(SheetState.Initial, ui.Sheet);

        ui.Apply(new SheetEvent.PinTap(Jester));
        await callbacks.OnEscape();
        Assert.Equal(SheetState.Initial, ui.Sheet);
        Assert.Equal(0, port.Entries);
    }

    [Fact]
    public void HistoryCallbacks_AreTheJsInvokableNamesTheShellScriptCalls_AndTheScriptCallsNothingElse()
    {
        static List<string> Invokable(Type type) =>
            type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(method => method.GetCustomAttribute<JSInvokableAttribute>() is not null)
                .Select(method => method.Name)
                .ToList();

        var history = Invokable(typeof(HistoryCallbacks));
        var viewport = Invokable(typeof(ShellCallbacks));
        var script = File.ReadAllText(Path.Combine(PayloadContractTests.FindRepositoryRoot(), "src", "Realm.Web", "wwwroot", "js", "realmShell.js"));
        var called = Regex.Matches(script, "'(On[A-Z][A-Za-z]+)'").Select(match => match.Groups[1].Value).ToHashSet();

        Assert.Equal(["OnEscape", "OnHistoryBack"], history.Order(StringComparer.Ordinal));
        Assert.Empty(called.Except(history.Concat(viewport)));
        Assert.Contains("OnHistoryBack", called);
        Assert.Contains("OnEscape", called);
    }
}
