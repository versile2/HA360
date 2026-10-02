using System.Text;

namespace Realm.Infrastructure.Ha;

/// <summary>
/// Settings of <see cref="HaWebSocketConnection"/>. The defaults are the contract of 03 section 2.5; tests point <see cref="Endpoint"/> at a fake server
/// and fix the jitter. The token is a plain string that is only ever sent as the <c>access_token</c> of the auth message (03 section 9.2); the record's
/// <c>ToString</c> leaves it out, so logging the options cannot leak it.
/// </summary>
public sealed record HaWebSocketOptions
{
    /// <summary>The Supervisor's websocket proxy to Home Assistant Core.</summary>
    public static readonly Uri SupervisorEndpoint = new("ws://supervisor/core/websocket");

    public Uri Endpoint { get; init; } = SupervisorEndpoint;

    /// <summary><c>SUPERVISOR_TOKEN</c>. Null or empty: the connection stays <see cref="HaConnectionState.NotConfigured"/> and does nothing.</summary>
    public string? Token { get; init; }

    /// <summary>The budget for connect, <c>auth_required</c>, <c>auth</c>, <c>auth_ok</c> and the answer to the subscription.</summary>
    public TimeSpan HandshakeTimeout { get; init; } = TimeSpan.FromSeconds(10);

    public TimeSpan PingInterval { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>No <c>pong</c> within this long after a ping forces a reconnect.</summary>
    public TimeSpan PongTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>The fixed wait between attempts after <c>auth_invalid</c>.</summary>
    public TimeSpan AuthRetryInterval { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>The waits between attempts, one per failure in a row; the last one repeats (02 section 1.1).</summary>
    public IReadOnlyList<TimeSpan> Backoff { get; init; } =
    [
        TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30),
    ];

    /// <summary>A connection that lasted this long starts the back-off over.</summary>
    public TimeSpan StableAfter { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>How long a clean close waits for HA's answer.</summary>
    public TimeSpan CloseTimeout { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>The factor applied to a back-off wait: 0.8 to 1.2, which is the 20 % jitter of 03 section 2.5. A test passes a constant.</summary>
    public Func<double> Jitter { get; init; } = () => 0.8 + (Random.Shared.NextDouble() * 0.4);

    /// <summary>A message larger than this closes the socket and reconnects (03 section 2.5).</summary>
    public int MaxMessageBytes { get; init; } = 16 * 1024 * 1024;

    /// <summary>A message nested deeper than this closes the socket and reconnects.</summary>
    public int MaxJsonDepth { get; init; } = 64;

    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Endpoint = ").Append(Endpoint);
        return true;
    }
}
