using Microsoft.Extensions.Logging;

namespace Realm.Infrastructure.Hosting;

/// <summary>
/// Wraps the loop of one background service (03 section 2.14): everything except cancellation is caught, logged once per distinct error per minute,
/// recorded in the service's <see cref="ServiceHealth"/>, and the loop is started again after a back-off of 1 s that doubles up to 60 s. A programming
/// error therefore shows up in the log and in the diagnostics instead of stopping the host. A loop that returns normally is finished and is not restarted.
/// </summary>
public sealed class ResilientLoop
{
    /// <summary>The wait after the first failure.</summary>
    public static readonly TimeSpan InitialBackoff = TimeSpan.FromSeconds(1);

    /// <summary>The wait never grows beyond this; a loop that then runs at least this long before failing starts again from <see cref="InitialBackoff"/>.</summary>
    public static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(60);

    /// <summary>The same error is logged at most once per this interval.</summary>
    public static readonly TimeSpan LogInterval = TimeSpan.FromMinutes(1);

    // The log memory is dropped when it grows past this, so a stream of distinct messages cannot grow it without bound.
    private const int MaxRememberedErrors = 64;

    private readonly string _name;
    private readonly ILogger _logger;
    private readonly TimeProvider _time;
    private readonly Dictionary<string, DateTimeOffset> _lastLogged = new(StringComparer.Ordinal);

    public ResilientLoop(string name, ILogger logger, TimeProvider time, ServiceHealth? health = null)
    {
        _name = name;
        _logger = logger;
        _time = time;
        Health = health ?? new ServiceHealth(name);
        Delay = (span, token) => Task.Delay(span, _time, token);
    }

    public ServiceHealth Health { get; }

    /// <summary>How the back-off waits. It follows the injected clock; a test replaces it to see the requested waits without waiting.</summary>
    public Func<TimeSpan, CancellationToken, Task> Delay { get; init; }

    /// <summary>
    /// Runs <paramref name="body"/> until it returns, restarting it after every failure. Returns when the body returns or when
    /// <paramref name="cancellationToken"/> is cancelled; it never throws for either.
    /// </summary>
    public async Task RunAsync(Func<CancellationToken, Task> body, CancellationToken cancellationToken)
    {
        var backoff = InitialBackoff;
        while (!cancellationToken.IsCancellationRequested)
        {
            Health.MarkHealthy();
            var started = _time.GetUtcNow();
            try
            {
                await body(cancellationToken);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                var now = _time.GetUtcNow();
                if (now - started >= MaxBackoff)
                {
                    backoff = InitialBackoff;
                }

                Health.MarkFaulted(ex.GetType().Name);
                LogOnce(ex, backoff, now);
                try
                {
                    await Delay(backoff, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                backoff = backoff * 2 > MaxBackoff ? MaxBackoff : backoff * 2;
            }
        }
    }

    private void LogOnce(Exception error, TimeSpan backoff, DateTimeOffset now)
    {
        var key = error.GetType().FullName + ": " + error.Message;
        if (_lastLogged.TryGetValue(key, out var last) && now - last < LogInterval)
        {
            return;
        }

        if (_lastLogged.Count >= MaxRememberedErrors)
        {
            _lastLogged.Clear();
        }

        _lastLogged[key] = now;
        _logger.LogError(error, "{Service} failed; starting again in {BackoffSeconds} s", _name, backoff.TotalSeconds);
    }
}
