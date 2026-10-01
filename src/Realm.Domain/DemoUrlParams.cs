namespace Realm.Domain;

/// <summary>The Demo parameters the data layer needs: the clock override and the variant names, applied left to right.</summary>
public record DemoUrlParams(
    DateTimeOffset? Now,
    IReadOnlyList<string> Variants);
