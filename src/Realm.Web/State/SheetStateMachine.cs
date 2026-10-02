using Realm.Domain;
using Realm.Web.Layout;

namespace Realm.Web.State;

/// <summary>
/// The sheet's state transitions as one pure function (03 section 3.6, 01 section 5.7), with no Blazor and no JavaScript in it. The component layer calls
/// it and then calls the history sync (03 section 3.7). Every selection event leaves with <see cref="SheetSize.Peek"/> (D45); the sheet reaches Tall only
/// through <see cref="SheetEvent.HandleToggle"/> or <see cref="SheetEvent.HandleSet"/>, which the handle and the Peek header both raise (D46). The
/// Expanded panel has no size, so the two handle events change nothing there; <see cref="LayoutMode.Unknown"/> counts as Compact.
/// </summary>
public static class SheetStateMachine
{
    /// <summary>The state after the event. The result of an event that has no effect is the very same state value.</summary>
    /// <param name="state">The current sheet state.</param>
    /// <param name="sheetEvent">What happened.</param>
    /// <param name="mode">The current layout.</param>
    public static SheetState Reduce(SheetState state, SheetEvent sheetEvent, LayoutMode mode)
    {
        ArgumentNullException.ThrowIfNull(sheetEvent);
        var expanded = mode == LayoutMode.Expanded;
        return sheetEvent switch
        {
            SheetEvent.RowTap row => Select(state, row.Entity),
            SheetEvent.PinTap pin => SelectUnlessSelected(state, pin.Entity),
            SheetEvent.BubbleTap { Ids.Count: 1 } bubble => SelectUnlessSelected(state, new EntityRef(EntityKind.Member, bubble.Ids[0])),
            SheetEvent.BubbleTap => state,
            SheetEvent.HereNowTap here => Select(state, new EntityRef(EntityKind.Member, here.MemberId)),
            SheetEvent.SegmentTap segment => state with { Section = segment.Section, Selection = null },
            SheetEvent.ClearTap or SheetEvent.NavReTap => state with { Selection = null, Size = SheetSize.Peek },
            SheetEvent.MapTap => MapTap(state, expanded),
            SheetEvent.HandleToggle => expanded ? state : state with { Size = state.Size == SheetSize.Peek ? SheetSize.Tall : SheetSize.Peek },
            SheetEvent.HandleSet set => expanded || !Enum.IsDefined(set.Size) ? state : state with { Size = set.Size },
            SheetEvent.Escape => BackReducer.Escape(state, mode).State,
            _ => throw new ArgumentOutOfRangeException(nameof(sheetEvent), sheetEvent, "Unknown sheet event."),
        };
    }

    // D45: every selection path leaves at Peek, in both layouts (so folding back from the panel returns to the header).
    private static SheetState Select(SheetState state, EntityRef entity) =>
        state with { Selection = entity, Size = SheetSize.Peek };

    // 01 section 4.13 "selected pin again": nothing expands and nothing collapses.
    private static SheetState SelectUnlessSelected(SheetState state, EntityRef entity) =>
        state.Selection == entity ? state : Select(state, entity);

    private static SheetState MapTap(SheetState state, bool expanded)
    {
        if (expanded)
        {
            return state with { Selection = null };
        }

        return state.Size == SheetSize.Tall ? state with { Size = SheetSize.Peek }
            : state.Selection is not null ? state with { Selection = null }
            : state;
    }
}
