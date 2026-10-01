namespace Realm.Demo;

/// <summary>
/// The Demo session's clock (02 section 9.1, R3-006): frozen at one instant, or, when asked, advancing in real time
/// from it (the ha-down variant, so members go stale). A Demo-owned class because FakeTimeProvider is a test package
/// and Realm.Demo ships in the image. <see cref="CreateTimer"/> delegates to the system provider, so a PeriodicTimer
/// ticker still fires under a frozen clock.
/// </summary>
public sealed class DemoTimeProvider : TimeProvider
{
    private readonly DateTimeOffset _start;
    private readonly bool _advancing;
    private readonly long _startTimestamp;

    /// <param name="instant">The instant the clock shows when created.</param>
    /// <param name="advancing">False (the default): the clock never moves. True: it runs in real time from <paramref name="instant"/>.</param>
    public DemoTimeProvider(DateTimeOffset instant, bool advancing = false)
    {
        _start = instant.ToUniversalTime();
        _advancing = advancing;
        _startTimestamp = GetTimestamp();
    }

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => _advancing ? _start + GetElapsedTime(_startTimestamp) : _start;

    /// <inheritdoc />
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
        TimeProvider.System.CreateTimer(callback, state, dueTime, period);
}
