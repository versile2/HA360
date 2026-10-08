using Microsoft.Extensions.Logging;

namespace Realm.Infrastructure.Options;

/// <summary>
/// The add-on options as typed values (02 section 3). Since 0.2.0 (D114) the add-on has six options: <see cref="DemoMode"/>, <see cref="AllowDemoParam"/>,
/// <see cref="LogLevel"/>, <see cref="RetentionFixDays"/>, <see cref="DrivingWeekStart"/> and <see cref="DrivingSpeedingMps"/>. <see cref="OptionsBinding"/> is the only
/// producer and reads those six; every other value is a fixed default that lives here, in one place, and is changed in a test with a <c>with</c> expression
/// on <see cref="OptionsBinding.Defaults"/>. Who is on the map is not an option: it is the roster in the database (02 section 2.7). Units are SI where the option
/// is not: the mph option arrives as m/s.
/// </summary>
public sealed record RealmOptions
{
    // ---- the six add-on options ---------------------------------------------------------------------------------

    /// <summary>log_level.</summary>
    public LogLevel LogLevel { get; init; } = LogLevel.Information;

    /// <summary>driving_week_start: <see cref="DayOfWeek.Monday"/> or <see cref="DayOfWeek.Sunday"/>.</summary>
    public DayOfWeek DrivingWeekStart { get; init; } = DayOfWeek.Monday;

    /// <summary>driving_speeding_mph, in metres per second.</summary>
    public double DrivingSpeedingMps { get; init; } = 80 * 0.44704;

    /// <summary>retention_fix_days.</summary>
    public int RetentionFixDays { get; init; } = 100;

    /// <summary>demo_mode.</summary>
    public bool DemoMode { get; init; }

    /// <summary>allow_demo_param.</summary>
    public bool AllowDemoParam { get; init; }

    // ---- fixed values (they were options before 0.2.0) ----------------------------------------------------------

    /// <summary>Minutes without a new position before a person is stale: a floor, see 02 section 4.7.</summary>
    public int UiStaleAfterMinutes { get; init; } = 30;

    /// <summary>Hours without any position before a person is offline.</summary>
    public int UiOfflineAfterHours { get; init; } = 24;

    /// <summary>Minutes without an update before a vehicle's position is stale.</summary>
    public int UiVehicleStaleAfterMinutes { get; init; } = 45;

    /// <summary>Phone battery percent at or below which the battery is flagged.</summary>
    public int UiLowBatteryPercent { get; init; } = 15;

    /// <summary>Accuracy radius in metres above which a position is labelled approximate.</summary>
    public int UiPoorAccuracyMeters { get; init; } = 500;

    /// <summary>Radius in kilometres of the area the map fits when it first opens.</summary>
    public int UiDefaultViewRadiusKm { get; init; } = 40;

    /// <summary>Zones with a larger radius (km) are never drawn or listed.</summary>
    public double UiMaxZoneRadiusKm { get; init; } = 5;

    /// <summary>Distance in kilometres from "me" beyond which a person's city and state are shown.</summary>
    public int UiFarAwayKm { get; init; } = 80;

    /// <summary>The Back-gesture switch, bound at <c>Realm:Ui:HistoryTokens</c> by the web host (D47); always on.</summary>
    public bool UiHistoryTokens { get; init; } = true;

    /// <summary>Reserved; always off.</summary>
    public bool FeaturesTempBubble { get; init; }

    /// <summary>Reserved; always off.</summary>
    public bool FeaturesAddRows { get; init; }

    /// <summary>Extra minutes allowed on top of a source's normal update interval before its position counts as old.</summary>
    public int FusionStaleGraceMinutes { get; init; } = 10;

    /// <summary>Speed above which movement starts a drive, in metres per second (15 mph).</summary>
    public double TripsStartSpeedMps { get; init; } = 15 * 0.44704;

    /// <summary>Stops shorter than this many seconds stay part of the same drive.</summary>
    public int TripsStopMergeSeconds { get; init; } = 180;

    /// <summary>Drives shorter than this many metres are not recorded (0.3 miles).</summary>
    public double TripsMinDistanceM { get; init; } = 0.3 * 1609.344;

    /// <summary>Drives shorter than this many seconds are not recorded.</summary>
    public int TripsMinDurationSeconds { get; init; } = 120;

    /// <summary>Seconds above the speeding threshold before a stretch counts as speeding.</summary>
    public int DrivingSpeedingMinSeconds { get; init; } = 30;

    /// <summary>Seconds of screen use while driving before it counts as phone use.</summary>
    public int DrivingPhoneMinSeconds { get; init; } = 10;

    /// <summary>Days of Home Assistant history read when the app starts. 0 turns the backfill off.</summary>
    public int BackfillDays { get; init; } = 10;

    /// <summary>Debug logs carry positions only when true; always off.</summary>
    public bool PrivacyLogPositions { get; init; }
}
