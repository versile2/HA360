using Microsoft.JSInterop;
using Realm.Web.Map;

namespace Realm.Web.Components.Map;

/// <summary>
/// The typed face of <c>wwwroot/js/realmMap.js</c> (03 section 4.3): one method per export, camelCase JSON through the interop
/// serializer, and the one <c>DotNetObjectReference</c> that carries <see cref="MapCallbacks"/> into the script. Create it after the first
/// render only (the circuit exists then, and prerendering never runs it); dispose it with the component. The exports catch their own
/// errors and report them through <c>OnError</c>, so nothing here defends against a script exception, and a disconnected circuit is
/// not an error.
/// </summary>
public sealed class MapInterop : IAsyncDisposable
{
    /// <summary>The payload schema this class speaks; <c>init</c> returns the script's, and a mismatch means a stale cached script (03 section 4.1).</summary>
    public const int PayloadSchema = 1;

    /// <summary>
    /// The module, as a plain relative URL that the browser resolves against the page's <c>&lt;base href&gt;</c> (03 section 4.1, D62); never
    /// <c>@Assets</c> and never a path starting with a slash, so it works under the Ingress prefix.
    /// </summary>
    public const string ModulePath = "./js/realmMap.js";

    private readonly IJSObjectReference _module;
    private readonly DotNetObjectReference<MapCallbacks> _callbacks;
    private bool _disposed;

    private MapInterop(IJSObjectReference module, DotNetObjectReference<MapCallbacks> callbacks)
    {
        _module = module;
        _callbacks = callbacks;
    }

    /// <summary>Imports the module. On failure the reference to <paramref name="callbacks"/> is released before the exception leaves.</summary>
    public static async ValueTask<MapInterop> CreateAsync(IJSRuntime js, MapCallbacks callbacks)
    {
        var reference = DotNetObjectReference.Create(callbacks);
        try
        {
            var module = await js.InvokeAsync<IJSObjectReference>("import", ModulePath);
            return new MapInterop(module, reference);
        }
        catch
        {
            reference.Dispose();
            throw;
        }
    }

    /// <summary>Creates the map in the element named by <see cref="MapInitOptions.ContainerId"/>; idempotent for the same element. The script reports a missing WebGL 2 through <c>OnWebGlUnavailable</c>.</summary>
    public async ValueTask<ReadyInfo?> InitAsync(MapInitOptions options)
    {
        if (_disposed)
        {
            return null;
        }

        try
        {
            return await _module.InvokeAsync<ReadyInfo>("init", options, _callbacks);
        }
        catch (JSDisconnectedException)
        {
            return null;
        }
    }

    /// <summary>Switches the style; the outcome also arrives through <c>OnStyleResult</c>. Null when the circuit is gone.</summary>
    public async ValueTask<StyleResult?> SetStyleAsync(string styleId)
    {
        if (_disposed)
        {
            return null;
        }

        try
        {
            return await _module.InvokeAsync<StyleResult>("setStyle", styleId);
        }
        catch (JSDisconnectedException)
        {
            return null;
        }
    }

    public ValueTask SetLayoutAsync(LayoutPayload layout) => CallAsync("setLayout", layout);

    /// <summary>Null is the measured mode (the padding follows the layout); a value forces it (tests, the aside host).</summary>
    public ValueTask SetPaddingAsync(Padding? padding) => CallAsync("setPadding", padding);

    public ValueTask UpsertMembersAsync(MembersPayload payload) => CallAsync("upsertMembers", payload);

    public ValueTask UpsertVehiclesAsync(VehiclesPayload payload) => CallAsync("upsertVehicles", payload);

    public ValueTask SetZonesAsync(ZonesPayload payload) => CallAsync("setZones", payload);

    public ValueTask SetDefaultTargetsAsync(DefaultTargets targets) => CallAsync("setDefaultTargets", targets);

    /// <summary>Runs the default camera; the first load passes <c>false</c> so it does not animate.</summary>
    public ValueTask FitDefaultAsync(bool animate) => CallAsync("fitDefault", new { animate });

    public ValueTask ResizeAsync() => CallAsync("resize");

    public ValueTask SetReducedMotionAsync(bool on) => CallAsync("setReducedMotion", on);

    /// <summary>Tears the map down, then releases the module and the reference to <see cref="MapCallbacks"/>. Safe to call twice and after the circuit is gone.</summary>
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
            // The circuit is gone, so the page and the map with it.
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
