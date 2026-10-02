using Microsoft.JSInterop;
using Realm.Domain;
using Realm.Web.Layout;
using Realm.Web.Map;
using Realm.Web.State;
using Xunit;

namespace Realm.Web.Tests;

/// <summary>
/// <see cref="HistorySync"/> on a real <see cref="RealmUiState"/> with only the browser's end faked (<see cref="FakeHistoryPort"/>): the depth tokens of 03 section 3.7 with the flag on and
/// off, a Back the browser reports (one entry, several entries, a Forward), Esc, the overlay layers, the Driving sentinel, the tab change, a page that supersedes another, and a browser
/// that fails. The JavaScript half of the same contract is tested in <c>tests/js/realmShellHistory.test.mjs</c>.
/// </summary>
public sealed class HistorySyncTests
{
    private static readonly EntityRef Jester = new(EntityKind.Member, "jester");
    private static readonly EntityRef Hall = new(EntityKind.Place, "hall");

    private static RealmUiState NewUi(LayoutMode mode = LayoutMode.Compact) =>
        new() { Layout = new LayoutSnapshot(mode, false, mode == LayoutMode.Compact ? 412 : 900, 915, new Padding(0, 0, 0, 0)) };

    private static async Task<(HistorySync Sync, FakeHistoryPort Port, IAsyncDisposable Lease)> AttachAsync(
        RealmUiState ui,
        bool tokens = true,
        HistorySurface surface = HistorySurface.Location,
        Func<Task>? onLeave = null)
    {
        var sync = new HistorySync(ui, new HistoryOptions(tokens));
        var port = new FakeHistoryPort(tokens);
        var lease = await sync.AttachAsync(port, surface, onLeave);
        return (sync, port, lease);
    }

    // The detail of Jester: depth 2 in Compact (the selection, and the size above Peek).
    private static RealmUiState AtDetail()
    {
        var ui = NewUi();
        ui.Apply(new SheetEvent.PinTap(Jester));
        ui.Apply(new SheetEvent.HandleToggle());
        return ui;
    }

    // ---- the depth follows the state ------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Attach_BringsTheHistoryToTheDepthOfTheStateThatIsAlreadyThere()
    {
        var ui = AtDetail();

        var (sync, port, _) = await AttachAsync(ui);

        Assert.True(sync.IsAttached);
        Assert.Equal(HistorySurface.Location, sync.Surface);
        Assert.Equal(2, sync.TargetDepth);
        Assert.Equal(2, port.Entries);
        Assert.Equal(2, port.Recorded);
    }

    [Fact]
    public async Task EveryChangeOfTheState_PushesOrPopsEntriesToTheNewDepth()
    {
        var ui = NewUi();
        var (_, port, _) = await AttachAsync(ui);
        Assert.Equal(0, port.Entries);

        ui.Apply(new SheetEvent.PinTap(Jester));   // 0 to 1: a push
        Assert.Equal((1, 1), (port.Entries, port.Calls));

        ui.Apply(new SheetEvent.HandleToggle());   // 1 to 2: a push
        Assert.Equal((2, 2), (port.Entries, port.Calls));

        ui.Apply(new SheetEvent.HandleSet(SheetSize.Peek));   // 2 to 1: one go, back to the header
        Assert.Equal((1, 3), (port.Entries, port.Calls));

        ui.Apply(new SheetEvent.ClearTap());   // 1 to 0
        Assert.Equal((0, 4), (port.Entries, port.Calls));
        Assert.Equal(0, port.Recorded);
    }

    [Fact]
    public async Task ASelectionMadeAtTall_KeepsTheDepth_AndTouchesNoEntry()
    {
        var ui = NewUi();
        ui.Apply(new SheetEvent.HandleSet(SheetSize.Tall));
        var (_, port, _) = await AttachAsync(ui);
        var requests = port.Requests;

        ui.Apply(new SheetEvent.RowTap(Hall));   // the selection adds a level, the collapse takes one away

        Assert.Equal(1, port.Entries);
        Assert.Equal(requests, port.Requests);
    }

