namespace Realm.Web.State;

/// <summary>
/// What <see cref="HistorySync"/> needs from the browser: the end of the depth tokens that <c>realmShell.js</c> implements (<c>history.setDepth</c>, 03 sections 3.7 and 4.8). The
/// production port is <see cref="HistoryInterop"/>; a test fakes only this, so the sync itself runs as the product runs.
/// </summary>
public interface IHistoryPort : IAsyncDisposable
{
    /// <summary>
    /// Makes the history hold <paramref name="depth"/> entries above the page's base entry: pushes the missing <c>#r&lt;n&gt;</c> entries, or goes back by the surplus without
    /// reporting that as a user Back. Idempotent, and completes once the browser has settled (the <c>popstate</c> of a <c>history.go</c> has fired). With the history tokens
    /// switched off it only records the depth and touches nothing.
    /// </summary>
    ValueTask SetDepthAsync(int depth);
}
