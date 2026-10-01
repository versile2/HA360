using Realm.Domain;

namespace Realm.Demo;

/// <summary>The four event counts of a driver, a week or a drive. Null means not recorded; 0 is a real zero.</summary>
internal sealed record DemoEventCounts(int? Speeding, int? Phone, int? Accel, int? Braking)
{
    /// <summary>The four keys in report order.</summary>
    public static readonly IReadOnlyList<string> Keys = [EventKeys.Speeding, EventKeys.Phone, EventKeys.Accel, EventKeys.Braking];

    /// <summary>No count of any kind.</summary>
    public static readonly DemoEventCounts NotRecorded = new(null, null, null, null);

    public int? Get(string key) => key switch
    {
        EventKeys.Speeding => Speeding,
        EventKeys.Phone => Phone,
        EventKeys.Accel => Accel,
        EventKeys.Braking => Braking,
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Not one of the four event keys."),
    };

    /// <summary>The same shape with <paramref name="transform"/> applied to each count.</summary>
    public DemoEventCounts Map(Func<int?, int?> transform) => new(transform(Speeding), transform(Phone), transform(Accel), transform(Braking));

    /// <summary>The counts keyed as the view models carry them.</summary>
    public IReadOnlyDictionary<string, int?> ToDictionary() => new Dictionary<string, int?>
    {
        [EventKeys.Speeding] = Speeding,
        [EventKeys.Phone] = Phone,
        [EventKeys.Accel] = Accel,
        [EventKeys.Braking] = Braking,
    };
}
