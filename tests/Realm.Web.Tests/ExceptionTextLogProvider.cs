using Microsoft.Extensions.Logging;

namespace Realm.Web.Tests;

/// <summary>
/// Wraps a logger provider so that a log call's exception, stack trace included, becomes part of the message the wrapped provider records.
/// <see cref="Realm.TestKit.InMemoryLogSink"/> keeps only the formatted message, and the default formatter leaves the exception out, so
/// without this a server-side exception (the 500 of a page that fails to render) would never reach a test's failure text.
/// </summary>
internal sealed class ExceptionTextLogProvider(ILoggerProvider inner) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new ExceptionTextLogger(inner.CreateLogger(categoryName));

    public void Dispose()
    {
        // The wrapped provider belongs to the test that created it; it stays readable after the host is disposed.
    }

    private sealed class ExceptionTextLogger(ILogger inner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => inner.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel) => inner.IsEnabled(logLevel);

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (exception is null)
            {
                inner.Log(logLevel, eventId, state, exception, formatter);
                return;
            }

            inner.Log(logLevel, eventId, state, exception, (s, e) => $"{formatter(s, e)}{Environment.NewLine}{e}");
        }
    }
}
