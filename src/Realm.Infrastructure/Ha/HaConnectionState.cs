namespace Realm.Infrastructure.Ha;

/// <summary>The states of the HA websocket (03 section 9.3). <see cref="HaConnectionStatus.ToConnectionVm"/> maps them onto the UI's <c>ConnectionVm</c>.</summary>
public enum HaConnectionState
{
    /// <summary>No token: the connection does nothing.</summary>
    NotConfigured,

    /// <summary>The first attempt since start is under way.</summary>
    Connecting,

    /// <summary>The socket is open and the handshake or the subscription is under way.</summary>
    Authenticating,

    /// <summary>Authenticated and subscribed.</summary>
    Connected,

    /// <summary>A connection was lost or an attempt failed; the next attempt waits for its back-off.</summary>
    Reconnecting,

    /// <summary>HA answered <c>auth_invalid</c>; the next attempt is a minute away.</summary>
    AuthFailed,
}
