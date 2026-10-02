using Realm.Web.Map;

namespace Realm.Web.Components.Map;

/// <summary>
/// What <see cref="MapCallbacks"/> forwards to: one method per call of <c>realmMap.js</c> into .NET (03 section 4.7). <c>MapView</c>
/// implements it, and a test can implement it to watch the callbacks without a browser.
/// </summary>
public interface IMapEventHandler
{
    /// <summary>The map finished its first load.</summary>
    Task ReadyAsync(ReadyInfo info);

    /// <summary>A pin was tapped, or a zone with no pin under the finger; <paramref name="kind"/> is <c>member</c>, <c>vehicle</c> or <c>place</c>.</summary>
    Task PinTapAsync(string kind, string id);

    /// <summary>The empty map was tapped.</summary>
    Task MapTapAsync();

    /// <summary>An edge bubble was tapped (not the viewer's own, which is the recenter): one member id for a single-member bubble, two or more for a cluster, whose camera fit the script has already run (03 section 4.6).</summary>
    Task BubbleTapAsync(IReadOnlyList<string> ids);

    /// <summary>A camera move settled.</summary>
    Task CameraChangedAsync(CameraState camera);

    /// <summary>The camera stopped following a driving member (a pan, zoom, bubble tap or recentre, a new selection, or the member stopped driving); raised only when Follow was running (01 section 4.14, R1-14).</summary>
    Task FollowEndedAsync();

    /// <summary>A style switch finished, well or badly.</summary>
    Task StyleResultAsync(StyleResult result);

    /// <summary>The map could not be built (no WebGL 2, or the library did not load).</summary>
    Task WebGlUnavailableAsync();

    /// <summary>The script caught an exception; <paramref name="message"/> is at most 300 characters and carries no payload data.</summary>
    Task ErrorAsync(string area, string message);
}
