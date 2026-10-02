using Realm.Domain;

namespace Realm.Infrastructure.Data;

/// <summary>The text of <see cref="FixSource"/> in the <c>fixes.source</c> column (02 section 7.2): <c>life360</c> or <c>companion</c>. Vehicle readings never reach that table.</summary>
internal static class FixSourceText
{
    public static string ToText(FixSource source)
    {
        return source switch
        {
            FixSource.Life360 => "life360",
            FixSource.Companion => "companion",
            _ => throw new ArgumentOutOfRangeException(nameof(source), "Only life360 and companion fixes are stored as fixes"),
        };
    }

    public static FixSource Parse(string text)
    {
        return text switch
        {
            "life360" => FixSource.Life360,
            "companion" => FixSource.Companion,
            _ => throw new InvalidOperationException("The fixes table holds an unknown source token"),
        };
    }
}
