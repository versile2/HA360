using Realm.Domain;
using Realm.Web.Layout;
using Realm.Web.Map;

namespace Realm.Web.State;

/// <summary>The kinds of overlay that are one history layer each (03 section 3.6).</summary>
public enum OverlayKind
{
    /// <summary>A popover such as the map style picker.</summary>
    Popover,

    /// <summary>A dialog.</summary>
    Dialog,

    /// <summary>The Settings dialog.</summary>
    Settings,
}

/// <summary>One open overlay: a layer of the Overlays stack and of the history depth (03 section 3.7).</summary>
/// <param name="Kind">Popover, dialog or Settings.</param>
/// <param name="Id">Tells two overlays of one kind apart; null when there is only one.</param>
public readonly record struct OverlayLayer(OverlayKind Kind, string? Id = null);

/// <summary>The layout facts of the circuit (03 section 3.6): what <see cref="LayoutResolver"/> resolved plus the viewport, the panel and the safe area.</summary>
/// <param name="Mode">Compact or Expanded; Unknown until the viewport is reported.</param>
/// <param name="PanelHidden">Expanded only: the side panel is folded away.</param>
/// <param name="ViewportWidth">The viewport width in CSS pixels; 0 while unknown.</param>
/// <param name="ViewportHeight">The viewport height in CSS pixels; 0 while unknown.</param>
/// <param name="Safe">Safe-area insets in CSS pixels.</param>
public sealed record LayoutSnapshot(LayoutMode Mode, bool PanelHidden, double ViewportWidth, double ViewportHeight, Padding Safe)
{
    /// <summary>Before <c>ShellInterop</c> reports the viewport.</summary>
    public static LayoutSnapshot Unknown { get; } = new(LayoutMode.Unknown, PanelHidden: false, 0, 0, new Padding(0, 0, 0, 0));

    /// <summary>The panel's width in Expanded (03 section 4.5).</summary>
    public double PanelWidthPx => LayoutResolver.PanelWidthPx;
}

/// <summary>
/// The per-circuit UI state (03 section 3.6; register it scoped): the section, the selection and the Compact sheet size live in one <see cref="SheetState"/>
/// that only <see cref="SheetStateMachine"/> and <see cref="BackReducer"/> change (D45), so what the sheet shows (<see cref="Body"/>) and the history depth
/// (<see cref="Depth"/>) are derived and cannot disagree with it. It has no persistence of its own. <see cref="Changed"/> fires once after anything here changed.
/// </summary>
public sealed class RealmUiState
{
    private readonly List<OverlayLayer> _overlays = [];
    private SheetState _sheet = SheetState.Initial;
    private LayoutSnapshot _layout = LayoutSnapshot.Unknown;
    private int _selectedWeek;

    /// <summary>Raised after a change of any property or of the overlay stack.</summary>
    public event Action? Changed;

    /// <summary>The section, the selection and the size as the one record the reducers work on.</summary>
    public SheetState Sheet => _sheet;

    /// <summary>The sheet's tab; default Drivers.</summary>
    public Section Section => _sheet.Section;

    /// <summary>The selected member, vehicle or place (by id, so it survives data updates); null when nothing is selected.</summary>
    public EntityRef? Selection => _sheet.Selection;

    /// <summary>Peek or Tall (D42); the Expanded panel ignores it.</summary>
    public SheetSize SheetSize => _sheet.Size;

    /// <summary>What the sheet shows now: a pure function of <see cref="Sheet"/> and the layout (D45).</summary>
    public SheetBody Body => SheetBody.BodyOf(_sheet, _layout.Mode);

    /// <summary>The history depth of 03 section 3.7: overlays, plus the selection, plus a Compact size other than Peek.</summary>
    public int Depth => _sheet.Depth(_layout.Mode, _overlays.Count);

    /// <summary>The open overlays, bottom first; each one is a history layer.</summary>
    public IReadOnlyList<OverlayLayer> Overlays => _overlays;

    /// <summary>The member the camera follows (01 section 4.14); set when a driving member is selected by a tap, null otherwise.</summary>
    public string? FollowMemberId
    {
        get;
        set => Set(ref field, value);
    }

    /// <summary>The Driving week, 0 (this week) to 3.</summary>
    public int SelectedWeek
    {
        get => _selectedWeek;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 3);
            Set(ref _selectedWeek, value);
        }
    }

    /// <summary>The layout; changing it never changes the selection (AC-12), only what <see cref="Body"/> and <see cref="Depth"/> derive.</summary>
    public LayoutSnapshot Layout
    {
        get => _layout;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            Set(ref _layout, value);
        }
    }

    /// <summary>The last camera JavaScript reported (it mirrors it to <c>sessionStorage["realm.camera"]</c> itself); null before the first report.</summary>
    public CameraState? LastCamera
    {
        get;
        set => Set(ref field, value);
    }

    /// <summary>The viewer's member id, resolved in SSR (03 section 5.5); null when unknown.</summary>
    public string? MeId
    {
        get;
        set => Set(ref field, value);
    }

    /// <summary>Runs the event through <see cref="SheetStateMachine"/>; true when the state changed.</summary>
    public bool Apply(SheetEvent sheetEvent)
    {
        var next = SheetStateMachine.Reduce(_sheet, sheetEvent, _layout.Mode);
        return Set(ref _sheet, next);
    }

    /// <summary>One Back (the Android Back, the detail's back arrow): the topmost overlay closes if any, otherwise <see cref="BackReducer.Reduce"/> applies.</summary>
    public BackStep Back()
    {
        var (next, step) = BackReducer.Reduce(_sheet, _layout.Mode, _overlays.Count);
        if (step == BackStep.Overlay)
        {
            _overlays.RemoveAt(_overlays.Count - 1);
            Changed?.Invoke();
        }
        else
        {
            Set(ref _sheet, next);
        }

        return step;
    }

    /// <summary>Esc: the topmost overlay closes if any, otherwise Back steps 2 and 3 only (<see cref="BackReducer.Escape"/>); null when nothing changed.</summary>
    public BackStep? Escape()
    {
        if (_overlays.Count > 0)
        {
            return Back();
        }

        var (next, step) = BackReducer.Escape(_sheet, _layout.Mode);
        Set(ref _sheet, next);
        return step;
    }

    /// <summary>Opens an overlay: one more history layer.</summary>
    public void OpenOverlay(OverlayLayer layer)
    {
        _overlays.Add(layer);
        Changed?.Invoke();
    }

    /// <summary>Closes the topmost overlay of that layer's kind and id (an overlay closing by its own button); false when it is not open.</summary>
    public bool CloseOverlay(OverlayLayer layer)
    {
        var index = _overlays.LastIndexOf(layer);
        if (index < 0)
        {
            return false;
        }

        _overlays.RemoveAt(index);
        Changed?.Invoke();
        return true;
    }

    private bool Set<T>(ref T field, T value)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        Changed?.Invoke();
        return true;
    }
}
