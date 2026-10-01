using Microsoft.Extensions.Logging;

namespace Realm.Infrastructure.Options;

/// <summary>
/// The add-on options as typed values (02 section 3). <see cref="OptionsBinding"/> is the only producer: it reads every property from the
/// configuration path of 02 section 3.4, so the defaults live in one place, the binding table. Take <see cref="OptionsBinding.Defaults"/> and
/// <c>with</c> a change in a test. Units are SI where the option is not: the mph options arrive as m/s and the mile option as metres.
/// </summary>
public sealed record RealmOptions
{
    /// <summary>log_level.</summary>
    public required LogLevel LogLevel { get; init; }

    /// <summary>ui_stale_after_minutes: a floor, see 02 section 4.7.</summary>
    public required int UiStaleAfterMinutes { get; init; }

    /// <summary>ui_offline_after_hours.</summary>
    public required int UiOfflineAfterHours { get; init; }

    /// <summary>ui_vehicle_stale_after_minutes.</summary>
    public required int UiVehicleStaleAfterMinutes { get; init; }

    /// <summary>ui_low_battery_percent.</summary>
    public required int UiLowBatteryPercent { get; init; }

    /// <summary>ui_poor_accuracy_meters.</summary>
    public required int UiPoorAccuracyMeters { get; init; }

    /// <summary>ui_default_view_radius_km.</summary>
    public required int UiDefaultViewRadiusKm { get; init; }

    /// <summary>ui_max_zone_radius_km.</summary>
    public required double UiMaxZoneRadiusKm { get; init; }

    /// <summary>ui_far_away_km.</summary>
    public required int UiFarAwayKm { get; init; }

    /// <summary>ui_history_tokens: the Back-gesture kill switch, bound at <c>Realm:Ui:HistoryTokens</c> (D47).</summary>
    public required bool UiHistoryTokens { get; init; }

    /// <summary>features_temp_bubble.</summary>
    public required bool FeaturesTempBubble { get; init; }

    /// <summary>features_add_rows.</summary>
    public required bool FeaturesAddRows { get; init; }

    /// <summary>fusion_stale_grace_minutes.</summary>
    public required int FusionStaleGraceMinutes { get; init; }

    /// <summary>trips_start_speed_mph, in metres per second.</summary>
    public required double TripsStartSpeedMps { get; init; }

    /// <summary>trips_stop_merge_seconds.</summary>
    public required int TripsStopMergeSeconds { get; init; }

    /// <summary>trips_min_distance_miles, in metres.</summary>
    public required double TripsMinDistanceM { get; init; }

    /// <summary>trips_min_duration_seconds.</summary>
    public required int TripsMinDurationSeconds { get; init; }

    /// <summary>driving_week_start: <see cref="DayOfWeek.Monday"/> or <see cref="DayOfWeek.Sunday"/>.</summary>
    public required DayOfWeek DrivingWeekStart { get; init; }

    /// <summary>driving_speeding_mph, in metres per second.</summary>
    public required double DrivingSpeedingMps { get; init; }

    /// <summary>driving_speeding_min_seconds.</summary>
    public required int DrivingSpeedingMinSeconds { get; init; }

    /// <summary>driving_phone_min_seconds.</summary>
    public required int DrivingPhoneMinSeconds { get; init; }

    /// <summary>retention_fix_days.</summary>
    public required int RetentionFixDays { get; init; }

    /// <summary>backfill_days.</summary>
    public required int BackfillDays { get; init; }

    /// <summary>privacy_log_positions.</summary>
    public required bool PrivacyLogPositions { get; init; }

    /// <summary>demo_mode.</summary>
    public required bool DemoMode { get; init; }

    /// <summary>allow_demo_param.</summary>
    public required bool AllowDemoParam { get; init; }

    /// <summary>me_fallback_member: a member id, or empty when the optional key is absent (R-068).</summary>
    public required string MeFallbackMember { get; init; }

    /// <summary>ignore_entities: entity ids never used as sources.</summary>
    public required IReadOnlyList<string> IgnoreEntities { get; init; }

    /// <summary>members, in the order of the options.</summary>
    public required IReadOnlyList<MemberOption> Members { get; init; }

    /// <summary>vehicles, in the order of the options.</summary>
    public required IReadOnlyList<VehicleOption> Vehicles { get; init; }

    /// <summary>places, in the order of the options.</summary>
    public required IReadOnlyList<PlaceOption> Places { get; init; }
}
