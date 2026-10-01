using Microsoft.Extensions.Logging;

namespace Realm.Data.Tests;

/// <summary>Collects the formatted log messages so a test can assert what was logged.</summary>
internal sealed class ListLogger<T> : ILogger<T>
{
    private readonly List<string> _messages = [];

    public IReadOnlyList<string> Messages => _messages;

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
        _messages.Add(formatter(state, exception));
    }
}
