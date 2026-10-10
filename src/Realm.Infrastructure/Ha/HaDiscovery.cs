using System.Text.Json;
using Realm.Domain;
using Realm.Infrastructure.Avatars;

namespace Realm.Infrastructure.Ha;

/// <summary>
/// One thing Home Assistant reports that can be on the map (02 section 2.3): a <c>person</c> merged with its device trackers, or a GPS <c>device_tracker</c> that no
/// person owns, with every entity that feeds it. Discovery finds these; the roster (<see cref="RosterEntry"/>) says which of them are on the map and what they
/// are called. A null entity id means that source does not exist for it.
/// </summary>
/// <param name="EntityId">The roster key: the person's entity id, or the standalone tracker's.</param>
/// <param name="Name">The name a new roster entry takes: the person's first name, or the tracker's friendly name.</param>
/// <param name="Source">The word for where it comes from (<see cref="RosterEntry.Source"/>).</param>
/// <param name="Active">It reports a usable state now: the person, or one of its trackers, is not unavailable or unknown.</param>
/// <param name="UserId">The HA user id of the person; only ever compared, never shown or logged.</param>
/// <param name="AvatarUpstream">The servable picture (an HA <c>image/serve</c> path or a Life360 HTTPS URL); null for none.</param>
/// <param name="Identity">The entities behind it with their friendly names, for Settings (D117).</param>
public sealed record DiscoveredEntity(
    string EntityId,
    RosterKind Kind,
    string Name,
    string Source,
    bool Active,
    string? PersonId,
    string? UserId,
    string? Life360TrackerId,
    string? CompanionTrackerId,
    CompanionSensors? Sensors,
    string? AvatarUpstream,
    RosterIdentity? Identity = null)
{
    /// <summary>The tracker a vehicle follows: the phone app's (it has the speed) before Life360's.</summary>
    public string? PrimaryTrackerId => CompanionTrackerId ?? Life360TrackerId;

    /// <summary>What the lifecycle rules need to know about it.</summary>
    public RosterCandidate ToCandidate() => new(EntityId, Kind, Name, Source, Active);
}

/// <summary>
/// Discovery as a pure function (02 sections 1.2 and 2.3): the integration entity lists, the states and the roster in; the discovered entities, and from them the
/// resolved members, vehicles, zones and the websocket watch list out. No clock is read and nothing is logged: what is worth a warning comes back in
/// <see cref="HaDiscoveryResult.Warnings"/>, naming an entity or a member id and never a position.
/// </summary>
public static class HaDiscovery
{
    /// <summary>The source word of Home Assistant's own trackers (the companion app and any other GPS tracker).</summary>
    public const string HomeAssistantSource = "Home Assistant";

    /// <summary>The source word of Life360.</summary>
    public const string Life360Source = "Life360";

    private const string PersonPrefix = "person.";
    private const string TrackerPrefix = "device_tracker.";
    private const string ZonePrefix = "zone.";

    /// <summary>
    /// Which states of <c>GET states</c> discovery needs: persons, zones and trackers, and every entity of the mobile_app integration (the phone's sensors).
    /// </summary>
    public static Func<string, bool> StateFilter(HaIntegrationEntities integration)
    {
        var listed = new HashSet<string>(integration.MobileApp, StringComparer.Ordinal);
        return id => id.StartsWith(PersonPrefix, StringComparison.Ordinal)
            || id.StartsWith(ZonePrefix, StringComparison.Ordinal)
            || id.StartsWith(TrackerPrefix, StringComparison.Ordinal)
            || listed.Contains(id);
    }

