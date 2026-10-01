namespace Realm.Infrastructure.Options;

/// <summary>
/// The outcome of the cross-field checks of 02 section 3.3. An error means the data service must not start; a warning is logged and start goes on.
/// A message names the offending key (with the list index where there is one), never its value (10.8).
/// </summary>
public sealed record OptionsValidation(IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings)
{
    public bool IsValid => Errors.Count == 0;
}
