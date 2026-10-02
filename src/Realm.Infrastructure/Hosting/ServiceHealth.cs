namespace Realm.Infrastructure.Hosting;

/// <summary>
/// What a background service reports about itself (03 section 2.14): healthy, or faulted with a reason. <see cref="ResilientLoop"/> sets it;
/// the diagnostics builder reads it from any thread. The reason is the exception type, never a message, so it carries no values.
/// </summary>
public sealed class ServiceHealth
{
    private volatile Snapshot _snapshot = new(false, null, 0);

    public ServiceHealth(string name)
    {
        Name = name;
    }

    public string Name { get; }

    /// <summary>True from a failed iteration until the loop is started again after its back-off.</summary>
    public bool IsFaulted => _snapshot.IsFaulted;

    /// <summary>The exception type that faulted the service; null while healthy.</summary>
    public string? Reason => _snapshot.Reason;

    /// <summary>How many iterations have failed since the process started.</summary>
    public int FaultCount => _snapshot.FaultCount;

    internal void MarkHealthy()
    {
        _snapshot = new Snapshot(false, null, _snapshot.FaultCount);
    }

    internal void MarkFaulted(string reason)
    {
        _snapshot = new Snapshot(true, reason, _snapshot.FaultCount + 1);
    }

    private sealed record Snapshot(bool IsFaulted, string? Reason, int FaultCount);
}
