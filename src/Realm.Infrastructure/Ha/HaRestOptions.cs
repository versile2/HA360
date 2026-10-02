using System.Text;

namespace Realm.Infrastructure.Ha;

/// <summary>
/// Settings of <see cref="HaRestClient"/> (03 section 2.6). The defaults are the contract: the Supervisor's proxy to Core, a 60 s timeout per attempt,
/// retries after 1, 2, 5, 10 and 30 s, and 250 ms between history requests. The token is a plain string that is only ever sent as the bearer header;
/// the record's <c>ToString</c> leaves it out, so logging the options cannot leak it.
/// </summary>
public sealed record HaRestOptions
{
    /// <summary>The Supervisor's proxy to the Home Assistant REST API. The trailing slash matters: every request path is relative to it.</summary>
    public static readonly Uri SupervisorBaseAddress = new("http://supervisor/core/api/");

    public Uri BaseAddress { get; init; } = SupervisorBaseAddress;

    /// <summary><c>SUPERVISOR_TOKEN</c>. Null or empty sends no Authorization header.</summary>
    public string? Token { get; init; }

    /// <summary>One attempt gets this long (connect, headers and body).</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>The waits before the retries of a 502, 503 or 504, a timeout or a refused connection, one per retry; when they are used up the call fails.</summary>
    public IReadOnlyList<TimeSpan> RetryDelays { get; init; } =
    [
        TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30),
    ];

    /// <summary>The least time between the end of one history request and the start of the next (02 section 1.1).</summary>
    public TimeSpan HistorySpacing { get; init; } = TimeSpan.FromMilliseconds(250);

    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("BaseAddress = ").Append(BaseAddress);
        return true;
    }
}
