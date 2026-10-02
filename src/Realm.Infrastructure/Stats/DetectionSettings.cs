using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Realm.Domain;
using Realm.Infrastructure.Options;

namespace Realm.Infrastructure.Stats;

/// <summary>
/// The trip detector's parameters as the options set them, and the two stamps every stored trip carries (02 section 7.7). One place builds them, so live
/// detection, the start-up replay, the backfill replay and the phone-use counting all run with the same numbers.
/// </summary>
internal sealed class DetectionSettings
{
    /// <summary>
    /// <c>trips.algo_version</c> (02 section 7.7): bump it whenever the detector, the speeding rule or the distance logic changes. v1 recomputes nothing,
    /// so trips already stored keep the version they were closed with.
    /// </summary>
    public const int AlgoVersion = 1;

    public DetectionSettings(RealmOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        Trips = new TripOptions(
            StartSpeedMps: options.TripsStartSpeedMps,
            StopMergeS: options.TripsStopMergeSeconds,
            MinDistanceM: options.TripsMinDistanceM,
            MinDurationS: options.TripsMinDurationSeconds);
        Driving = new DrivingOptions(
            SpeedingMps: options.DrivingSpeedingMps,
            SpeedingMinS: options.DrivingSpeedingMinSeconds,
            PhoneMinS: options.DrivingPhoneMinSeconds);
        DeriveHash = HashOf(options);
    }

    public TripOptions Trips { get; }

    public DrivingOptions Driving { get; }

    /// <summary><c>trips.derive_hash</c>: SHA-256 (first 16 hex digits) of the option values that influence the derived numbers: <c>trips_*</c>, <c>driving_speeding_*</c>, <c>driving_phone_*</c>.</summary>
    public string DeriveHash { get; }

    private static string HashOf(RealmOptions options)
    {
        var text = string.Join(
            '|',
            new[]
            {
                options.TripsStartSpeedMps.ToString("R", CultureInfo.InvariantCulture),
                options.TripsStopMergeSeconds.ToString(CultureInfo.InvariantCulture),
                options.TripsMinDistanceM.ToString("R", CultureInfo.InvariantCulture),
                options.TripsMinDurationSeconds.ToString(CultureInfo.InvariantCulture),
                options.DrivingSpeedingMps.ToString("R", CultureInfo.InvariantCulture),
                options.DrivingSpeedingMinSeconds.ToString(CultureInfo.InvariantCulture),
                options.DrivingPhoneMinSeconds.ToString(CultureInfo.InvariantCulture),
            });
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16];
    }
}
