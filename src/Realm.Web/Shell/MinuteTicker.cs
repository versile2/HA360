namespace Realm.Web.Shell;

/// <summary>
/// Raises <see cref="Tick"/> on every minute boundary of the session's clock (03 section 2.9, R-041): the owner of the minute-granular strings
/// ("Here for 3 hrs, 33 mins", "Updated 5 min ago"). The 30 s data tick of the session owns <c>Freshness</c> and nothing else. Created by a
/// component after its first render (so never during prerendering) and disposed with it; the clock is the session's <see cref="TimeProvider"/>,
/// never the wall clock, so a test that owns the clock owns the ticks.
/// </summary>
/// <remarks>
/// Each wait is computed again from the clock, so the ticks do not drift and a clock that jumps is followed. Under the frozen Demo clock the
/// delay is the same every time and the ticks arrive in real time, which is harmless: nothing the page sends changes while the clock stands still.
/// </remarks>
public sealed class MinuteTicker : IDisposable
{
    private readonly TimeProvider _time;
    private readonly CancellationTokenSource _stop = new();
    private bool _started;

    public MinuteTicker(TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(time);
        _time = time;
    }

    /// <summary>Raised on a thread-pool thread; the subscriber marshals back to its renderer.</summary>
    public event Action? Tick;

    /// <summary>Starts the ticks. Calling it again, or after <see cref="Dispose"/>, does nothing.</summary>
    public void Start()
    {
        if (_started || _stop.IsCancellationRequested)
        {
            return;
        }

        _started = true;
        _ = RunAsync(_stop.Token);
    }

    /// <summary>The wait from <paramref name="now"/> to the next whole minute; a full minute exactly on a boundary.</summary>
    public static TimeSpan UntilNextMinute(DateTimeOffset now)
    {
        var intoMinute = now.UtcTicks % TimeSpan.TicksPerMinute;
        return TimeSpan.FromTicks(TimeSpan.TicksPerMinute - intoMinute);
    }

    /// <summary>Stops the ticks; the subscribers are released.</summary>
    public void Dispose()
    {
        if (_stop.IsCancellationRequested)
        {
            return;
        }

        _stop.Cancel();
        _stop.Dispose();
        Tick = null;
    }

    private async Task RunAsync(CancellationToken stop)
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                await Task.Delay(UntilNextMinute(_time.GetUtcNow()), _time, stop).ConfigureAwait(false);
                try
                {
                    Tick?.Invoke();
                }
                catch (Exception)
                {
                    // One broken subscriber must not end the ticks for the rest.
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Disposed.
        }
        catch (ObjectDisposedException)
        {
            // Disposed between the check and the wait.
        }
    }
}
