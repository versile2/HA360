using Realm.Domain;

namespace Realm.Web.Components.Shell;

/// <summary>How long the circuit has waited for the first data snapshot (01 section 8.6, rows "First data slow" and "First data failed").</summary>
public enum FirstDataStage
{
    /// <summary>Under 8 s: the skeleton rows and "Summoning the court…" are all there is to say.</summary>
    Waiting,

    /// <summary>Over 8 s: the banner "Still summoning the court…" as well.</summary>
    Slow,

    /// <summary>Over 20 s: the error "The court didn't answer. Try again." with a Retry button, in the sheet body.</summary>
    Failed,
}

/// <summary>
/// The two first-data deadlines of one circuit (01 sections 3.8, 8.6 and 9 case 1), cascaded by <c>RealmShell</c> so the banner and the sheet read the same clock. It counts from
/// <see cref="Begin"/>, which the shell calls on the circuit's first render (never in prerender: a prerender pass is thrown away and a timer there would leak), on the session's
/// own <see cref="TimeProvider"/>, so a test moves it with a manual clock and Demo's frozen clock (whose timers run on the system's) never needs it: Demo has its data at once.
/// <para>
/// The watch knows only how long it has waited. Whether data has come is the snapshot's: <see cref="IsPending"/> is the same rule the summary line uses
/// (<c>HandleSummaryFormatter.Format</c>: no people, no vehicles and no places is the state before the first snapshot, because a configured Realm has at least one of them), and the
/// consumers show the pending states only while it holds, whatever the stage says. The stage never moves back: data arriving ends the states, it does not reset the clock.
/// A refused configuration (02 section 10.8) is not a separate signal; the data service does not start, the snapshot stays empty, and the clock runs out.
/// </para>
/// </summary>
public sealed class FirstDataWatch : IDisposable
{
    /// <summary>The wait after which the banner "Still summoning the court…" shows.</summary>
    public static readonly TimeSpan SlowAfter = TimeSpan.FromSeconds(8);

    /// <summary>The wait after which the sheet shows the error and the Retry button.</summary>
    public static readonly TimeSpan FailedAfter = TimeSpan.FromSeconds(20);

    private readonly Func<TimeProvider> _time;
    private readonly object _gate = new();
    private ITimer? _slowTimer;
    private ITimer? _failedTimer;
    private FirstDataStage _stage = FirstDataStage.Waiting;
    private bool _begun;
    private bool _disposed;

    /// <param name="time">The session's clock (<c>IRealmSession.Time</c>).</param>
    public FirstDataWatch(TimeProvider time)
        : this(() => time)
    {
        ArgumentNullException.ThrowIfNull(time);
    }

    /// <param name="time">Reads the session's clock when <see cref="Begin"/> runs, so creating the watch touches nothing on the session (creating a session has no other side effect).</param>
    public FirstDataWatch(Func<TimeProvider> time)
    {
        ArgumentNullException.ThrowIfNull(time);
        _time = time;
    }

    /// <summary>Raised from a timer's thread when the stage moves: subscribers marshal with <c>InvokeAsync</c>.</summary>
    public event Action? Changed;

    /// <summary>How long the circuit has waited so far. <see cref="FirstDataStage.Waiting"/> until <see cref="Begin"/> has been called and 8 s have passed.</summary>
    public FirstDataStage Stage
    {
        get
        {
            lock (_gate)
            {
                return _stage;
            }
        }
    }

    /// <summary>
    /// True before the first data snapshot: no people, no vehicles and no places. Mirrors <c>HandleSummaryFormatter.Format</c>, which writes "Summoning the court…" for exactly this
    /// snapshot, so the summary, the skeleton rows and the banners always agree.
    /// </summary>
    public static bool IsPending(IReadOnlyList<MemberVm> members, IReadOnlyList<VehicleVm> vehicles, IReadOnlyList<PlaceVm> places)
    {
        ArgumentNullException.ThrowIfNull(members);
        ArgumentNullException.ThrowIfNull(vehicles);
        ArgumentNullException.ThrowIfNull(places);
        return members.Count == 0 && vehicles.Count == 0 && places.Count == 0;
    }

    /// <summary>Starts both deadlines. A second call does nothing, so a re-render of the shell never restarts the clock.</summary>
    public void Begin()
    {
        lock (_gate)
        {
            if (_begun || _disposed)
            {
                return;
            }

            _begun = true;
            var clock = _time();
            _slowTimer = clock.CreateTimer(_ => MoveTo(FirstDataStage.Slow), null, SlowAfter, Timeout.InfiniteTimeSpan);
            _failedTimer = clock.CreateTimer(_ => MoveTo(FirstDataStage.Failed), null, FailedAfter, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>Stops both timers. Nothing is raised after this.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _slowTimer?.Dispose();
            _failedTimer?.Dispose();
            _slowTimer = null;
            _failedTimer = null;
        }
    }

    // A stage only moves forward, and never after disposal.
    private void MoveTo(FirstDataStage stage)
    {
        lock (_gate)
        {
            if (_disposed || stage <= _stage)
            {
                return;
            }

            _stage = stage;
        }

        Changed?.Invoke();
    }
}
