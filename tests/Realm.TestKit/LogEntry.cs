using Microsoft.Extensions.Logging;

namespace Realm.TestKit;

/// <summary>One formatted log call captured by <see cref="InMemoryLogSink"/>.</summary>
public sealed record LogEntry(string Category, LogLevel Level, EventId EventId, string Message);
