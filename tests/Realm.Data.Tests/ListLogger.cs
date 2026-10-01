using Microsoft.Extensions.Logging;

namespace Realm.Data.Tests;

/// <summary>Collects the formatted log messages so a test can assert what was logged.</summary>
internal sealed class ListLogger<T> : ILogger<T>
{
    private readonly List<string> _messages = [];

    // A background service logs from its own thread while the test reads: a copy under the lock.
    public IReadOnlyList<string> Messages
    {
        get
        {
            lock (_messages)
            {
                return _messages.ToArray();
            }
        }
    }

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
    {
        return null;
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return true;
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var message = formatter(state, exception);
        lock (_messages)
        {
            _messages.Add(message);
        }
    }
}
