using Microsoft.Extensions.Logging;
using Realm.Domain;
using Realm.Infrastructure.Ha;

namespace Realm.Infrastructure.Places;

/// <summary>
/// The Live <see cref="IPlaceEditor"/> (0.2.2, D119): asks Home Assistant to create the zone through <see cref="IHaGateway.CreateZoneAsync"/> and keeps nothing itself.
/// Zones mirror Home Assistant, so the new zone reaches the map by the normal zone sync; this service only rings the <see cref="ZoneRefreshSignal"/> (at once, and
/// again a few seconds later, because Home Assistant adds the zone's entity a moment after it answers) so that it does not wait for the five-minute refresh. A refusal,
/// a connection that is down and a timeout are all a <see cref="PlaceCreateResult"/> with a sentence; no zone is made locally.
/// </summary>
public sealed class ZoneService : IPlaceEditor
{
    /// <summary>How long after a creation the zones are read a second time.</summary>
    public static readonly TimeSpan SecondRefreshDelay = TimeSpan.FromSeconds(3);

    private readonly IHaGateway _gateway;
    private readonly ZoneRefreshSignal _signal;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;

    public ZoneService(IHaGateway gateway, ZoneRefreshSignal signal, TimeProvider time, ILogger<ZoneService> logger)
    {
        _gateway = gateway;
        _signal = signal;
        _time = time;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<PlaceCreateResult> CreateAsync(NewZone zone, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(zone);
        if (zone.Problem() is { } problem)
        {
            return PlaceCreateResult.Failure(problem);
        }

        try
        {
            await _gateway.CreateZoneAsync(zone with { Name = zone.Name.Trim() }, cancellationToken);
        }
        catch (HaCommandException ex)
        {
            _logger.LogWarning("Home Assistant did not create the place ({Code})", ex.Code);
            return PlaceCreateResult.Failure(PlaceCreateResult.Describe(ex.Code));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning("The place could not be created ({ErrorType})", ex.GetType().Name);
            return PlaceCreateResult.Failure(PlaceCreateResult.Describe(null));
        }

        _logger.LogInformation("A place was created in Home Assistant");
        _signal.Request();
        _ = RingAgainAsync();
        return PlaceCreateResult.Success;
    }

    // Home Assistant answers zone/create when the item is stored; its entity can show up a moment later, so the zones are read a second time.
    private async Task RingAgainAsync()
    {
        try
        {
            await Task.Delay(SecondRefreshDelay, _time);
            _signal.Request();
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
        {
            // The app is stopping.
        }
    }
}
