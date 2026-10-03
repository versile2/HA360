using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Realm.Infrastructure.Diagnostics;

/// <summary>
/// The start-up time zone self-check of 03 section 5.1: it resolves an IANA zone id against the zone data of this machine and, when it cannot, logs an error and
/// sets the <c>zone_data_missing</c> warning of <c>diagnostics.json</c> (<see cref="ServiceCounters.RecordZoneData"/>). Without zone data every local time is UTC,
/// and the diagnostics file says so. It is a hosted service only so that it runs once at start; it reads a local table and finishes at once.
/// </summary>
/// <param name="counters">Where the answer is kept.</param>
/// <param name="logger">Where the error is logged.</param>
/// <param name="resolves">Whether a zone id resolves; <see cref="Resolves"/> when null (a test says "missing" without removing the machine's zone data).</param>
public sealed class ZoneDataSelfCheck(ServiceCounters counters, ILogger<ZoneDataSelfCheck> logger, Func<string, bool>? resolves = null) : IHostedService
{
    /// <summary>
    /// An id that needs real zone data (UTC does not): a zone with daylight saving time, so a machine with a partial table fails it too. The configured Home
    /// Assistant zone is not known yet at this point; <see cref="DiagnosticsSnapshotBuilder"/> checks that one when it reports.
    /// </summary>
    public const string ProbeZoneId = "America/New_York";

    /// <summary>Resolves the probe zone and records the answer. Returns true when the zone data is present.</summary>
    public bool Check()
    {
        var ok = resolves is null ? Resolves(ProbeZoneId) : resolves(ProbeZoneId);
        counters.RecordZoneData(ok);
        if (!ok)
        {
            logger.LogError("The time zone data of this machine is missing, so local times are shown in UTC");
        }

        return ok;
    }

    /// <summary>True when <paramref name="zoneId"/> is a zone this machine knows. An unknown id and a corrupt entry both read false; neither throws.</summary>
    public static bool Resolves(string zoneId)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(zoneId) is not null;
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        Check();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
