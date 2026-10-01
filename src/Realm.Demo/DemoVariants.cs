namespace Realm.Demo;

/// <summary>
/// The Demo variants of 02 section 9.5, parsed in one place. Each of the nine names is a pure transform that switches one
/// aspect of the default fixture; they compose with commas and are applied left to right (see <see cref="Parse"/>). No
/// variant undoes another, so the result does not depend on the order. Unknown names are ignored.
/// </summary>
public sealed record DemoVariants
{
    // The one list of names: <see cref="Names"/> reads its keys, <see cref="Parse"/> applies its transforms.
    private static readonly Dictionary<string, Func<DemoVariants, DemoVariants>> Transforms = new(StringComparer.Ordinal)
    {
        ["all-sources"] = current => current with { AllSources = true },
        ["phone-unavailable"] = current => current with { PhoneUnavailable = true },
        ["life360-down"] = current => current with { Life360Down = true },
        ["ha-down"] = current => current with { HaDown = true },
        ["poor-accuracy"] = current => current with { PoorAccuracy = true },
        ["no-fix"] = current => current with { NoFix = true },
        ["all-near"] = current => current with { AllNear = true },
        ["empty-week"] = current => current with { EmptyWeek = true },
        ["fresh-install"] = current => current with { FreshInstall = true },
    };

    /// <summary>The default fixture: no variant.</summary>
    public static DemoVariants None { get; } = new();

    /// <summary>The nine variant names of 02 section 9.5.</summary>
    public static IReadOnlyList<string> Names { get; } = [.. Transforms.Keys];

    /// <summary>Every driver is phone-capable and every event type has a count in every week (the base data of 02 section 9.4).</summary>
    public bool AllSources { get; init; }

    /// <summary>Phone use is null for every driver, the phone-capable king included.</summary>
    public bool PhoneUnavailable { get; init; }

    /// <summary>The Life360 trackers are unavailable: all four event types are null and members lose their address, driving flag and speed.</summary>
    public bool Life360Down { get; init; }

    /// <summary>Home Assistant is unavailable and the session clock runs in real time, so members go stale.</summary>
    public bool HaDown { get; init; }

    /// <summary>The jester's position accuracy is 800 m.</summary>
    public bool PoorAccuracy { get; init; }

    /// <summary>The cryptid has never reported a position.</summary>
    public bool NoFix { get; init; }

    /// <summary>The cryptid and the prince are placed within 5 km of the king.</summary>
    public bool AllNear { get; init; }

    /// <summary>This week has no drives.</summary>
    public bool EmptyWeek { get; init; }

    /// <summary>Recording began on Wed 2026-09-23 12:00: partial last week, no earlier weeks, no trends.</summary>
    public bool FreshInstall { get; init; }

    /// <summary>
    /// The variants named by <paramref name="names"/>, applied left to right. A name may itself hold a comma-separated list
    /// (the caller normally splits it already). Surrounding spaces are ignored; a name that is not one of the nine is ignored.
    /// </summary>
    public static DemoVariants Parse(IEnumerable<string>? names)
    {
        var result = None;
        foreach (var entry in names ?? [])
        {
            foreach (var name in entry.Split(','))
            {
                if (Transforms.TryGetValue(name.Trim(), out var apply))
                {
                    result = apply(result);
                }
            }
        }

        return result;
    }
}
