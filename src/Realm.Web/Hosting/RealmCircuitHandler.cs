using System.Collections.Concurrent;
using Microsoft.AspNetCore.Components.Server.Circuits;

namespace Realm.Web.Hosting;

/// <summary>
/// Counts the open and the disconnected Blazor circuits for <c>diagnostics.json</c> (03 sections 2.11 and 5.6), logs each open and close at Debug
/// and warns above 30 concurrent circuits (a leak signal at household scale). One instance serves every circuit: it is registered as a singleton,
/// and as a <see cref="CircuitHandler"/> so the framework calls it.
/// </summary>
public sealed partial class RealmCircuitHandler(ILogger<RealmCircuitHandler> logger) : CircuitHandler
{
    private const int LeakWarningThreshold = 30;

    // Circuit id -> whether its connection is up. A disconnected circuit is retained for 45 minutes (5.6), so it stays here until it is closed.
    private readonly ConcurrentDictionary<string, bool> _circuits = new();

    /// <summary>Circuits whose connection is up.</summary>
    public int Open => _circuits.Count(circuit => circuit.Value);

    /// <summary>Circuits the server still holds but whose connection is down.</summary>
    public int Disconnected => _circuits.Count(circuit => !circuit.Value);

    /// <inheritdoc />
    public override Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        _circuits[circuit.Id] = true;
        LogOpened(logger, _circuits.Count);
        if (_circuits.Count > LeakWarningThreshold)
        {
            LogTooMany(logger, _circuits.Count, LeakWarningThreshold);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public override Task OnConnectionUpAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        _circuits.TryUpdate(circuit.Id, true, false);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public override Task OnConnectionDownAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        _circuits.TryUpdate(circuit.Id, false, true);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public override Task OnCircuitClosedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        _circuits.TryRemove(circuit.Id, out _);
        LogClosed(logger, _circuits.Count);
        return Task.CompletedTask;
    }

    [LoggerMessage(EventId = 6002, Level = LogLevel.Debug, Message = "Circuit opened; {Count} held.")]
    private static partial void LogOpened(ILogger logger, int count);

    [LoggerMessage(EventId = 6003, Level = LogLevel.Debug, Message = "Circuit closed; {Count} held.")]
    private static partial void LogClosed(ILogger logger, int count);

    [LoggerMessage(EventId = 6004, Level = LogLevel.Warning, Message = "{Count} circuits are held, above {Threshold}: a circuit is probably not being released.")]
    private static partial void LogTooMany(ILogger logger, int count, int threshold);
}
