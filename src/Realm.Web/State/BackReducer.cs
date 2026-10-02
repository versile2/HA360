using Realm.Web.Layout;

namespace Realm.Web.State;

/// <summary>
/// The 01 section 5.7 Back chain as a pure function (03 section 3.6): the same function serves the Android Back, the in-sheet back arrow of the detail and
/// Esc. Compact: overlay, then detail at Tall to Peek (selection kept), then selection cleared, then list at Tall to Peek, then leave. Expanded has no size
/// steps: overlay, then selection cleared, then leave. <see cref="LayoutMode.Unknown"/> counts as Compact.
/// </summary>
public static class BackReducer
{
    /// <summary>One Back. The step it took is returned with the new state; <see cref="BackStep.Leave"/> returns the state unchanged.</summary>
    /// <param name="state">The current sheet state.</param>
    /// <param name="mode">The current layout.</param>
    /// <param name="overlayCount">
    /// How many overlays are open. Above zero the step is <see cref="BackStep.Overlay"/> and the sheet state is returned unchanged; closing the topmost overlay
    /// is the overlay stack's job (03 section 3.7), the reducer only reports that it comes first.
    /// </param>
    public static (SheetState State, BackStep Step) Reduce(SheetState state, LayoutMode mode, int overlayCount = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(overlayCount);
        if (overlayCount > 0)
        {
            return (state, BackStep.Overlay);
        }

        if (mode == LayoutMode.Expanded)
        {
            return state.Selection is not null ? (state with { Selection = null }, BackStep.Selection) : (state, BackStep.Leave);
        }

        return (state.Selection is not null, state.Size) switch
        {
            (true, SheetSize.Tall) => (state with { Size = SheetSize.Peek }, BackStep.Detail),
            (true, _) => (state with { Selection = null }, BackStep.Selection),
            (false, SheetSize.Tall) => (state with { Size = SheetSize.Peek }, BackStep.List),
            _ => (state, BackStep.Leave),
        };
    }

    /// <summary>
    /// Esc once no overlay is left to close: Back steps 2 and 3 only (in Expanded the selection step). It never collapses a list and never leaves, so with no
    /// selection the step is <c>null</c> and the state is unchanged.
    /// </summary>
    /// <param name="state">The current sheet state.</param>
    /// <param name="mode">The current layout.</param>
    public static (SheetState State, BackStep? Step) Escape(SheetState state, LayoutMode mode)
    {
        if (state.Selection is null)
        {
            return (state, null);
        }

        var (next, step) = Reduce(state, mode);
        return (next, step);
    }
}