    [Fact]
    public async Task WithTheTokensOff_ThePortOnlyRecords_AndTheHistoryIsNeverTouched()
    {
        var ui = NewUi();
        var (sync, port, _) = await AttachAsync(ui, tokens: false);

        ui.Apply(new SheetEvent.PinTap(Jester));
        ui.Apply(new SheetEvent.HandleToggle());
        ui.OpenOverlay(new OverlayLayer(OverlayKind.Settings));

        Assert.False(sync.Tokens);
        Assert.Equal(3, port.Recorded);
        Assert.Equal(0, port.Entries);
        Assert.Equal(0, port.Calls);
    }

    [Fact]
    public async Task WithTheTokensOff_TheBackArrowAndEscStillRunTheReducers()
    {
        var ui = AtDetail();
        var (sync, port, _) = await AttachAsync(ui, tokens: false);

        Assert.Equal(BackStep.Detail, await sync.BackAsync());
        Assert.Equal(new SheetBody.Header(Jester), ui.Body);
        Assert.Equal(1, port.Recorded);

        Assert.Equal(BackStep.Selection, await sync.HandleEscapeAsync());
        Assert.Equal(0, port.Recorded);
        Assert.Equal(0, port.Calls);
    }

    // ---- a Back the browser reports --------------------------------------------------------------------------------------------------------------------------------

    [Fact(DisplayName = "[AC-24] The Back gesture steps through the detail, the header and the list, one entry each, and the next one leaves")]
    public async Task UserBack_StepsThroughTheChain_OneEntryAtATime()
    {
        var ui = AtDetail();
        var (sync, port, _) = await AttachAsync(ui);
        var taken = new List<BackStep>();
        sync.BackTaken += taken.Add;

        var detail = await sync.HandleBackAsync(port.UserPop());
        Assert.Equal(BackStep.Detail, detail);
        Assert.Equal(new SheetBody.Header(Jester), ui.Body);
        Assert.Equal(1, port.Entries);

        var header = await sync.HandleBackAsync(port.UserPop());
        Assert.Equal(BackStep.Selection, header);
        Assert.Equal(SheetState.Initial, ui.Sheet);
        Assert.Equal(0, port.Entries);

        // Nothing is left to pop: the next Back leaves the app, which this document never hears about, and the in-page arrow reports it as Leave without changing anything.
        Assert.Equal(BackStep.Leave, await sync.BackAsync());
        Assert.Equal(new[] { BackStep.Detail, BackStep.Selection }, taken);
        Assert.Equal(2, port.Calls);   // the two pushes of the attach; no go was needed, the browser had popped the entries itself
    }

    [Fact]
    public async Task UserBack_ByTwoEntriesAtOnce_AppliesOneReduction_AndPushesTheMissingEntryAgain()
    {
        var ui = AtDetail();
        var (sync, port, _) = await AttachAsync(ui);
        var taken = new List<BackStep>();
        sync.BackTaken += taken.Add;
        var callsBefore = port.Calls;

        var step = await sync.HandleBackAsync(port.UserPop(2));

        Assert.Equal(BackStep.Detail, step);
        Assert.Equal(new[] { BackStep.Detail }, taken);   // one reduction for one gesture
        Assert.Equal(new SheetBody.Header(Jester), ui.Body);
        Assert.Equal(1, ui.Depth);
        Assert.Equal(1, port.Entries);   // the entry of the header is pushed again
        Assert.Equal(callsBefore + 1, port.Calls);
    }

    [Fact]
    public async Task AForward_ChangesNothing_AndIsUndone()
    {
        var ui = NewUi();
        ui.Apply(new SheetEvent.PinTap(Jester));
        var (sync, port, _) = await AttachAsync(ui);

        var step = await sync.HandleBackAsync(port.UserForward(1));

        Assert.Null(step);
        Assert.Equal(new SheetBody.Header(Jester), ui.Body);
        Assert.Equal(1, port.Entries);   // the browser went back down to the depth of the state
    }

    [Fact]
    public async Task ABackReportedWhileNoPageIsAttached_IsIgnored()
    {
        var ui = AtDetail();
        var (sync, port, lease) = await AttachAsync(ui);
        await lease.DisposeAsync();

        var step = await sync.HandleBackAsync(port.UserPop());

        Assert.Null(step);
        Assert.Equal(new SheetBody.Detail(Jester), ui.Body);
        Assert.False(sync.IsAttached);
        Assert.True(port.Disposed);
    }

