using Microsoft.JSInterop;
using Realm.Domain;
using Realm.Web.Layout;
using Realm.Web.Map;
using Realm.Web.Shell;
using Realm.Web.State;
using Xunit;

namespace Realm.Web.Tests;

/// <summary>
/// <see cref="HistoryInterop"/>, the production <see cref="IHistoryPort"/>, against a fake <see cref="IJSRuntime"/> (03 sections 3.7 and 4.8): it imports the module the shell and the preferences
/// import, attaches the script's listeners with the switch, forwards the depth through <c>history.setDepth</c>, detaches only its own attachment, and a dropped circuit is not an error.
/// No browser; the script itself is tested in <c>tests/js/realmShellHistory.test.mjs</c>.
/// </summary>
public sealed class HistoryInteropTests
{
    private static readonly EntityRef Jester = new(EntityKind.Member, "jester");

    private static RealmUiState NewUi()
    {
        var ui = new RealmUiState { Layout = new LayoutSnapshot(LayoutMode.Compact, false, 412, 915, new Padding(0, 0, 0, 0)) };
        ui.Apply(new SheetEvent.PinTap(Jester));
        return ui;
    }

    [Fact]
    public async Task AttachAsync_ImportsTheShellModule_AttachesWithTheSwitch_AndBringsTheHistoryToTheDepth()
    {
        var js = new FakeJs();
        var sync = new HistorySync(NewUi(), new HistoryOptions(Tokens: true));

        await using var lease = await HistoryInterop.AttachAsync(sync, js, HistorySurface.Location);

        var import = Assert.Single(js.Runtime);
        Assert.Equal("import", import.Identifier);
        Assert.Equal([ShellInterop.ModulePath], import.Args);
        Assert.Equal("./js/realmShell.js", ShellInterop.ModulePath);   // the very URL of the shell: the browser holds one instance of the script
        Assert.Equal(["attachHistory", "history.setDepth"], js.Module.Select(call => call.Identifier));
        var attach = js.Module[0];
        Assert.IsType<DotNetObjectReference<HistoryCallbacks>>(attach.Args[0]);
        Assert.Equal(true, attach.Args[1]?.GetType().GetProperty("historyTokens")?.GetValue(attach.Args[1]));
        Assert.Equal([1], js.Module[1].Args);   // the selection is already there: depth 1
        Assert.True(sync.IsAttached);
    }

    [Fact]
    public async Task AttachAsync_WithTheTokensOff_TellsTheScript()
    {
        var js = new FakeJs();
        var sync = new HistorySync(NewUi(), new HistoryOptions(Tokens: false));

        await using var lease = await HistoryInterop.AttachAsync(sync, js, HistorySurface.Driving);

        Assert.Equal(false, js.Module[0].Args[1]?.GetType().GetProperty("historyTokens")?.GetValue(js.Module[0].Args[1]));
        Assert.Equal(HistorySurface.Driving, sync.Surface);
        Assert.Equal([1], js.Module[1].Args);   // the sentinel of the Driving page
    }

    [Fact]
    public async Task ChangesOfTheState_ReachTheScriptThroughSetDepth()
    {
        var js = new FakeJs();
        var ui = NewUi();
        var sync = new HistorySync(ui);
        await using var lease = await HistoryInterop.AttachAsync(sync, js, HistorySurface.Location);

        ui.Apply(new SheetEvent.HandleToggle());
        ui.Apply(new SheetEvent.ClearTap());

        Assert.Equal([1, 2, 0], js.Module.Where(call => call.Identifier == "history.setDepth").Select(call => (int)call.Args[0]!));   // null-forgiving: setDepth always has its depth
    }

    [Fact]
    public async Task Dispose_DetachesWithItsToken_ReleasesTheModuleAndTheReference_AndIsSafeTwice()
    {
        var js = new FakeJs { Token = 7 };
        var sync = new HistorySync(NewUi());
        var lease = await HistoryInterop.AttachAsync(sync, js, HistorySurface.Location);
        var callbacks = Assert.IsType<DotNetObjectReference<HistoryCallbacks>>(js.Module[0].Args[0]);

        await lease.DisposeAsync();
        await lease.DisposeAsync();

        var detach = Assert.Single(js.Module, call => call.Identifier == "detachHistory");
        Assert.Equal([7], detach.Args);
        Assert.Equal(1, js.ModuleDisposed);
        Assert.Throws<ObjectDisposedException>(() => callbacks.Value);
        Assert.False(sync.IsAttached);
    }

    [Fact]
    public async Task ADroppedCircuit_IsNotAnError_NeitherForTheDepthNorForTheDispose()
    {
        var js = new FakeJs();
        var ui = NewUi();
        var sync = new HistorySync(ui);
        var lease = await HistoryInterop.AttachAsync(sync, js, HistorySurface.Location);
        js.Disconnected = true;

        ui.Apply(new SheetEvent.HandleToggle());   // the sync asks the port, the port swallows the dropped circuit
        await lease.DisposeAsync();

        Assert.Equal(new SheetBody.Detail(Jester), ui.Body);
        Assert.Equal(1, js.ModuleDisposed);
    }

    [Fact]
    public async Task WhenTheScriptRefusesToAttach_TheModuleAndTheReferenceAreReleased_AndTheErrorLeaves()
    {
        var js = new FakeJs { AttachError = new JSException("no such export") };
        var sync = new HistorySync(NewUi());

        await Assert.ThrowsAsync<JSException>(async () => await HistoryInterop.AttachAsync(sync, js, HistorySurface.Location));

        Assert.Equal(1, js.ModuleDisposed);
        Assert.False(sync.IsAttached);
    }

    [Fact]
    public async Task WhenTheImportFails_TheErrorLeaves_AndNothingIsAttached()
    {
        var js = new FakeJs { ImportError = new JSException("no such module") };
        var sync = new HistorySync(NewUi());

        await Assert.ThrowsAsync<JSException>(async () => await HistoryInterop.AttachAsync(sync, js, HistorySurface.Location));

        Assert.False(sync.IsAttached);
    }

    private sealed record Call(string Identifier, object?[] Args);

    private sealed class FakeJs : IJSRuntime
    {
        public List<Call> Runtime { get; } = [];

        public List<Call> Module { get; } = [];

        public int ModuleDisposed { get; private set; }

        public int Token { get; init; } = 3;

        public Exception? ImportError { get; init; }

        public Exception? AttachError { get; init; }

        public bool Disconnected { get; set; }

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
            if (identifier == "attachHistory" && js.AttachError is not null)
            {
                throw js.AttachError;
            }

            if (js.Disconnected && identifier != "attachHistory")
            {
                throw new JSDisconnectedException("The circuit is gone.");
            }

            object? result = identifier == "attachHistory" ? js.Token : null;
            return new ValueTask<TValue>((TValue)result!);   // null-forgiving: a void call's result is never read
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            InvokeAsync<TValue>(identifier, args);

        public ValueTask DisposeAsync()
        {
            js.Disposed();
            return ValueTask.CompletedTask;
        }
    }
}
