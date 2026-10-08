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
    private readonly List<Action?> _closers = [];
    private SheetState _sheet = SheetState.Initial;
    private LayoutSnapshot _layout = LayoutSnapshot.Unknown;
    private int _selectedWeek;
    private DateTimeOffset _cameraAt;
    private bool _startupBegun;

    /// <summary>How long a camera stays worth restoring when the person comes back to Location (01 section 2.2): after more than this the default camera runs instead.</summary>
    public static TimeSpan CameraRestoreWindow { get; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Raised after a change of any property or of the overlay stack, except <see cref="RecordCamera"/>, which nothing renders. It is raised on the caller's context,
    /// so a subscriber that renders marshals with <c>InvokeAsync</c>.
    /// </summary>
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

    /// <summary>
    /// The long period the split button of the Driving report applies from its main part: Last month until the person chooses another (a month, a rolling window or a custom
    /// range). Kept for the circuit, not across a reload.
    /// </summary>
    public ReportPeriod LastLongPeriod
    {
        get;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            Set(ref field, value);
        }
    } = new(ReportPeriod.DefaultLong);

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

    /// <summary>
    /// The last camera the Location page kept: the one JavaScript reported (it reports only when the recentre state changes, <c>[X-07]</c>, and mirrors every settled camera to <c>sessionStorage["realm.camera"]</c>
    /// itself) or the one the page read from it as it went away (R1-12); null before either. Set by <see cref="RecordCamera"/>.
    /// </summary>
    public CameraState? LastCamera { get; private set; }

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

    /// <summary>
    /// One Back (the Android Back, the detail's back arrow): the topmost overlay closes if any, otherwise <see cref="BackReducer.Reduce"/> applies. A closing overlay
    /// leaves the stack first and <see cref="Changed"/> is raised; then the close action its owner registered with <see cref="OpenOverlay"/> runs, so the owner can
    /// take the dialog or popover down. The owner's later <see cref="CloseOverlay"/> then finds nothing to do.
    /// </summary>
    public BackStep Back()
    {
        var (next, step) = BackReducer.Reduce(_sheet, _layout.Mode, _overlays.Count);
        if (step == BackStep.Overlay)
        {
            var close = _closers[^1];
            _overlays.RemoveAt(_overlays.Count - 1);
            _closers.RemoveAt(_closers.Count - 1);
            Changed?.Invoke();
            close?.Invoke();
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
    /// <param name="layer">Which overlay it is.</param>
    /// <param name="close">
    /// What closes it from outside (the Android Back, Esc): called by <see cref="Back"/> and <see cref="Escape"/> after the layer left the stack, possibly off the
    /// renderer's context, so it marshals itself (<c>InvokeAsync</c>). Null for an overlay that only its own button closes.
    /// </param>
    public void OpenOverlay(OverlayLayer layer, Action? close = null)
    {
        _overlays.Add(layer);
        _closers.Add(close);
        Changed?.Invoke();
    }

    /// <summary>Closes the topmost overlay of that layer's kind and id (an overlay closing by its own button); false when it is not open. The close action is not called.</summary>
    public bool CloseOverlay(OverlayLayer layer)
    {
        var index = _overlays.LastIndexOf(layer);
        if (index < 0)
        {
            return false;
        }

        _overlays.RemoveAt(index);
        _closers.RemoveAt(index);
        Changed?.Invoke();
        return true;
    }

    /// <summary>
    /// Remembers a camera and when (the session's clock, never the wall clock): the one JavaScript just reported, or the one the page read as it went away. Raises nothing: no component renders the
    /// camera itself, and the page re-renders only for what it derives from it, the recentre state.
    /// </summary>
    public void RecordCamera(CameraState camera, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(camera);
        LastCamera = camera;
        _cameraAt = at;
    }

    /// <summary>
    /// The camera to restore when the Location page comes back (01 section 2.2, R1-12): the last one reported, unless more than <see cref="CameraRestoreWindow"/> has
    /// passed since, when the default camera fit runs instead; null before any report.
    /// </summary>
    /// <param name="now">The session's clock now.</param>
    public CameraState? CameraToRestore(DateTimeOffset now) =>
        LastCamera is { } camera && now - _cameraAt <= CameraRestoreWindow ? camera : null;

    /// <summary>
    /// True the first time it is called in this circuit and false afterwards: the page's start-up overrides (the Demo <c>sheet=80</c> parameter) apply once, not each time
    /// the page is created again after a trip to Driving, when the state they would override is the person's own.
    /// </summary>
    public bool TryBeginStartup()
    {
        if (_startupBegun)
        {
            return false;
        }

        _startupBegun = true;
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