    [Fact]
    public async Task TheBackArrowOfTheDetail_RunsTheSameReduction_AndTakesTheEntryBackWithIt()
    {
        var ui = AtDetail();
        var (sync, port, _) = await AttachAsync(ui);
        var taken = new List<BackStep>();
        sync.BackTaken += taken.Add;

        var step = await sync.BackAsync();

        Assert.Equal(BackStep.Detail, step);
        Assert.Equal(new[] { BackStep.Detail }, taken);
        Assert.Equal(1, port.Entries);   // one go: the entry of the detail is gone
        Assert.Equal(3, port.Calls);
    }

    [Fact(DisplayName = "[R-086] history.go is asynchronous: a Back made in the page completes when the browser has settled, with the state already reduced")]
    public async Task TheBackArrow_CompletesOnlyWhenThePortHasSettled()
    {
        var ui = AtDetail();
        var port = new GatedPort();
        var sync = new HistorySync(ui);
        await sync.AttachAsync(port, HistorySurface.Location);
        port.Gated = true;

        var back = sync.BackAsync().AsTask();

        Assert.False(back.IsCompleted);
        Assert.Equal(new[] { 2, 1 }, port.Requested);
        Assert.Equal(1, ui.Depth);   // the state is reduced first, the browser follows
        port.ReleaseAll();
        Assert.Equal(BackStep.Detail, await back);
    }

    // ---- Esc ---------------------------------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Esc_ClosesTheTopmostOverlayFirst_ThenStepsTwoAndThreeOnly()
    {
        var ui = AtDetail();
        ui.OpenOverlay(new OverlayLayer(OverlayKind.Dialog, "popup"));
        var (sync, port, _) = await AttachAsync(ui);
        var taken = new List<BackStep>();
        sync.BackTaken += taken.Add;
        Assert.Equal(3, port.Entries);

        Assert.Equal(BackStep.Overlay, await sync.HandleEscapeAsync());
        Assert.Equal(BackStep.Detail, await sync.HandleEscapeAsync());
        Assert.Equal(BackStep.Selection, await sync.HandleEscapeAsync());

        Assert.Equal(new[] { BackStep.Overlay, BackStep.Detail, BackStep.Selection }, taken);
        Assert.Equal(0, port.Entries);   // each step took its entry back
    }

    [Fact]
    public async Task Esc_NeverCollapsesAListAtTall_AndNeverLeaves()
    {
        var ui = NewUi();
        ui.Apply(new SheetEvent.HandleSet(SheetSize.Tall));
        var (sync, port, _) = await AttachAsync(ui);
        var requests = port.Requests;

        Assert.Null(await sync.HandleEscapeAsync());
        Assert.Equal(SheetSize.Tall, ui.SheetSize);

        ui.Apply(new SheetEvent.HandleSet(SheetSize.Peek));
        Assert.Null(await sync.HandleEscapeAsync());   // the list at Peek: nothing for Esc to do either
        Assert.Equal(requests + 1, port.Requests);   // only the size change asked the port
    }

    // ---- overlay layers --------------------------------------------------------------------------------------------------------------------------------------------------

    [Fact(DisplayName = "[AC-38] An open overlay is one history layer: opening pushes an entry, closing it by its own button takes the entry back")]
    public async Task AnOverlay_IsOneLayer_PushedWhenOpened_AndPoppedWhenClosed()
    {
        var ui = NewUi();
        var (_, port, _) = await AttachAsync(ui);
        var settings = new OverlayLayer(OverlayKind.Settings);

        ui.OpenOverlay(settings);
        Assert.Equal(1, port.Entries);

        Assert.True(ui.CloseOverlay(settings));
        Assert.Equal(0, port.Entries);
        Assert.Equal(2, port.Calls);
    }

    [Fact]
    public async Task TheBackGesture_ClosesTheOverlay_AndAsksItsOwnerToTakeItDown()
    {
        var ui = NewUi();
        var closed = 0;
        var settings = new OverlayLayer(OverlayKind.Settings);
        var (sync, port, _) = await AttachAsync(ui);
        ui.OpenOverlay(settings, () => closed++);

        var step = await sync.HandleBackAsync(port.UserPop());

        Assert.Equal(BackStep.Overlay, step);
        Assert.Equal(1, closed);
        Assert.Empty(ui.Overlays);
        Assert.Equal(0, port.Entries);
        Assert.False(ui.CloseOverlay(settings));   // the owner's own close, a moment later, finds nothing to do
    }

