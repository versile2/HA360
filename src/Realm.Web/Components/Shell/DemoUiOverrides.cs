namespace Realm.Web.Components.Shell;

/// <summary>
/// The Appendix-B URL parameters that are UI state, not data (01 Appendix B, 03 section 2.1, 04 G-15), read once per circuit by
/// <see cref="RealmShell"/> and cascaded beside the session. Each value is the id as written in the URL, already checked against
/// its value set; a parameter that was absent, invalid, or not allowed in this circuit is <c>null</c>. S5, S8 and S10 turn them into
/// the initial <c>RealmUiState</c> and <c>DevicePrefs</c>; nothing here reaches the data layer.
/// </summary>
/// <param name="Style">The initial map style: <c>night</c>, <c>day</c>, <c>streets</c>, <c>satellite</c> or the hidden <c>demo-offline</c>. Demo sessions only.</param>
/// <param name="Sheet">The initial sheet state: <c>peek</c> or <c>80</c> (D42). Demo sessions only.</param>
/// <param name="Layout">The layout override: <c>auto</c>, <c>sheet</c> or <c>panel</c>. Demo sessions only.</param>
/// <param name="Week">The Driving week offset, 0 to 3. An ordinary route query, honoured in every mode (R2-008).</param>
public sealed record DemoUiOverrides(string? Style = null, string? Sheet = null, string? Layout = null, int? Week = null)
{
    /// <summary>No parameter was given, or none applies.</summary>
    public static DemoUiOverrides None { get; } = new();
}
