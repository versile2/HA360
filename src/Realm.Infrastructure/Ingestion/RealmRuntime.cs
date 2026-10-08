using Realm.Domain;
using Realm.Infrastructure.Ha;

namespace Realm.Infrastructure.Ingestion;

/// <summary>
/// The mutable, per-member state of the pipeline (03 section 2.8): the latest accepted fix of each source (the <c>F_s</c> pointers of 02 section 4.1), the
/// phone's battery sensors, the stored fix times the heartbeat is measured from, the zones the member is in, the trip detector and what the "since" ladder
/// needs. Only the pipeline touches it, under its lock.
/// </summary>
internal sealed class MemberRuntime
{
    public MemberRuntime(ResolvedMember plan, TripDetector? detector)
    {
        Plan = plan;
        Detector = detector;
    }

    public ResolvedMember Plan { get; set; }

    /// <summary>Null for a static member, which has no fixes.</summary>
    public TripDetector? Detector { get; }

    public RawFix? Life360 { get; set; }

    public RawFix? Companion { get; set; }

    /// <summary>The Android battery sensors (02 section 4.3): a reading with its own timestamp, newer than the fix's own battery when it is.</summary>
    public int? SensorBatteryPct { get; set; }

    public DateTimeOffset? SensorBatteryAsOfUtc { get; set; }

    public bool? SensorCharging { get; set; }

    /// <summary>Fix times of the last 24 hours per source, the input of <see cref="FreshnessRules.Heartbeat"/>.</summary>
    public Dictionary<FixSource, List<DateTimeOffset>> FixTimes { get; } = [];

    public TimeSpan Heartbeat { get; set; } = TimeSpan.FromMinutes(FreshnessRules.DefaultHeartbeatMinutes);

    public IReadOnlyList<string> ZoneIds { get; set; } = [];

    public string? PlaceId { get; set; }

    /// <summary>Life360's <c>at_loc_since</c> of the tracker, used only for a visit that was already running when the add-on started.</summary>
    public DateTimeOffset? AtLocSinceUtc { get; set; }

    /// <summary>The kind of the current run ("drive", "place:{id}" or "out") and when it began (02 section 4.6, simplified).</summary>
    public string? RunKey { get; set; }

    public DateTimeOffset? RunStartUtc { get; set; }

    public bool RunCensored { get; set; } = true;

    /// <summary>When the Life360 tracker was first seen unavailable or unknown; null while it reports.</summary>
    public DateTimeOffset? Life360UnavailableSinceUtc { get; set; }

    public bool Life360Seen { get; set; }

    public Dictionary<PhoneSignalKind, (bool? IsOn, DateTimeOffset Ts)> LastSignals { get; } = [];
}

/// <summary>The mutable state of one vehicle: its tracker's last accepted fix and the zones it is in.</summary>
internal sealed class VehicleRuntime
{
    public VehicleRuntime(ResolvedVehicle plan)
    {
        Plan = plan;
    }

    public ResolvedVehicle Plan { get; set; }

    public RawFix? Fix { get; set; }

    public IReadOnlyList<string> ZoneIds { get; set; } = [];

    public string? PlaceId { get; set; }
}