    /// <summary>
    /// Every candidate for the roster (02 section 2.3): all <c>person</c> entities, each merged with its trackers, then every <c>device_tracker</c> with a GPS
    /// position (numeric <c>latitude</c> and <c>longitude</c>) that no person owns. Persons first, each group by entity id.
    /// </summary>
    public static IReadOnlyList<DiscoveredEntity> FindEntities(
        HaIntegrationEntities integration,
        IReadOnlyList<HaEntitySnapshot> states,
        List<string>? warnings = null)
    {
        var byId = new Dictionary<string, HaEntitySnapshot>(StringComparer.Ordinal);
        foreach (var state in states)
        {
            byId[state.EntityId] = state;
        }

        var life360 = new HashSet<string>(integration.Life360.Where(id => id.StartsWith(TrackerPrefix, StringComparison.Ordinal)), StringComparer.Ordinal);
        var mobileApp = new HashSet<string>(integration.MobileApp.Where(id => id.StartsWith(TrackerPrefix, StringComparison.Ordinal)), StringComparer.Ordinal);
        var persons = states
            .Where(s => s.EntityId.StartsWith(PersonPrefix, StringComparison.Ordinal))
            .Select(ParsePerson)
            .OrderBy(p => p.Id, StringComparer.Ordinal)
            .ToList();

        var found = new List<DiscoveredEntity>();
        var owned = new HashSet<string>(StringComparer.Ordinal);
        foreach (var person in persons)
        {
            foreach (var tracker in person.Trackers)
            {
                owned.Add(tracker);
            }

            found.Add(FromPerson(person, byId, life360, mobileApp, warnings));
        }

        foreach (var snapshot in states.Where(s => s.EntityId.StartsWith(TrackerPrefix, StringComparison.Ordinal)).OrderBy(s => s.EntityId, StringComparer.Ordinal))
        {
            if (owned.Contains(snapshot.EntityId) || !HasPosition(snapshot) || !IsGps(snapshot))
            {
                continue;
            }

            var isLife360 = life360.Contains(snapshot.EntityId);
            var companion = isLife360 ? null : snapshot.EntityId;
            var sensors = SensorsOf(companion, byId);
            found.Add(new DiscoveredEntity(
                EntityId: snapshot.EntityId,
                Kind: RosterKind.Tracker,
                Name: TrackerName(snapshot, isLife360),
                Source: isLife360 ? Life360Source : HomeAssistantSource,
                Active: IsAvailable(snapshot),
                PersonId: null,
                UserId: null,
                Life360TrackerId: isLife360 ? snapshot.EntityId : null,
                CompanionTrackerId: companion,
                Sensors: sensors,
                AvatarUpstream: ServablePicture(Text(snapshot, "entity_picture")),
                Identity: new RosterIdentity(
                    [new RosterIdentityEntity(snapshot.EntityId, Text(snapshot, "friendly_name"), isLife360 ? "Life360 tracker" : "Tracker")],
                    isLife360 ? Life360FullName(snapshot) : null,
                    ServablePicture(Text(snapshot, "entity_picture")) is not null)));
        }

        return found;
    }

    /// <summary>
    /// Resolves the members, vehicles, zones and watch list (02 sections 2.3, 2.4 and 1.2 step 8) from what was found and the roster: an entry in People
    /// becomes a member, one in Vehicles a vehicle, one in Not tracked is left out (it has no sources, so nothing about it is watched or stored), and an entry
    /// Home Assistant no longer reports is left out until the lifecycle rules retire it.
    /// </summary>
    public static HaDiscoveryResult Resolve(
        HaConfig config,
        IReadOnlyList<DiscoveredEntity> found,
        IReadOnlyList<HaEntitySnapshot> states,
        IReadOnlyList<RosterEntry> roster,
        IReadOnlyList<string>? warnings = null)
    {
        var byId = new Dictionary<string, HaEntitySnapshot>(StringComparer.Ordinal);
        foreach (var state in states)
        {
            byId[state.EntityId] = state;
        }

        var entities = found.ToDictionary(e => e.EntityId, StringComparer.Ordinal);
        var members = new List<ResolvedMember>();
        var vehicles = new List<ResolvedVehicle>();
        foreach (var entry in RosterRules.Sorted(roster))
        {
            if (!entities.TryGetValue(entry.EntityId, out var entity))
            {
                continue;
            }

            if (entry.Group == RosterGroup.People)
            {
                members.Add(new ResolvedMember(
                    Id: entry.Id,
                    DisplayName: entry.DisplayName,
                    LoreTitle: entry.LoreTitle,
                    Kind: MemberKind.Live,
                    Color: entry.Color,
                    SortOrder: entry.SortOrder,
                    InDrivingReport: true,
                    PersonId: entity.PersonId,
                    UserId: entity.UserId,
                    Life360TrackerId: entity.Life360TrackerId,
                    CompanionTrackerId: entity.CompanionTrackerId,
                    Sensors: entity.Sensors,
                    PhoneCapable: entity.Sensors?.Interactive is not null,
                    AvatarUpstream: entity.AvatarUpstream,
                    StaticLabel: null,
                    StaticAddress: null,
                    StaticLat: null,
                    StaticLon: null,
                    StaticShowAddress: false,
                    Icon: entry.Icon));
            }
            else if (entry.Group == RosterGroup.Vehicles)
            {
                vehicles.Add(new ResolvedVehicle(
                    Id: entry.Id,
                    Name: entry.DisplayName,
                    LoreTitle: entry.LoreTitle,
                    Glyph: VehicleGlyph.Car,
                    SortOrder: entry.SortOrder,
                    TrackerId: entity.PrimaryTrackerId,
                    Source: entity.PrimaryTrackerId is not null && entity.PrimaryTrackerId == entity.Life360TrackerId ? FixSource.Life360 : FixSource.Companion,
                    Color: entry.Color,
                    Icon: entry.Icon,
                    AvatarUpstream: entity.AvatarUpstream,
                    KeepHistory: entry.KeepHistory));
            }
        }

        var zones = states
            .Select(FixParser.ParseZone)
            .OfType<RawPlace>()
            .OrderBy(z => z.Id, StringComparer.Ordinal)
            .ToList();
        return new HaDiscoveryResult(config.TimeZone, config.Version, members, vehicles, zones, WatchListOf(members, vehicles, states, byId), warnings ?? []);
    }

