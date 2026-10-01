namespace Realm.TestKit;

/// <summary>What <see cref="FakeHaServer"/> does with one websocket connection attempt; queue them with <see cref="FakeHaServer.ScriptNext"/>.</summary>
public sealed record FakeHaAttempt
{
    /// <summary>Answer the upgrade request with this HTTP status instead of a websocket (the Supervisor answers 502 while Core restarts); null accepts it.</summary>
    public int? RejectStatus { get; init; }

    /// <summary>Accept the websocket and then say nothing at all, not even <c>auth_required</c>.</summary>
    public bool Stall { get; init; }

    /// <summary>Accept the connection and play the protocol (the default).</summary>
    public static FakeHaAttempt Accept() => new();

    /// <summary>Refuse the upgrade with <paramref name="status"/> before any websocket exists.</summary>
    public static FakeHaAttempt Reject(int status) => new() { RejectStatus = status };

    /// <summary>Accept the upgrade and stay silent until the client gives up.</summary>
    public static FakeHaAttempt Silent() => new() { Stall = true };
}
