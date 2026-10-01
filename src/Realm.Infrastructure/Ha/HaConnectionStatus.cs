using Realm.Domain;

namespace Realm.Infrastructure.Ha;

/// <summary>
/// A snapshot of what <see cref="HaWebSocketConnection"/> knows about itself, and the mapping of 02 section 1.8 onto the UI's
/// <see cref="ConnectionVm"/>. The mapping depends on the clock (an outage turns from Reconnecting into Unavailable by itself),
/// so it is a method of <c>now</c> that the consumer calls on every change and on its own tick.
/// </summary>
/// <param name="State">The raw state.</param>
/// <param name="OutageSinceUtc">When contact with HA was last lost (or the first attempt started); null while connected.</param>
/// <param name="LastActivityUtc">The last state event or ping reply, and the moment the subscription was acknowledged; null before the first connection.</param>
/// <param name="Attempt">Connection attempts since the last successful connection.</param>
/// <param name="NextAttemptUtc">When the next attempt starts, while the connection waits for its back-off; otherwise null.</param>
public sealed record HaConnectionStatus(
    HaConnectionState State,
    DateTimeOffset? OutageSinceUtc,
    DateTimeOffset? LastActivityUtc,
    int Attempt,
    DateTimeOffset? NextAttemptUtc)
{
    /// <summary>The first 15 s of an outage read Reconnecting, then Unavailable (02 section 1.8).</summary>
    public static readonly TimeSpan ReconnectingWindow = TimeSpan.FromSeconds(15);

    /// <summary>An open socket counts as Connected while a state event or a ping reply arrived within this long (02 section 1.8).</summary>
    public static readonly TimeSpan SilenceLimit = TimeSpan.FromSeconds(90);

    /// <summary>
    /// 02 section 1.8 for <c>HomeAssistant</c>: Connected while the socket is open and something arrived within 90 s; Reconnecting during the first 15 s
    /// of an outage (a connection or attempt that is not yet established counts from the start of the outage); Unavailable after that, and at once for
    /// <see cref="HaConnectionState.AuthFailed"/> and <see cref="HaConnectionState.NotConfigured"/> (03 section 9.3).
    /// </summary>
    public ConnectionState ToConnectionState(DateTimeOffset now)
    {
        DateTimeOffset outageStart;
        switch (State)
        {
            case HaConnectionState.NotConfigured:
            case HaConnectionState.AuthFailed:
                return ConnectionState.Unavailable;
            case HaConnectionState.Connected:
                // Open but silent for too long: the outage began when the silence limit ran out.
                var lastHeard = LastActivityUtc ?? now;
                if (now - lastHeard <= SilenceLimit)
                {
                    return ConnectionState.Connected;
                }

                outageStart = lastHeard + SilenceLimit;
                break;
            default:
                outageStart = OutageSinceUtc ?? now;
                break;
        }

        return now - outageStart <= ReconnectingWindow ? ConnectionState.Reconnecting : ConnectionState.Unavailable;
    }

    /// <summary>The <c>HomeAssistant</c> entry of <c>RealmSnapshot.Connections</c>.</summary>
    public ConnectionVm ToConnectionVm(DateTimeOffset now)
    {
        return new ConnectionVm(ConnectionNames.HomeAssistant, ToConnectionState(now), LastActivityUtc);
    }
}
