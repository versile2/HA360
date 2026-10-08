namespace Realm.Infrastructure.Options;

/// <summary>
/// One row of the binding table of 02 section 3.4: a flat add-on option key, the .NET configuration path it is bound to, and its default.
/// </summary>
/// <param name="Key">The flat snake_case key of <c>options.json</c> and <c>config.yaml</c>.</param>
/// <param name="Path">The configuration path, with <c>:</c> separators.</param>
/// <param name="Kind">What the option holds.</param>
/// <param name="Default">The default as invariant text, as the option is written (before <paramref name="Factor"/>).</param>
/// <param name="Factor">The unit conversion applied to the value on its way to <paramref name="Path"/> (miles per hour to m/s, miles to metres); null for none.</param>
/// <param name="Optional">True for a key the Supervisor leaves out of <c>options.json</c> when the owner set no value (R-068): absent reads as the default.</param>
public sealed record OptionBinding(string Key, string Path, OptionKind Kind, string Default, double? Factor = null, bool Optional = false);
