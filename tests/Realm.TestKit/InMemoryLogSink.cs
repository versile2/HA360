using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Realm.TestKit;

/// <summary>A logger provider that keeps every call in memory so a test can assert on it (03 section 8.1 rule 5).</summary>
public sealed class InMemoryLogSink : ILoggerProvider
{
    private readonly ConcurrentQueue<LogEntry> _entries = new();

    public IReadOnlyList<LogEntry> Entries => _entries.ToArray();

    public ILogger CreateLogger(string categoryName) => new SinkLogger(categoryName, _entries);

    public void Dispose()
    {
        // Nothing to release: the entries stay readable after the host that used the sink is disposed.
    }

    private sealed class SinkLogger(string category, ConcurrentQueue<LogEntry> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => entries.Enqueue(new LogEntry(category, logLevel, eventId, formatter(state, exception)));
    }
}
