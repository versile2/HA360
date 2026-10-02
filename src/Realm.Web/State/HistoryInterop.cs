using Microsoft.JSInterop;
using Realm.Web.Shell;

namespace Realm.Web.State;

/// <summary>
/// The typed face of the history and Esc part of <c>wwwroot/js/realmShell.js</c> (03 sections 3.7 and 4.8), and the production <see cref="IHistoryPort"/>. It imports the same module
/// URL as <see cref="ShellInterop"/> and <see cref="DevicePrefs"/>, so the browser holds one instance of the script, but it has its own entry (<c>attachHistory</c>, not <c>init</c>):
/// <c>init</c> tears the viewport state down on every call, and the Driving page needs the history without a <see cref="ShellInterop"/>. Create it after the first render only (the
/// circuit exists then), through <see cref="AttachAsync"/>, and dispose the lease it returns with the page.
/// </summary>
public sealed class HistoryInterop : IHistoryPort
{
    private readonly IJSObjectReference _module;
    private readonly DotNetObjectReference<HistoryCallbacks> _callbacks;
    private readonly int _token;
    private bool _disposed;

    private HistoryInterop(IJSObjectReference module, DotNetObjectReference<HistoryCallbacks> callbacks, int token)
    {
        _module = module;
        _callbacks = callbacks;
        _token = token;
    }

    /// <summary>
    /// Starts the script's <c>popstate</c> and Escape listeners for the page on screen, attaches the port to <paramref name="sync"/> and brings the history to its depth. The script keeps one
    /// attachment per document: a later page's attach replaces this one, and this one's dispose then leaves it alone.
    /// </summary>
    /// <param name="sync">The circuit's history sync; it also says whether the tokens are on.</param>
    /// <param name="js">The circuit's JavaScript runtime.</param>
    /// <param name="surface">The page that attaches.</param>
    /// <param name="onLeave">Driving only: what the page does when Back pops the sentinel (see <see cref="HistorySync.AttachAsync"/>).</param>
    /// <returns>The lease: dispose it with the page.</returns>
    public static async ValueTask<IAsyncDisposable> AttachAsync(HistorySync sync, IJSRuntime js, HistorySurface surface, Func<Task>? onLeave = null)
    {
        ArgumentNullException.ThrowIfNull(sync);
        ArgumentNullException.ThrowIfNull(js);
        var reference = DotNetObjectReference.Create(new HistoryCallbacks(sync));
        IJSObjectReference? module = null;
        try
        {
            module = await js.InvokeAsync<IJSObjectReference>("import", ShellInterop.ModulePath);
            var token = await module.InvokeAsync<int>("attachHistory", reference, new { historyTokens = sync.Tokens });
            return await sync.AttachAsync(new HistoryInterop(module, reference, token), surface, onLeave);
        }
        catch
        {
            if (module is not null)
            {
                await ReleaseModuleAsync(module);
            }

            reference.Dispose();
            throw;
        }
    }

    /// <inheritdoc />
    public async ValueTask SetDepthAsync(int depth)
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            await _module.InvokeVoidAsync("history.setDepth", depth);
        }
        catch (JSDisconnectedException)
        {
            // The circuit dropped; the page and the entries it pushed are the browser's to forget.
        }
    }

    /// <summary>Detaches the script's listeners (only if this is still the attachment) and releases the module and the reference to the callbacks. Safe to call twice and after the circuit is gone.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            await _module.InvokeVoidAsync("detachHistory", _token);
        }
        catch (JSDisconnectedException)
        {
            // The circuit is gone, so the page and the listeners with it.
        }
        finally
        {
            await ReleaseModuleAsync(_module);
            _callbacks.Dispose();
        }
    }

    private static async ValueTask ReleaseModuleAsync(IJSObjectReference module)
    {
        try
        {
            await module.DisposeAsync();
        }
        catch (JSDisconnectedException)
        {
            // Nothing left to release on the browser's side.
        }
    }
}
