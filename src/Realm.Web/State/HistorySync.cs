using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;

namespace Realm.Web.State;

/// <summary>
/// The glue between <see cref="RealmUiState"/> and the browser's history (03 section 3.7): keeps one history entry (<c>#r&lt;n&gt;</c>) per level of depth, so that the Android Back
/// gesture steps through the sheet and the overlays instead of leaving the app, and turns a Back the browser reports into exactly one reduction. One instance per circuit
/// (scoped); a page attaches a port for as long as it is on screen.
/// </summary>
/// <remarks>
/// <para>
/// C# is authoritative. Every change of the UI state (a row, the handle, the empty map, an overlay opening or closing, a fold of the window) raises
/// <see cref="RealmUiState.Changed"/>, and this class asks the port for <see cref="TargetDepth"/>; the port's <c>setDepth</c> is idempotent, so the same path serves both
/// directions: a push when the depth grew, a suppressed <c>history.go</c> when it shrank. The port is only asked when the depth differs from what it was last asked for.
/// </para>
/// <para>
/// A Back the person made arrives as <see cref="HandleBackAsync"/> with the depth the browser is at now. One reduction is applied (the topmost overlay closes, otherwise
/// <see cref="BackReducer"/> takes one step, each of which lowers the depth by exactly 1), and the port is then asked for the resulting depth, so a Back by several entries or
/// a Forward converges. Esc is <see cref="HandleEscapeAsync"/>: the topmost overlay, then Back steps 2 and 3 only. On the Driving page the depth is the one sentinel entry plus the
/// open popups, and the Back that pops the sentinel asks the page to go to Location.
/// </para>
/// <para>
/// With the history tokens off (<see cref="Tokens"/>) the port records the depth and the browser history is never touched, so none of this reaches the Android Back, which
/// leaves the app; Esc, the in-app back arrow and the reducers are unchanged.
/// </para>
/// </remarks>
public sealed partial class HistorySync
{
    private readonly RealmUiState _ui;
    private readonly ILogger<HistorySync> _logger;
    private IHistoryPort? _port;
    private HistorySurface _surface;
    private Func<Task>? _onLeave;
    private int _requested;
    private bool _suspended;
    private bool _reducing;

    /// <summary>Creates the sync of one circuit's <paramref name="ui"/> and listens to its changes.</summary>
    /// <param name="ui">The circuit's UI state.</param>
    /// <param name="options">The history switch; the tokens are on when omitted.</param>
    /// <param name="logger">Where a refused browser call is noted; nowhere when omitted.</param>
    public HistorySync(RealmUiState ui, HistoryOptions? options = null, ILogger<HistorySync>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(ui);
        _ui = ui;
        Tokens = options?.Tokens ?? true;
        _logger = logger ?? NullLogger<HistorySync>.Instance;
        _ui.Changed += OnUiChanged;
    }

    /// <summary>Raised after each Back, Escape or back-arrow step that changed something (never for <see cref="BackStep.Leave"/>), with the step it took: what the focus logic reads.</summary>
    public event Action<BackStep>? BackTaken;

    /// <summary>Whether the browser history is driven at all (<c>Realm:Ui:HistoryTokens</c>); the script is told when a port is created.</summary>
    public bool Tokens { get; }

    /// <summary>Whether a page has attached a port.</summary>
    public bool IsAttached => _port is not null;

    /// <summary>The page the attached port belongs to; <see cref="HistorySurface.Location"/> before any attach.</summary>
    public HistorySurface Surface => _surface;

    /// <summary>The depth the browser history should have: the sheet's <see cref="RealmUiState.Depth"/> on Location; on Driving the sentinel plus the open overlays.</summary>
    public int TargetDepth => _surface == HistorySurface.Driving ? _ui.Overlays.Count + 1 : _ui.Depth;

    /// <summary>
    /// Attaches the browser end for the page that is on screen and brings the history to <see cref="TargetDepth"/>. A page that was attached before is superseded: its own lease
    /// then detaches nothing. The returned lease releases the port when it is disposed, and only if it is still the attached one.
    /// </summary>
    /// <param name="port">The browser end.</param>
    /// <param name="surface">The page it belongs to.</param>
    /// <param name="onLeave">
    /// Driving only: what the page does when the person goes Back past the sentinel (navigate to Location, replacing the entry). It runs on the context that raised the callback, so
    /// it marshals itself.
    /// </param>
    public async ValueTask<IAsyncDisposable> AttachAsync(IHistoryPort port, HistorySurface surface, Func<Task>? onLeave = null)
    {
        ArgumentNullException.ThrowIfNull(port);
        _port = port;
        _surface = surface;
        _onLeave = onLeave;
        _suspended = false;
        await PushAsync(port, TargetDepth);
        return new Lease(this, port);
    }

    /// <summary>
    /// Leaves the page for another tab (03 section 3.7, "Tab navigation"): takes the history back to the base entry (<c>setDepth(0)</c>) and awaits it, so no <c>#r&lt;n&gt;</c> entry
    /// stays above the page, and stops following the state until the next page attaches. The caller navigates, replacing the entry, once this completes. Does nothing when no page is attached.
    /// </summary>
    public async ValueTask ReleaseAsync()
    {
        if (_port is not { } port)
        {
            return;
        }

        _suspended = true;
        await PushAsync(port, 0);
    }

