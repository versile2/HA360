using System.Globalization;
using System.Text;

namespace Realm.Web.Formatting;

/// <summary>
/// The place strings of 01 sections 5.3, 8.5 and 10.3. A place is occupied by <b>people</b> only (R2-009): a parked vehicle adds nothing to "1 here". Pure.
/// </summary>
public static class PlaceTextFormatter
{
    /// <summary>The trailing text of a place nobody is in.</summary>
    public const string Empty = "Empty";

    /// <summary>"1 here", "2 here", or <see cref="Empty"/> for nobody.</summary>
    public static string Count(int people) => people <= 0 ? Empty : people.ToString(CultureInfo.InvariantCulture) + " here";

    /// <summary>"Hearth Haven, Home. 1 here: Alden." or "Work, The Counting House. Empty." (the subtitle is left out when there is none; every occupant is named).</summary>
    public static string AccessibleName(string name, string subtitle, int people, IReadOnlyList<string> occupantNames)
    {
        var text = new StringBuilder(string.IsNullOrWhiteSpace(subtitle) ? name : name + ", " + subtitle).Append(". ").Append(Count(people));
        if (people > 0 && occupantNames.Count > 0)
        {
            text.Append(": ").Append(string.Join(", ", occupantNames));
        }

        return text.Append('.').ToString();
    }
}
