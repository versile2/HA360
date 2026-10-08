using System.Threading.Channels;

namespace Realm.Infrastructure.Ha;

/// <summary>
/// A bell that asks the discovery refresher to read the zones again now (0.2.2, D119): rung after a place was created, so that the new zone is on the map in a
/// moment and not at the next five-minute refresh. One bell is enough (a second ring while one is waiting adds nothing).
/// </summary>
public sealed class ZoneRefreshSignal
{
    private readonly Channel<bool> _bell = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
        SingleReader = true,
    });

    /// <summary>The refresher's end: completes when the bell was rung.</summary>
    public ChannelReader<bool> Reader => _bell.Reader;

    /// <summary>Asks for a zone refresh.</summary>
    public void Request() => _bell.Writer.TryWrite(true);
}