    [Fact]
    public async Task OverlaysAboveTheSheet_AreBackedOutOfFirst()
    {
        var ui = NewUi();
        ui.Apply(new SheetEvent.PinTap(Jester));
        var (sync, port, _) = await AttachAsync(ui);
        ui.OpenOverlay(new OverlayLayer(OverlayKind.Settings));
        ui.OpenOverlay(new OverlayLayer(OverlayKind.Popover, "map-style"));
        Assert.Equal(3, port.Entries);

        var steps = new List<BackStep?>
        {
            await sync.HandleBackAsync(port.UserPop()),
            await sync.HandleBackAsync(port.UserPop()),
            await sync.HandleBackAsync(port.UserPop()),
        };

        Assert.Equal(new BackStep?[] { BackStep.Overlay, BackStep.Overlay, BackStep.Selection }, steps);
        Assert.Equal(SheetState.Initial, ui.Sheet);
        Assert.Equal(0, port.Entries);
    }

    // ---- the Driving page ------------------------------------------------------------------------------------------------------------------------------------------------

    [Fact(DisplayName = "[AC-38] On Driving the page pushes one sentinel and each popup is one more layer; Back closes the popup, then leaves for Location")]
    public async Task Driving_HasASentinel_APopupIsALayer_AndBackFromTheSentinelLeaves()
    {
        var ui = NewUi();
        ui.Apply(new SheetEvent.PinTap(Jester));   // the Location state that waits underneath must not move
        var left = 0;
        var closed = 0;
        var (sync, port, _) = await AttachAsync(ui, surface: HistorySurface.Driving, onLeave: () =>
        {
            left++;
            return Task.CompletedTask;
        });
        var taken = new List<BackStep>();
        sync.BackTaken += taken.Add;
        Assert.Equal(HistorySurface.Driving, sync.Surface);
        Assert.Equal(1, sync.TargetDepth);   // the sentinel; the sheet's depth is not Driving's
        Assert.Equal(1, port.Entries);

        ui.OpenOverlay(new OverlayLayer(OverlayKind.Dialog, "stat-popup"), () => closed++);
        Assert.Equal(2, port.Entries);

        Assert.Equal(BackStep.Overlay, await sync.HandleBackAsync(port.UserPop()));
        Assert.Equal((1, 1, 0), (closed, port.Entries, left));

        Assert.Equal(BackStep.Leave, await sync.HandleBackAsync(port.UserPop()));
        Assert.Equal(1, left);
        Assert.Equal(0, port.Entries);   // the sentinel stays popped
        Assert.Equal(new SheetBody.Header(Jester), ui.Body);   // Location's state is untouched
        Assert.Equal(new[] { BackStep.Overlay }, taken);   // Leave raises nothing

        var requests = port.Requests;
        ui.Apply(new SheetEvent.HandleToggle());
        Assert.Equal(requests, port.Requests);   // the page is on its way out: nothing follows the state any more
    }

    [Fact]
    public async Task Driving_EscClosesAPopup_AndHasNothingElseToDo()
    {
        var ui = NewUi();
        ui.Apply(new SheetEvent.PinTap(Jester));
        var (sync, port, _) = await AttachAsync(ui, surface: HistorySurface.Driving);

        Assert.Null(await sync.HandleEscapeAsync());
        Assert.Equal(new SheetBody.Header(Jester), ui.Body);

        ui.OpenOverlay(new OverlayLayer(OverlayKind.Settings));
        Assert.Equal(2, port.Entries);
        Assert.Equal(BackStep.Overlay, await sync.HandleEscapeAsync());
        Assert.Equal(1, port.Entries);
    }

    // ---- a tab change, and a page that replaces another -------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Release_TakesTheHistoryBackToTheBaseEntry_AndStopsFollowingUntilTheNextPageAttaches()
    {
        var ui = AtDetail();
        var (sync, port, _) = await AttachAsync(ui);

        await sync.ReleaseAsync();

        Assert.Equal(0, port.Entries);
        var requests = port.Requests;
        ui.Apply(new SheetEvent.HandleSet(SheetSize.Peek));
        Assert.Equal(requests, port.Requests);
        Assert.Null(await sync.HandleBackAsync(0));
        Assert.Null(await sync.HandleEscapeAsync());
        Assert.Equal(new SheetBody.Header(Jester), ui.Body);

        var next = new FakeHistoryPort(tokens: true);
        await sync.AttachAsync(next, HistorySurface.Location);   // the page the tab leads to
        Assert.Equal(ui.Depth, next.Entries);
    }