    /// <summary>Asks the port for <see cref="TargetDepth"/> if that is not what it was last asked for. The state changes do this by themselves; an explicit call is for a caller that awaits it.</summary>
    public ValueTask SyncAsync()
    {
        if (_suspended || _port is not { } port)
        {
            return ValueTask.CompletedTask;
        }

        var target = TargetDepth;
        return target == _requested ? ValueTask.CompletedTask : new ValueTask(PushAsync(port, target));
    }

    /// <summary>
    /// One Back made in the page (the detail's back arrow): the same reduction the Android Back runs. Returns the step it took; <see cref="BackStep.Leave"/> changes nothing.
    /// </summary>
    public async ValueTask<BackStep> BackAsync()
    {
        BackStep step;
        _reducing = true;
        try
        {
            step = _ui.Back();
        }
        finally
        {
            _reducing = false;
        }

        if (step != BackStep.Leave)
        {
            BackTaken?.Invoke(step);
        }

        await ConvergeAsync();
        return step;
    }

    /// <summary>
    /// A <c>popstate</c> the script did not cause itself (03 section 3.7): the browser is now at <paramref name="browserDepth"/>. Lower than where C# last put it is a Back: one reduction is
    /// applied and the port converges on the result. The same depth or a higher one is a Forward, which is undone: the port is asked for <see cref="TargetDepth"/> again. Returns the step
    /// taken, or null when the report changed nothing. A report that arrives after the page detached is ignored.
    /// </summary>
    /// <param name="browserDepth">The depth the history is at, read from the entry's token.</param>
    public async ValueTask<BackStep?> HandleBackAsync(int browserDepth)
    {
        if (_suspended || _port is null)
        {
            return null;
        }

        var expected = _requested;
        _requested = Math.Max(0, browserDepth);
        if (browserDepth >= expected)
        {
            await ConvergeAsync();
            return null;
        }

        BackStep step;
        _reducing = true;
        try
        {
            step = BackOnce();
        }
        finally
        {
            _reducing = false;
        }

        if (step == BackStep.Leave)
        {
            if (_surface == HistorySurface.Driving)
            {
                // The person popped the sentinel on purpose: it stays popped, and the page goes to Location.
                _suspended = true;
                if (_onLeave is { } leave)
                {
                    await leave();
                }
            }
            else
            {
                await ConvergeAsync();
            }

            return step;
        }

        BackTaken?.Invoke(step);
        await ConvergeAsync();
        return step;
    }

    /// <summary>
    /// The Escape key (03 section 3.6): the topmost overlay closes first, otherwise Back steps 2 and 3 only (<see cref="BackReducer.Escape"/>); it never closes the sheet or collapses a list.
    /// On Driving only an overlay can close. Returns the step taken, or null when there was nothing for Esc to do.
    /// </summary>
    public async ValueTask<BackStep?> HandleEscapeAsync()
    {
        if (_suspended || _port is null)
        {
            return null;
        }

        BackStep? step;
        _reducing = true;
        try
        {
            step = _surface == HistorySurface.Driving && _ui.Overlays.Count == 0 ? null : _ui.Escape();
        }
        finally
        {
            _reducing = false;
        }

        if (step is { } taken)
        {
            BackTaken?.Invoke(taken);
            await ConvergeAsync();
        }

        return step;
    }

    // Driving has no sheet: with no overlay open the only thing Back can do there is leave (the sentinel popped), and it must not reduce the Location state that waits underneath.
    private BackStep BackOnce() => _surface == HistorySurface.Driving && _ui.Overlays.Count == 0 ? BackStep.Leave : _ui.Back();

    private void OnUiChanged()
    {
        if (_reducing || _suspended || _port is not { } port)
        {
            return;
        }

        var target = TargetDepth;
        if (target != _requested)
        {
            _ = PushAsync(port, target);
        }
    }

    private Task ConvergeAsync() => _suspended || _port is not { } port ? Task.CompletedTask : PushAsync(port, TargetDepth);

    // Never throws: a dropped circuit or a failed script call leaves the history as it was, and the next change asks again.
    private async Task PushAsync(IHistoryPort port, int depth)
    {
        _requested = depth;
        try
        {
            await port.SetDepthAsync(depth);
        }
        catch (Exception exception) when (exception is JSException or JSDisconnectedException or ObjectDisposedException or OperationCanceledException or InvalidOperationException)
        {
            LogPortFailure(_logger, exception);
        }
    }

    private void Detach(IHistoryPort port)
    {
        if (ReferenceEquals(_port, port))
        {
            _port = null;
            _onLeave = null;
        }
    }

    [LoggerMessage(EventId = 7101, Level = LogLevel.Debug, Message = "The browser history could not be updated; the circuit is gone or the script refused the call.")]
    private static partial void LogPortFailure(ILogger logger, Exception exception);

    private sealed class Lease(HistorySync owner, IHistoryPort port) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            owner.Detach(port);
            return port.DisposeAsync();
        }
    }
}
