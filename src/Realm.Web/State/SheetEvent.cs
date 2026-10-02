using Realm.Domain;
using Realm.Web.Layout;

namespace Realm.Web.State;

/// <summary>
/// The input of <see cref="SheetStateMachine.Reduce"/> (03 section 3.6, 01 section 5.7): one case per thing a person can do to the sheet or the selection.
/// There is no <c>HeaderTap</c>: a tap on the Peek selection header body is <see cref="HandleToggle"/> (D46; the ✕ raises <see cref="ClearTap"/> only).
/// </summary>
public abstract record SheetEvent
{
    // Closed hierarchy: only the cases below derive from it.
    private SheetEvent()
    {
    }

    /// <summary>A list row was tapped (at either size): select it, at Peek.</summary>
    /// <param name="Entity">The row's member, vehicle or place.</param>
    public sealed record RowTap(EntityRef Entity) : SheetEvent;

    /// <summary>A member pin, a vehicle pin or a zone (a place) was tapped: select it, at Peek; the entity that is already selected changes nothing.</summary>
    /// <param name="Entity">The pin's member or vehicle, or the zone's place.</param>
    public sealed record PinTap(EntityRef Entity) : SheetEvent;

    /// <summary>An edge bubble was tapped. One member id is a selection exactly like <see cref="PinTap"/>; two or more (a cluster) change nothing, the camera fit is JavaScript's.</summary>
    /// <param name="Ids">The ids of the members in the bubble.</param>
    public sealed record BubbleTap(IReadOnlyList<string> Ids) : SheetEvent;

    /// <summary>A "Here now" row of a place detail was tapped: select that member (replacing the place), at Peek.</summary>
    /// <param name="MemberId">The occupant's member id.</param>
    public sealed record HereNowTap(string MemberId) : SheetEvent;

    /// <summary>A segment of the segmented control was tapped: that section's list, nothing selected, the size unchanged.</summary>
    /// <param name="Section">The tab.</param>
    public sealed record SegmentTap(Section Section) : SheetEvent;

    /// <summary>The ✕ of the Peek header or of the floating pill: nothing selected, Peek. Never also a toggle.</summary>
    public sealed record ClearTap : SheetEvent;

    /// <summary>The empty map was tapped.</summary>
    public sealed record MapTap : SheetEvent;

    /// <summary>
    /// The size toggles: a handle tap or key (Enter, Space) and, with a selection, a tap on the Peek header body (D46): the same event with two raisers.
    /// Section and selection are kept, so the body swaps header and detail, or list and list.
    /// </summary>
    public sealed record HandleToggle : SheetEvent;

    /// <summary>The size is set: the handle's arrow, Home and End keys and the <c>OnSizeChanged</c> echo of the sheet host. The current size changes nothing.</summary>
    /// <param name="Size">Peek or Tall.</param>
    public sealed record HandleSet(SheetSize Size) : SheetEvent;

    /// <summary>The global Esc key, once no overlay is left to close: Back steps 2 and 3 only (<see cref="BackReducer.Escape"/>).</summary>
    public sealed record Escape : SheetEvent;

    /// <summary>The bottom navigation's Location tab was tapped while already on Location (01 section 2.2): nothing selected, Peek.</summary>
    public sealed record NavReTap : SheetEvent;
}
