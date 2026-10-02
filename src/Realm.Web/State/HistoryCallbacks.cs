using Microsoft.JSInterop;

namespace Realm.Web.State;

/// <summary>
/// The <c>[JSInvokable]</c> methods <c>realmShell.js</c> calls for the history and Esc (03 section 4.8), held by the one <c>DotNetObjectReference</c> that
/// <see cref="HistoryInterop"/> creates and disposes. A forwarder with no logic of its own: <see cref="HistorySync"/> decides.
/// </summary>
public sealed class HistoryCallbacks(HistorySync sync)
{
    /// <summary>A <c>popstate</c> the script did not cause itself: the browser is now at <paramref name="newDepth"/> (lower for a Back, higher for a Forward).</summary>
    [JSInvokable]
    public async Task OnHistoryBack(int newDepth) => await sync.HandleBackAsync(newDepth);

    /// <summary>The Escape key, once the script found no dialog, popover or text field that owns it.</summary>
    [JSInvokable]
    public async Task OnEscape() => await sync.HandleEscapeAsync();
}
