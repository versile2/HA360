using Realm.Web.Sheet;

namespace Realm.Web.Components.Sheet;

/// <summary>The one place that maps a row's detail tone to the CSS modifier of <c>realm-sheet.css</c>, shared by the member and vehicle rows.</summary>
internal static class RowClasses
{
    /// <summary>The modifier (with its leading space) for the detail line: the warning colour (stale), the stale colour (offline), or none.</summary>
    public static string Tone(LineTone tone) =>
        tone switch
        {
            LineTone.Warning => " realm-row-detail--warning",
            LineTone.Stale => " realm-row-detail--stale",
            _ => string.Empty,
        };
}
