using Realm.Domain;
using Realm.Web.State;

namespace Realm.Web.Components.Sheet;

/// <summary>
/// Where a focus move of 01 section 10.2 goes. The detail's own back button is not a case: whether a detail that has just opened takes the focus is the bool <see cref="SheetDispatch.Apply"/>
/// returns, which reaches the detail as <c>FocusOnOpen</c>.
/// </summary>
public enum FocusTarget
{
    /// <summary>Nothing moves: a pin or a bubble selects "from where the focus is", and most events never took it anywhere.</summary>
    None,

    /// <summary>The sheet handle: the control that opens the detail, and the one that is still there when the row or the detail the focus was on has gone.</summary>
    Handle,

    /// <summary>The section tab that shows (the roving tab stop of the tablist), where the focus goes once the selection that replaced the tabs with its header is cleared.</summary>
    SectionTab,
}

/// <summary>
/// One request to move the focus, as the page makes it after an event or a Back step. It is a class so that a request is its own identity: the handle and the segments act on a request they have not
/// acted on yet, which a record (equal by value) could not tell apart from the last one of the same target.
/// </summary>
/// <param name="target">Where the focus goes.</param>
public sealed class FocusRequest(FocusTarget target)
{
    /// <summary>Where the focus goes.</summary>
    public FocusTarget Target { get; } = target;
}

/// <summary>
/// The focus flow of D45 (01 section 10.2), as pure functions of what happened. A row tap collapses the sheet to Peek and hides that row, so the focus goes to the handle; Back from a detail
/// goes there as well; clearing the selection (the X, Back or the empty map) goes to the section tab. A pin or a bubble selects where the focus is, and the detail's back button takes the focus
/// from the handle or the header through <see cref="SheetDispatch.Apply"/>. A target that is not on the page (there is no handle in the Expanded panel) is simply not focused.
/// </summary>
public static class SheetFocus
{
    /// <summary>
    /// The focus after a sheet event: a row, and a person in a place's Here-now list (a row that goes with the detail it was in), take it to the handle; the X and the empty map take it to the
    /// section tab when they cleared a selection; everything else leaves it.
    /// </summary>
    /// <param name="sheetEvent">What the person did.</param>
    /// <param name="before">The selection before the event.</param>
    /// <param name="after">The selection after it.</param>
    public static FocusTarget After(SheetEvent sheetEvent, EntityRef? before, EntityRef? after)
    {
        ArgumentNullException.ThrowIfNull(sheetEvent);
        return sheetEvent switch
        {
            SheetEvent.RowTap or SheetEvent.HereNowTap => FocusTarget.Handle,
            SheetEvent.ClearTap or SheetEvent.MapTap when before is not null && after is null => FocusTarget.SectionTab,
            _ => FocusTarget.None,
        };
    }

    /// <summary>
    /// The focus after a Back step (the Android Back, the detail's back arrow or Esc): the detail's close and the list's collapse leave the focus on the handle, clearing the selection puts it on
    /// the section tab, and an overlay that closes restores the focus to its opener by itself.
    /// </summary>
    /// <param name="step">The step <c>HistorySync.BackTaken</c> reported.</param>
    public static FocusTarget After(BackStep step) =>
        step switch
        {
            BackStep.Detail or BackStep.List => FocusTarget.Handle,
            BackStep.Selection => FocusTarget.SectionTab,
            _ => FocusTarget.None,
        };
}
