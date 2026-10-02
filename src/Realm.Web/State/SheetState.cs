using Realm.Domain;
using Realm.Web.Layout;

namespace Realm.Web.State;

/// <summary>
/// The one small record the sheet's reducers work on (03 section 3.6, D45): the section, the selected entity (null: nothing selected) and the size
/// of the Compact sheet. What the sheet shows is derived from it by <see cref="SheetBody.BodyOf"/> and never stored. Four Compact states are
/// reachable per section: <c>(none, Peek)</c> and <c>(none, Tall)</c> (both the list), <c>(entity, Peek)</c> (the summary header) and
/// <c>(entity, Tall)</c> (the detail). The size is the S7a enum <see cref="SheetSize"/> (D42: exactly Peek and Tall).
/// </summary>
/// <param name="Section">The segmented control's tab; only <see cref="SheetEvent.SegmentTap"/> changes it.</param>
/// <param name="Selection">A member, a vehicle or a place, by id so that it survives data updates; null when nothing is selected.</param>
/// <param name="Size">The Compact sheet's size. The Expanded panel has no size and ignores it.</param>
public readonly record struct SheetState(Section Section, EntityRef? Selection, SheetSize Size)
{
    /// <summary>App opens: Drivers, nothing selected, Peek (01 section 5.7).</summary>
    public static SheetState Initial { get; } = new(Section.Drivers, null, SheetSize.Peek);

    /// <summary>
    /// The history depth of 03 section 3.7: the open overlays, plus one for a selection, plus one when the Compact size is not Peek (the size term is 0
    /// in Expanded). So <c>(none, Peek)</c> is 0, <c>(none, Tall)</c> and <c>(entity, Peek)</c> are 1, <c>(entity, Tall)</c> is 2; every Back step lowers it by
    /// exactly 1 and a row tapped at Tall keeps it at 1.
    /// </summary>
    /// <param name="mode">The layout; <see cref="LayoutMode.Unknown"/> counts as Compact.</param>
    /// <param name="overlayCount">How many overlays (popover, dialog, Settings) are open, each one history layer.</param>
    public int Depth(LayoutMode mode, int overlayCount = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(overlayCount);
        return overlayCount + (Selection is not null ? 1 : 0) + (mode != LayoutMode.Expanded && Size != SheetSize.Peek ? 1 : 0);
    }
}
