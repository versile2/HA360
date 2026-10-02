using Microsoft.Extensions.Configuration;

namespace Realm.Web.State;

/// <summary>The settings of <see cref="HistorySync"/>.</summary>
/// <param name="Tokens">
/// <c>Realm:Ui:HistoryTokens</c>, the kill switch of the Back-gesture depth tokens (03 section 3.7, D47; the add-on option <c>ui_history_tokens</c>, default on). Off, the browser
/// history is never touched: the Android Back leaves the app, while Esc and the in-app Back arrow still run the same reducers.
/// </param>
public sealed record HistoryOptions(bool Tokens = true)
{
    /// <summary>The configuration key of the switch.</summary>
    public const string TokensKey = "Realm:Ui:HistoryTokens";

    /// <summary>Reads the switch: on unless the value is a false boolean (absent, empty or unreadable all mean on).</summary>
    public static HistoryOptions From(IConfiguration? configuration) =>
        new(configuration is null || !bool.TryParse(configuration[TokensKey], out var on) || on);
}
