namespace Realm.Infrastructure.Diagnostics;

/// <summary>
/// How many things happened in the last 60 seconds: one counter per second, in a ring of 60 slots, each slot remembering which second it holds. It reads the
/// instants it is given (the caller's <see cref="TimeProvider"/>), never a clock of its own, and a slot of an older minute is overwritten when its second
/// comes round again, so nothing grows and nothing needs a timer. A very short lock guards the ring: it is touched once per message.
/// </summary>
internal sealed class RateWindow
{
    private const int Slots = 60;

    private readonly Lock _gate = new();
    private readonly long[] _second = new long[Slots];
    private readonly long[] _count = new long[Slots];

    /// <summary>Counts one event that happened at <paramref name="at"/>.</summary>
    public void Add(DateTimeOffset at)
    {
        var second = at.ToUnixTimeSeconds();
        var slot = SlotOf(second);
        lock (_gate)
        {
            if (_second[slot] != second)
            {
                _second[slot] = second;
                _count[slot] = 0;
            }

            _count[slot]++;
        }
    }

    /// <summary>The events of the 60 seconds that end with the second of <paramref name="now"/> (that second included).</summary>
    public int PerMinute(DateTimeOffset now)
    {
        var current = now.ToUnixTimeSeconds();
        long total = 0;
        lock (_gate)
        {
            for (var slot = 0; slot < Slots; slot++)
            {
                if (_second[slot] > current - Slots)
                {
                    total += _count[slot];
                }
            }
        }

        return total > int.MaxValue ? int.MaxValue : (int)total;
    }

    private static int SlotOf(long second) => (int)(((second % Slots) + Slots) % Slots);
}
