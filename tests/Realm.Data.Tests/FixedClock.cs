namespace Realm.Data.Tests;

/// <summary>A clock frozen at one instant (no wall clock in tests); only <see cref="GetUtcNow"/> is needed here.</summary>
internal sealed class FixedClock : TimeProvider
{
    private readonly DateTimeOffset _now;

    public FixedClock(DateTimeOffset now)
    {
        _now = now;
    }

    public override DateTimeOffset GetUtcNow()
    {
        return _now;
    }
}
