namespace Realm.Infrastructure.Ha;

/// <summary>
/// The latest <see cref="HaDiscoveryResult"/>, shared by everything that needs to know who the members are: the discovery refresher writes it, the
/// avatar service, the data source (time zone, "me") and the pipeline's callers read it. Reads never block.
/// </summary>
public sealed class DiscoveryState
{
    private volatile Entry _current = new(HaDiscoveryResult.Empty, 0);

    /// <summary>The latest result; <see cref="HaDiscoveryResult.Empty"/> before the first discovery has finished.</summary>
    public HaDiscoveryResult Current => _current.Result;

    /// <summary>0 before the first discovery, then counting up with every <see cref="Publish"/>.</summary>
    public long Version => _current.Version;

    /// <summary>Replaces the result. Only the discovery refresher calls it.</summary>
    public void Publish(HaDiscoveryResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        _current = new Entry(result, _current.Version + 1);
    }

    private sealed record Entry(HaDiscoveryResult Result, long Version);
}
