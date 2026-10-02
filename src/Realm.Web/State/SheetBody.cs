using Realm.Web.Layout;

namespace Realm.Web.State;

/// <summary>
/// What <c>SheetContent</c> renders (03 section 3.4): a pure function of <see cref="SheetState"/> and the layout, never stored (D45). Nothing selected is the
/// <see cref="List"/> at either size; a selection at Peek is the summary <see cref="Header"/>; a selection at Tall, or in the Expanded panel, is the
/// <see cref="Detail"/>. A tap on the Peek header and a tap on the handle are the same event (<see cref="SheetEvent.HandleToggle"/>, D46) and swap the two.
/// </summary>
public abstract record SheetBody
{
    // Closed hierarchy: only the three cases below derive from it.
    private SheetBody()
    {
    }

    /// <summary>Nothing is selected: the segmented control and the list of the section, at either size.</summary>
    public sealed record List : SheetBody;

    /// <summary>The entity is selected and the Compact sheet is at Peek: the selection header (01 section 3.4.2).</summary>
    /// <param name="Entity">The selected member, vehicle or place.</param>
    public sealed record Header(Realm.Domain.EntityRef Entity) : SheetBody;

    /// <summary>The entity is selected and the sheet is at Tall, or the Expanded panel is showing: the detail view (01 sections 5.4 to 5.6).</summary>
    /// <param name="Entity">The selected member, vehicle or place.</param>
    public sealed record Detail(Realm.Domain.EntityRef Entity) : SheetBody;

    /// <summary>
    /// <c>List</c> iff there is no selection; <c>Detail</c> iff there is one and the size is Tall or the layout is Expanded; otherwise <c>Header</c>.
    /// <see cref="LayoutMode.Unknown"/> counts as Compact.
    /// </summary>
    public static SheetBody BodyOf(SheetState state, LayoutMode mode) =>
        state.Selection is not { } entity ? new List()
        : mode == LayoutMode.Expanded || state.Size == SheetSize.Tall ? new Detail(entity)
        : new Header(entity);
}