    // ---- persons ------------------------------------------------------------------------------------------------

    private static DiscoveredEntity FromPerson(
        Person person,
        Dictionary<string, HaEntitySnapshot> byId,
        HashSet<string> life360,
        HashSet<string> mobileApp,
        List<string>? warnings)
    {
        var life360Tracker = person.Trackers.FirstOrDefault(id => life360.Contains(id) && byId.ContainsKey(id));
        var companion = ChooseCompanion(person, life360, mobileApp, byId, warnings);
        var personSnapshot = byId.GetValueOrDefault(person.Id);
        var life360Snapshot = life360Tracker is null ? null : byId.GetValueOrDefault(life360Tracker);
        var companionSnapshot = companion is null ? null : byId.GetValueOrDefault(companion);
        var active = (personSnapshot is not null && IsAvailable(personSnapshot))
            || (life360Snapshot is not null && IsAvailable(life360Snapshot))
            || (companionSnapshot is not null && IsAvailable(companionSnapshot));
        var source = life360Tracker is null ? HomeAssistantSource : companion is null ? Life360Source : HomeAssistantSource + " + " + Life360Source;
        var picture = ServablePicture(person.Picture) ?? ServablePicture(Text(life360Snapshot, "entity_picture"));
        var entities = new List<RosterIdentityEntity> { new(person.Id, person.FriendlyName, "Person") };
        if (life360Tracker is not null)
        {
            entities.Add(new RosterIdentityEntity(life360Tracker, Text(life360Snapshot, "friendly_name"), "Life360 tracker"));
        }

        if (companion is not null)
        {
            entities.Add(new RosterIdentityEntity(companion, Text(companionSnapshot, "friendly_name"), "Phone app tracker"));
        }

        return new DiscoveredEntity(
            EntityId: person.Id,
            Kind: RosterKind.Person,
            Name: FirstToken(person.FriendlyName) ?? Capitalize(ObjectId(person.Id)),
            Source: source,
            Active: active,
            PersonId: person.Id,
            UserId: person.UserId,
            Life360TrackerId: life360Tracker,
            CompanionTrackerId: companion,
            Sensors: SensorsOf(companion, byId),
            AvatarUpstream: picture,
            Identity: new RosterIdentity(entities, life360Snapshot is null ? null : Life360FullName(life360Snapshot), picture is not null));
    }

    // A person's GPS tracker that is not Life360's: the phone app's (mobile_app) before any other, then the one that reported last, then the lower entity id,
    // so the same states always give the same tracker. With two or more candidates the choice is reported as a warning (the owner's R-065 option is gone).
    private static string? ChooseCompanion(Person person, HashSet<string> life360, HashSet<string> mobileApp, Dictionary<string, HaEntitySnapshot> byId, List<string>? warnings)
    {
        var candidates = person.Trackers
            .Where(id => !life360.Contains(id)
                && byId.TryGetValue(id, out var snapshot)
                && IsGps(snapshot)
                && (mobileApp.Contains(id) || HasPosition(snapshot)))
            .OrderByDescending(id => mobileApp.Contains(id))
            .ThenByDescending(id => byId[id].LastUpdatedUtc ?? DateTimeOffset.MinValue)
            .ThenBy(id => id, StringComparer.Ordinal)
            .ToList();
        if (candidates.Count > 1)
        {
            warnings?.Add($"{person.Id}: the person owns {candidates.Count} GPS trackers; {candidates[0]} is used as the phone's source");
        }

        return candidates.Count == 0 ? null : candidates[0];
    }

    private static bool IsAvailable(HaEntitySnapshot snapshot) =>
        !(string.Equals(snapshot.State, "unavailable", StringComparison.OrdinalIgnoreCase) || string.Equals(snapshot.State, "unknown", StringComparison.OrdinalIgnoreCase));

    // A GPS tracker: it reports numeric latitude and longitude.
    private static bool HasPosition(HaEntitySnapshot snapshot) =>
        snapshot.Attributes.TryGetValue("latitude", out var lat) && lat.ValueKind == JsonValueKind.Number
        && snapshot.Attributes.TryGetValue("longitude", out var lon) && lon.ValueKind == JsonValueKind.Number;

    // source_type "gps" or none: a router or bluetooth tracker is not a position.
    private static bool IsGps(HaEntitySnapshot snapshot) =>
        Text(snapshot, "source_type") is not { } type || string.Equals(type, "gps", StringComparison.OrdinalIgnoreCase);

