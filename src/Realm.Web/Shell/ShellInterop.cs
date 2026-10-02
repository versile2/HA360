using Microsoft.JSInterop;
using Realm.Web.Layout;
using Realm.Web.Map;

namespace Realm.Web.Shell;

/// <summary>What the browser reports about its window (03 section 4.8): CSS pixels, the safe-area insets, and the two media preferences.</summary>
/// <param name="Orientation"><c>portrait</c> or <c>landscape</c>.</param>
public sealed record ViewportInfo(double Width, double Height, double Dpr, Padding Safe, bool ReducedMotion, bool PrefersContrast, string Orientation);

/// <summary>What <c>init</c> of <c>realmShell.js</c> returns. Preferences and the stored camera arrive with the slices that own them.</summary>
public sealed record ShellInfo(ViewportInfo Viewport);

/// <summary>
/// The <c>[JSInvokable]</c> methods <c>realmShell.js</c> calls (03 section 4.8), held by one <c>DotNetObjectReference</c> that
/// <see cref="ShellInterop"/> creates and disposes. A forwarder with no logic of its own.
/// </summary>
public sealed class ShellCallbacks(Func<ViewportInfo, Task> onViewport)
{
    /// <summary>A resize, an orientation change or a visual-viewport resize, debounced 100 ms by the script.</summary>
    [JSInvokable]
    public Task OnViewport(ViewportInfo viewport) => onViewport(viewport);
}

/// <summary>
/// The typed face of <c>wwwroot/js/realmShell.js</c> (03 section 4.8): the viewport, the layout attribute on <c>&lt;html&gt;</c> and the sheet
/// metrics. Create it after the first render only (the circuit exists then; prerendering never runs it) and dispose it with the component.
/// The script catches its own errors, so a dropped circuit (<see cref="JSDisconnectedException"/>) is the only failure handled here.
/// </summary>
public sealed class ShellInterop : IAsyncDisposable
{
    /// <summary>The module as a plain relative URL, resolved by the browser against <c>&lt;base href&gt;</c> (D62): never <c>@Assets</c>, never a leading slash.</summary>
    public const string ModulePath = "./js/realmShell.js";

    /// <summary>
    /// The selector <c>observeSheet</c> follows (03 section 4.8): the MudX popover (<c>div[mudsheet]</c>), or our contract element in the aside host.
    /// A selector list returns the first match in document order, which is the popover when it exists: it is the whole sheet, the handle row included.
    /// </summary>
    public const string SheetSelector = "div[mudsheet], [data-testid=\"sheet\"]";

    private readonly IJSObjectReference _module;
    private readonly DotNetObjectReference<ShellCallbacks> _callbacks;
    private bool _disposed;

    private ShellInterop(IJSObjectReference module, DotNetObjectReference<ShellCallbacks> callbacks)
    {
        _module = module;
        _callbacks = callbacks;
    }

    /// <summary>Imports the module. On failure the reference to the callbacks is released before the exception leaves.</summary>
    public static async ValueTask<ShellInterop> CreateAsync(IJSRuntime js, ShellCallbacks callbacks)
    {
        var reference = DotNetObjectReference.Create(callbacks);
        try
        {
            var module = await js.InvokeAsync<IJSObjectReference>("import", ModulePath);
            return new ShellInterop(module, reference);
        }
        catch
        {
            reference.Dispose();
            throw;
        }
    }

    /// <summary>Starts the viewport reports and loads the MudX script the sheet needs; idempotent. Null when the circuit is gone.</summary>
    public async ValueTask<ShellInfo?> InitAsync()
    {
        if (_disposed)
        {
            return null;
        }

        try
        {
            return await _module.InvokeAsync<ShellInfo>("init", _callbacks);
        }
        catch (JSDisconnectedException)
        {
            return null;
        }
    }

    /// <summary>Sets <c>&lt;html data-layout&gt;</c>, which the CSS reads (the stack anchor, the sheet geometry). <see cref="LayoutMode.Unknown"/> removes it.</summary>
    public ValueTask SetLayoutAttrAsync(LayoutMode mode) =>
        CallAsync("setLayoutAttr", mode switch { LayoutMode.Compact => "compact", LayoutMode.Expanded => "expanded", _ => null });

    /// <summary>Follows the sheet element: <c>--realm-sheet-h</c> and <c>--realm-sheet-top</c> on <c>&lt;html&gt;</c>, and the metrics the map reads. Null stops.</summary>
    public ValueTask ObserveSheetAsync(string? selector) => CallAsync("observeSheet", selector);

    /// <summary>Stops the script's listeners and releases the module and the reference to the callbacks. Safe to call twice and after the circuit is gone.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            await _module.InvokeVoidAsync("dispose");
            await _module.DisposeAsync();
        }
        catch (JSDisconnectedException)
        {
            // The circuit is gone, so the page and the listeners with it.
        }
        finally
        {
            _callbacks.Dispose();
        }
    }

    private async ValueTask CallAsync(string identifier, params object?[] args)
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            await _module.InvokeVoidAsync(identifier, args);
        }
        catch (JSDisconnectedException)
        {
            // A late call after the circuit dropped; there is nothing to update.
        }
    }
}