    [Fact]
    public async Task Release_WithNoPageAttached_DoesNothing()
    {
        var sync = new HistorySync(NewUi());

        await sync.ReleaseAsync();

        Assert.False(sync.IsAttached);
    }

    [Fact]
    public async Task ALaterAttach_SupersedesTheEarlierOne_AndTheEarlierLeaseLeavesTheNewPageAlone()
    {
        var ui = NewUi();
        var sync = new HistorySync(ui);
        var first = new FakeHistoryPort(tokens: true);
        var second = new FakeHistoryPort(tokens: true);
        var firstLease = await sync.AttachAsync(first, HistorySurface.Driving);
        var secondLease = await sync.AttachAsync(second, HistorySurface.Location);

        await firstLease.DisposeAsync();   // the page that was replaced goes away a moment later

        Assert.True(first.Disposed);
        Assert.False(second.Disposed);
        Assert.True(sync.IsAttached);
        Assert.Equal(HistorySurface.Location, sync.Surface);
        ui.Apply(new SheetEvent.PinTap(Jester));
        Assert.Equal(1, second.Entries);

        await secondLease.DisposeAsync();
        Assert.False(sync.IsAttached);
        var requests = second.Requests;
        ui.Apply(new SheetEvent.HandleToggle());
        Assert.Equal(requests, second.Requests);
    }

    // ---- a browser that fails -----------------------------------------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("disconnected")]
    [InlineData("disposed")]
    [InlineData("canceled")]
    [InlineData("invalid")]
    public async Task AFailedBrowserCall_IsSwallowed_AndTheNextChangeAsksAgain(string fault)
    {
        var ui = NewUi();
        var (sync, port, _) = await AttachAsync(ui);
        port.Fault = fault switch
        {
            "disconnected" => new JSDisconnectedException("The circuit is gone."),
            "disposed" => new ObjectDisposedException("module"),
            "canceled" => new OperationCanceledException(),
            _ => new InvalidOperationException("No JavaScript runtime."),
        };

        ui.Apply(new SheetEvent.PinTap(Jester));   // raises nothing out of the state change
        Assert.Equal(new SheetBody.Header(Jester), ui.Body);
        Assert.Equal(0, port.Entries);
        Assert.Equal(BackStep.Selection, await sync.BackAsync());   // the page's own reduction works as well

        port.Fault = null;
        ui.Apply(new SheetEvent.PinTap(Hall));
        Assert.Equal(1, port.Entries);
        Assert.Equal(1, port.Recorded);
    }

    // ---- a page that is not attached yet --------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task BeforeAnyPageIsAttached_TheStateChangesAsksNobody_AndTheBackArrowStillReduces()
    {
        var ui = AtDetail();
        var sync = new HistorySync(ui);

        Assert.False(sync.IsAttached);
        Assert.Equal(HistorySurface.Location, sync.Surface);
        Assert.Equal(2, sync.TargetDepth);
        Assert.Equal(BackStep.Detail, await sync.BackAsync());
        Assert.Null(await sync.HandleEscapeAsync());
        Assert.Null(await sync.HandleBackAsync(0));
        Assert.Equal(new SheetBody.Header(Jester), ui.Body);
    }

    [Fact]
    public async Task NullArguments_AreRefused()
    {
        Assert.Throws<ArgumentNullException>(() => new HistorySync(null!));   // null-forgiving: the guard clause is what is under test
        var sync = new HistorySync(NewUi());
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await sync.AttachAsync(null!, HistorySurface.Location));   // null-forgiving: the guard clause is what is under test
    }

    private sealed class GatedPort : IHistoryPort
    {
        private readonly List<TaskCompletionSource> _pending = [];

        public bool Gated { get; set; }

        public List<int> Requested { get; } = [];

        public ValueTask SetDepthAsync(int depth)
        {
            Requested.Add(depth);
            if (!Gated)
            {
                return ValueTask.CompletedTask;
            }

            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending.Add(gate);
            return new ValueTask(gate.Task);
        }

        public void ReleaseAll()
        {
            foreach (var gate in _pending)
            {
                gate.SetResult();
            }

            _pending.Clear();
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