    private static CompanionSensors? SensorsOf(string? companionId, Dictionary<string, HaEntitySnapshot> byId)
    {
        if (companionId is null)
        {
            return null;
        }

        var stem = ObjectId(companionId);
        string? Found(string domain, string suffix)
        {
            var id = $"{domain}.{stem}_{suffix}";
            return byId.ContainsKey(id) ? id : null;
        }

        return new CompanionSensors(
            Found("sensor", "battery_level"),
            Found("sensor", "battery_state"),
            Found("binary_sensor", "interactive"),
            Found("binary_sensor", "device_locked"),
            Found("binary_sensor", "android_auto"));
    }

    private static string? ServablePicture(string? picture) => AvatarUpstream.IsServable(picture) ? picture!.Trim() : null;

    // ---- names --------------------------------------------------------------------------------------------------

    // The friendly name of a tracker without the "Life360 " prefix (and, for a Life360 tracker, the first name only, as for a person); the entity's own words when it has none.
    private static string TrackerName(HaEntitySnapshot snapshot, bool isLife360)
    {
        var name = Text(snapshot, "friendly_name");
        if (name is not null && name.StartsWith("Life360 ", StringComparison.OrdinalIgnoreCase))
        {
            name = name["Life360 ".Length..].Trim();
        }

        if (!string.IsNullOrWhiteSpace(name))
        {
            return isLife360 ? FirstToken(name) ?? name : name;
        }

        var stem = ObjectId(snapshot.EntityId);
        return Capitalize(stem.StartsWith("life360_", StringComparison.Ordinal) ? stem["life360_".Length..] : stem);
    }

    // The member's name in Life360: the tracker's friendly name without the "Life360 " prefix, whole (Life360 words it "Life360 Alden Smith").
    private static string? Life360FullName(HaEntitySnapshot snapshot)
    {
        var name = Text(snapshot, "friendly_name");
        if (name is not null && name.StartsWith("Life360 ", StringComparison.OrdinalIgnoreCase))
        {
            name = name["Life360 ".Length..].Trim();
        }

        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    private static string? FirstToken(string? text) =>
        text?.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();

    private static string Capitalize(string text) =>
        text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..].Replace('_', ' ');

    private static string ObjectId(string entityId)
    {
        var dot = entityId.IndexOf('.', StringComparison.Ordinal);
        return dot < 0 ? entityId : entityId[(dot + 1)..];
    }

    private static void AddIfPresent(HashSet<string> ids, string? id)
    {
        if (!string.IsNullOrWhiteSpace(id))
        {
            ids.Add(id);
        }
    }

    // ---- the watch list ------------------------------------------------------------------------------------------

    private static List<string> WatchListOf(
        List<ResolvedMember> members,
        List<ResolvedVehicle> vehicles,
        IReadOnlyList<HaEntitySnapshot> states,
        Dictionary<string, HaEntitySnapshot> byId)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var zone in states.Where(s => s.EntityId.StartsWith(ZonePrefix, StringComparison.Ordinal)))
        {
            ids.Add(zone.EntityId);
        }

        foreach (var member in members)
        {
            foreach (var id in new[] { member.PersonId, member.Life360TrackerId, member.CompanionTrackerId })
            {
                AddIfPresent(ids, id);
            }

            if (member.Sensors is { } sensors)
            {
                foreach (var id in new[] { sensors.BatteryLevel, sensors.BatteryState, sensors.Interactive, sensors.DeviceLocked, sensors.AndroidAuto })
                {
                    AddIfPresent(ids, id);
                }
            }
        }

        foreach (var vehicle in vehicles)
        {
            AddIfPresent(ids, vehicle.TrackerId);
        }

        return ids.Where(byId.ContainsKey).Order(StringComparer.Ordinal).ToList();
    }

    // ---- persons -------------------------------------------------------------------------------------------------

    private sealed record Person(string Id, string? UserId, IReadOnlyList<string> Trackers, string? Picture, string? FriendlyName);

    private static Person ParsePerson(HaEntitySnapshot snapshot)
    {
        var trackers = new List<string>();
        if (snapshot.Attributes.TryGetValue("device_trackers", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in list.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } id)
                {
                    trackers.Add(id);
                }
            }
        }

        return new Person(snapshot.EntityId, Text(snapshot, "user_id"), trackers, Text(snapshot, "entity_picture"), Text(snapshot, "friendly_name"));
    }

    private static string? Text(HaEntitySnapshot? snapshot, string key) =>
        snapshot is not null
        && snapshot.Attributes.TryGetValue(key, out var value)
        && value.ValueKind == JsonValueKind.String
        && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!.Trim()
            : null;
}
