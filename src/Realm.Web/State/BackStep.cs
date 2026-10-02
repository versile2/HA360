namespace Realm.Web.State;

/// <summary>
/// Which step of the 01 section 5.7 Back chain a Back (the Android Back, the detail's back arrow or Esc) took (03 section 3.6); the history sync and the
/// focus logic read it. Every step except <see cref="Leave"/> lowers the history depth by exactly 1 (03 section 3.7).
/// </summary>
public enum BackStep
{
    /// <summary>Step 1: an overlay (popover, dialog, Settings) was open and closes. The sheet state is unchanged.</summary>
    Overlay,

    /// <summary>Step 2: the detail at Tall goes to the header at Peek, the selection kept.</summary>
    Detail,

    /// <summary>Step 3: the selection is cleared (Compact: the header becomes the list at Peek; Expanded: the detail becomes the list).</summary>
    Selection,

    /// <summary>Step 4: the list at Tall collapses to Peek (Compact only).</summary>
    List,

    /// <summary>Step 5: nothing is left to close; the state is unchanged and the browser leaves.</summary>
    Leave,
}
