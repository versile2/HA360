using Microsoft.Extensions.Logging;

namespace Realm.Ingestion.Tests;

/// <summary>Keeps every log call, with its level, formatted message and the text of its exception, so a test can assert what was (and was not) logged.</summary>
internal sealed class RecordingLogger<T> : ILogger<T>
{
    private readonly List<(LogLevel Level, string Text)> _entries = [];

    /// <summary>Every call so far; the text is the message followed by the exception's full text.</summary>
    public IReadOnlyList<(LogLevel Level, string Text)> Entries
    {
        get
        {
            lock (_entries)
            {
                return _entries.ToArray();
            }
        }
    }

    /// <summary>All the text that was logged, for a search that must find nothing (a token).</summary>
    public string Text => string.Join('\n', Entries.Select(entry => entry.Text));

    public IReadOnlyList<string> Messages(LogLevel level) => Entries.Where(entry => entry.Level == level).Select(entry => entry.Text).ToArray();

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
        var text = formatter(state, exception) + (exception is null ? string.Empty : "\n" + exception);
        lock (_entries)
        {
            _entries.Add((logLevel, text));
        }
    }
}
