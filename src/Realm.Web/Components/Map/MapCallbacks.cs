using Microsoft.JSInterop;
using Realm.Web.Map;

namespace Realm.Web.Components.Map;

/// <summary>
/// The <c>[JSInvokable]</c> methods <c>realmMap.js</c> calls (03 section 4.7), held by one <c>DotNetObjectReference</c> that
/// <see cref="MapInterop"/> creates and disposes. A thin forwarder: it contains no logic, so each method is the name JavaScript uses and
/// one call on the <see cref="IMapEventHandler"/>.
/// </summary>
public sealed class MapCallbacks
{
    private readonly IMapEventHandler _handler;

    public MapCallbacks(IMapEventHandler handler) => _handler = handler;

    [JSInvokable]
    public Task OnReady(ReadyInfo info) => _handler.ReadyAsync(info);

    [JSInvokable]
    public Task OnPinTap(string kind, string id) => _handler.PinTapAsync(kind, id);

    [JSInvokable]
    public Task OnMapTap() => _handler.MapTapAsync();

    [JSInvokable]
    public Task OnBubbleTap(string[] ids) => _handler.BubbleTapAsync(ids);

    [JSInvokable]
    public Task OnCameraChanged(CameraState camera) => _handler.CameraChangedAsync(camera);

    [JSInvokable]
    public Task OnFollowEnded() => _handler.FollowEndedAsync();

    [JSInvokable]
    public Task OnStyleResult(StyleResult result) => _handler.StyleResultAsync(result);

    [JSInvokable]
    public Task OnWebGlUnavailable() => _handler.WebGlUnavailableAsync();

    [JSInvokable]
    public Task OnError(string area, string message) => _handler.ErrorAsync(area, message);
}
