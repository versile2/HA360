using Realm.Domain;

namespace Realm.Web.State;

/// <summary>
/// The one path every tap on the Location screen takes into the state (03 section 3.6, 01 section 4.13): a row, a member or vehicle pin, a zone, a bubble, the empty map, the handle, the
/// header, a segment or the nav re-tap each become one <see cref="SheetEvent"/>, <see cref="RealmUiState.Apply"/> runs it through the reducer, and the Follow mirror and the focus rule
/// are settled here, so no tap has a path of its own. The page only turns what a component raised into the event; what the event does is the reducer's and what follows from it is this
/// class's, which is why both can be tested without a renderer.
/// </summary>
public static class SheetDispatch
{
    /// <summary>
    /// Applies <paramref name="sheetEvent"/> to <paramref name="ui"/>, keeps <see cref="RealmUiState.FollowMemberId"/> in line with what the map does with the selection (01 section 4.14),
    /// and tells whether the keyboard focus should move to the detail's back button (01 section 10.2).
    /// </summary>
    /// <param name="ui">The circuit's UI state.</param>
    /// <param name="sheetEvent">What the person did.</param>
    /// <param name="members">The people now on the map; Follow is only mirrored for one who is driving with a fresh fix.</param>
    /// <returns>True only when the person just opened the detail with the handle or the header; a detail that appears because a pin was tapped in the panel leaves the focus where it is.</returns>
    public static bool Apply(RealmUiState ui, SheetEvent sheetEvent, IReadOnlyList<MemberVm> members)
    {
        ArgumentNullException.ThrowIfNull(ui);
        ArgumentNullException.ThrowIfNull(sheetEvent);
        ArgumentNullException.ThrowIfNull(members);

        var before = ui.Body;
        ui.Apply(sheetEvent);
        var focusDetail = sheetEvent is (SheetEvent.HandleToggle or SheetEvent.HandleSet) && before is not SheetBody.Detail && ui.Body is SheetBody.Detail;
        MirrorFollow(ui, sheetEvent, members);
        return focusDetail;
    }

    // FollowMemberId mirrors what the script does: a driving member selected by a tap (a row, a pin, a single bubble or a Here-now row) is followed, and the selection going away ends
    // it. The script tells when it ends Follow by itself (OnFollowEnded). MapView asks for Follow with every member selection and the script honours it for a driving member only. A tap
    // on the member that is already selected runs the flight again with Follow (D89), so it is mirrored again too.
    private static void MirrorFollow(RealmUiState ui, SheetEvent sheetEvent, IReadOnlyList<MemberVm> members)
    {
        if (ui.Selection is not { } selected)
        {
            ui.FollowMemberId = null;
            return;
        }

        if (sheetEvent is SheetEvent.RowTap or SheetEvent.PinTap or SheetEvent.HereNowTap or SheetEvent.BubbleTap { Ids.Count: 1 })
        {
            ui.FollowMemberId = selected.Kind == EntityKind.Member && members.Any(member => member.Id == selected.Id && member.IsDriving && member.Freshness == Freshness.Fresh)
                ? selected.Id
                : null;
        }
    }
}
