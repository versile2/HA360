using System.Text;

namespace Realm.Demo;

/// <summary>
/// The seeded generator of the Demo fixture (02 section 9.1): xorshift32, seeded with the FNV-1a hash of a text (member id
/// plus week start). Integer arithmetic only, so every platform draws the same numbers and the drive lists never change.
/// </summary>
internal sealed class DemoPrng
{
    private uint _state;

    public DemoPrng(string seedText)
    {
        _state = Fnv1a(seedText);
        if (_state == 0)
        {
            _state = 1;
        }
    }

    /// <summary>The 32-bit FNV-1a hash of the UTF-8 bytes of <paramref name="text"/>.</summary>
    public static uint Fnv1a(string text)
    {
        var hash = 2_166_136_261u;
        foreach (var value in Encoding.UTF8.GetBytes(text))
        {
            hash ^= value;
            hash *= 16_777_619u;
        }

        return hash;
    }

    /// <summary>The next number of the xorshift32 sequence (13, 17, 5).</summary>
    public uint NextUInt()
    {
        var x = _state;
        x ^= x << 13;
        x ^= x >> 17;
        x ^= x << 5;
        _state = x;
        return x;
    }

    /// <summary>A number from 0 up to, not including, <paramref name="maxExclusive"/>.</summary>
    public int Next(int maxExclusive)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxExclusive, 0);
        return (int)(NextUInt() % (uint)maxExclusive);
    }
}
