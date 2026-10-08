using System.Globalization;

namespace Realm.Web.Formatting;

/// <summary>
/// The paging rules of a driver's drive list (01 section 6.6): 25, 50 or 100 rows per page (25 by default), the pager at the bottom always, and at the top only when
/// the rows do not fit on one page. Printing ignores the pager (every row prints).
/// </summary>
public static class PagerMath
{
    /// <summary>The rows-per-page choices.</summary>
    public static IReadOnlyList<int> PageSizes { get; } = [25, 50, 100];

    /// <summary>The default rows per page.</summary>
    public const int DefaultPageSize = 25;

    /// <summary>The size if it is one of <see cref="PageSizes"/>, else the default.</summary>
    public static int NormalizeSize(int size) => PageSizes.Contains(size) ? size : DefaultPageSize;

    /// <summary>The number of pages; at least 1.</summary>
    public static int PageCount(int total, int size) => total <= 0 ? 1 : ((total - 1) / NormalizeSize(size)) + 1;

    /// <summary>The page index (from 0) held inside the existing pages.</summary>
    public static int ClampPage(int page, int total, int size) => Math.Clamp(page, 0, PageCount(total, size) - 1);

    /// <summary>True when the pager above the list is shown: the rows do not fit on one page.</summary>
    public static bool ShowTop(int total, int size) => total > NormalizeSize(size);

    /// <summary>True when the pager below the list is shown: there is at least one row.</summary>
    public static bool ShowBottom(int total) => total > 0;

    /// <summary>The first row (from 0) of the page.</summary>
    public static int Skip(int page, int total, int size) => ClampPage(page, total, size) * NormalizeSize(size);

    /// <summary>"26–50 of 120", "1–18 of 18", "0 of 0".</summary>
    public static string Summary(int total, int page, int size)
    {
        if (total <= 0)
        {
            return "0 of 0";
        }

        var skip = Skip(page, total, size);
        var last = Math.Min(total, skip + NormalizeSize(size));
        return (skip + 1).ToString(CultureInfo.InvariantCulture) + "–" + last.ToString(CultureInfo.InvariantCulture) + " of " + total.ToString(CultureInfo.InvariantCulture);
    }
}
