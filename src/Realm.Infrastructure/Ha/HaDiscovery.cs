using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Realm.Domain;
using Realm.Infrastructure.Avatars;
using Realm.Infrastructure.Options;

namespace Realm.Infrastructure.Ha;

/// <summary>
/// Discovery as a pure function (02 sections 1.2 and 2.3): the options, HA's configuration, the integration entity lists and the states in; the resolved
/// members, vehicles, zones and the websocket watch list out. No clock is read and nothing is logged: what is worth a warning comes back in
/// <see cref="HaDiscoveryResult.Warnings"/>, naming a member id or an option key and never a position.
/// </summary>
public static partial class HaDiscovery
{
    /// <summary>A person's companion tracker must have reported a position this recently for the person to become a member of their own (02 section 2.3 rule 3).</summary>
    public static readonly TimeSpan CompanionMaxAge = TimeSpan.FromDays(7);

    private const string Life360Prefix = "device_tracker.life360_";
    private const string PersonPrefix = "person.";
    private const string TrackerPrefix = "device_tracker.";
    private const string ZonePrefix = "zone.";

    // 01 section 7.5: the colours that members without an option colour take in turn.
    private static readonly string[] Palette = ["#E8BC4E", "#C792EA", "#5CC8FF", "#FF8FB1", "#7EE0A5", "#FFA657", "#A5B4FC", "#F9A8D4"];

    // 02 section 1.2 step 6 (the unused doorlock, doorstatus, battery and gps entities are not subscribed).
    private static readonly string[] VehicleSensors =
        ["odometer", "fuel", "ignitionstatus", "speed", "gearleverposition", "remotestartcountdown", "remotestartstatus", "lastrefresh"];

    /// <summary>
    /// Which states of <c>GET states</c> discovery needs: persons, zones and trackers, and every entity of the mobile_app and fordpass integrations
    /// (their sensors), plus the entities the options name by prefix, in case an integration list does not report them.
    /// </summary>
    public static Func<string, bool> StateFilter(RealmOptions options, HaIntegrationEntities integration)
    {
        var listed = new HashSet<string>(integration.MobileApp.Concat(integration.FordPass), StringComparer.Ordinal);
        var prefixes = new List<string>();
        foreach (var vehicle in options.Vehicles.Where(IsFordPass))
        {
            prefixes.Add($"sensor.{vehicle.EntityPrefix}_");
            prefixes.Add($"{TrackerPrefix}{vehicle.EntityPrefix}_");
        }

        foreach (var companion in options.Members.Select(m => m.CompanionTracker).Where(id => id is not null))
        {
            var stem = ObjectId(companion!);
            prefixes.Add($"sensor.{stem}_");
            prefixes.Add($"binary_sensor.{stem}_");
        }

        return id => id.StartsWith(PersonPrefix, StringComparison.Ordinal)
            || id.StartsWith(ZonePrefix, StringComparison.Ordinal)
            || id.StartsWith(TrackerPrefix, StringComparison.Ordinal)
            || listed.Contains(id)
            || prefixes.Any(prefix => id.StartsWith(prefix, StringComparison.Ordinal));
    }

    /// <summary>
    /// The entity ids the options name explicitly (persons, trackers, companion sensors, vehicle entities): the watch list to subscribe with before the first
    /// discovery has finished, so the first snapshot is not the whole of Home Assistant. Zones are not in it; the first discovery adds them.
    /// </summary>
    public static IReadOnlyList<string> SeedWatchList(RealmOptions options)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var member in options.Members)
        {
            AddIfPresent(ids, member.Person);
            AddIfPresent(ids, member.Life360Tracker);
            if (member.CompanionTracker is { } companion)
            {
                ids.Add(companion);
                foreach (var sensor in CompanionSensorIds(companion))
                {
                    ids.Add(sensor);
                }
            }
        }

        foreach (var vehicle in options.Vehicles.Where(IsFordPass))
        {
            ids.Add($"{TrackerPrefix}{vehicle.EntityPrefix}_tracker");
            foreach (var sensor in VehicleSensors)
            {
                ids.Add($"sensor.{vehicle.EntityPrefix}_{sensor}");
            }
        }

        return ids.Order(StringComparer.Ordinal).ToArray();
    }

    /// <summary>Resolves the members, vehicles, zones and watch list (02 sections 2.3, 2.4 and 1.2 step 8).</summary>
    /// <param name="states">The states <see cref="StateFilter"/> let through.</param>
    /// <param name="now">Only used for the seven-day rule of a person without Life360.</param>
    public static HaDiscoveryResult Resolve(
        RealmOptions options,
        HaConfig config,
        HaIntegrationEntities integration,
        IReadOnlyList<HaEntitySnapshot> states,
        DateTimeOffset now)
    {
        var byId = new Dictionary<string, HaEntitySnapshot>(StringComparer.Ordinal);
        foreach (var state in states)
        {
            byId[state.EntityId] = state;
        }

        var ignored = new HashSet<string>(options.IgnoreEntities, StringComparer.Ordinal);
        var life360 = new HashSet<string>(integration.Life360.Where(id => id.StartsWith(TrackerPrefix, StringComparison.Ordinal) && !ignored.Contains(id)), StringComparer.Ordinal);
        var mobileApp = new HashSet<string>(integration.MobileApp.Where(id => id.StartsWith(TrackerPrefix, StringComparison.Ordinal) && !ignored.Contains(id)), StringComparer.Ordinal);
        var persons = states
            .Where(s => s.EntityId.StartsWith(PersonPrefix, StringComparison.Ordinal))
            .Select(ParsePerson)
            .OrderBy(p => p.Id, StringComparer.Ordinal)
            .ToList();

        var warnings = new List<string>();
        var members = new List<ResolvedMember>();
        var claimedTrackers = new HashSet<string>(StringComparer.Ordinal);
        var claimedPersons = new HashSet<string>(StringComparer.Ordinal);
        var colour = 0;

        string NextColour() => Palette[colour++ % Palette.Length];

        // 1. Configured members, in the order of the options.
        for (var i = 0; i < options.Members.Count; i++)
        {
            var option = options.Members[i];
            var member = option.Kind == MemberKind.Static
                ? ResolveStatic(option, i, persons, byId, ignored, warnings, NextColour)
                : ResolveLive(option, i, persons, byId, life360, mobileApp, ignored, warnings, NextColour);
            members.Add(member);
            ClaimSources(member, claimedTrackers, claimedPersons);
        }

        // 2. A Life360 tracker no options entry claims is a member of its own (02 section 2.3 rule 2): everyone in Life360 shows up.
        var auto = 0;
        foreach (var trackerId in life360.Where(id => !claimedTrackers.Contains(id)).Order(StringComparer.Ordinal))
        {
            var person = persons.FirstOrDefault(p => p.Trackers.Contains(trackerId, StringComparer.Ordinal));
            var companion = person is null ? null : ChooseCompanion(person, mobileApp, byId, ignored, null, warnings);
            var snapshot = byId.GetValueOrDefault(trackerId);
            var id = AutoLife360Id(trackerId, snapshot);
            var personPicture = person?.Picture;
            var member = new ResolvedMember(
                Id: id,
                DisplayName: Life360FirstName(trackerId, snapshot),
                LoreTitle: null,
                Kind: MemberKind.Live,
                Color: NextColour(),
                SortOrder: options.Members.Count + auto++,
                InDrivingReport: true,
                PersonId: person?.Id,
                UserId: person?.UserId,
                Life360TrackerId: trackerId,
                CompanionTrackerId: companion,
                Sensors: SensorsOf(companion, byId),
                PhoneCapable: false,
                AvatarUpstream: ChooseAvatar("auto", personPicture, Text(snapshot, "entity_picture")),
                StaticLabel: null,
                StaticAddress: null,
                StaticLat: null,
                StaticLon: null,
                StaticShowAddress: false);
            members.Add(WithPhoneCapable(member));
            ClaimSources(member, claimedTrackers, claimedPersons);
        }

        // 3. A person without a Life360 tracker joins only with a companion tracker that reported lately (02 section 2.3 rule 3).
        foreach (var person in persons.Where(p => !claimedPersons.Contains(p.Id) && !p.Trackers.Any(life360.Contains)))
        {
            var companion = ChooseCompanion(person, mobileApp, byId, ignored, null, warnings: null, requireRecent: now - CompanionMaxAge);
            if (companion is null || claimedTrackers.Contains(companion))
            {
                continue;
            }

            var member = new ResolvedMember(
                Id: "ha_" + Hash8(person.Id),
                DisplayName: FirstToken(person.FriendlyName) ?? Capitalize(ObjectId(person.Id)),
                LoreTitle: null,
                Kind: MemberKind.Live,
                Color: NextColour(),
                SortOrder: options.Members.Count + auto++,
                InDrivingReport: true,
                PersonId: person.Id,
                UserId: person.UserId,
                Life360TrackerId: null,
                CompanionTrackerId: companion,
                Sensors: SensorsOf(companion, byId),
                PhoneCapable: false,
                AvatarUpstream: ChooseAvatar("auto", person.Picture, null),
                StaticLabel: null,
                StaticAddress: null,
                StaticLat: null,
                StaticLon: null,
                StaticShowAddress: false);
            members.Add(WithPhoneCapable(member));
            ClaimSources(member, claimedTrackers, claimedPersons);
        }

        var vehicles = ResolveVehicles(options, byId);
        var zones = states
            .Select(FixParser.ParseZone)
            .OfType<RawPlace>()
            .OrderBy(z => z.Id, StringComparer.Ordinal)
            .ToList();

        // An unmatched places[].zone is logged once and ignored (02 section 3.2): an entity id wins over an exact trimmed zone name.
        for (var i = 0; i < options.Places.Count; i++)
        {
            if (MatchZone(options.Places[i].Zone, zones) is null)
            {
                warnings.Add($"places[{i.ToString(CultureInfo.InvariantCulture)}].zone: no such zone in Home Assistant; the entry is ignored");
            }
        }

        return new HaDiscoveryResult(config.TimeZone, config.Version, members, vehicles, zones, WatchListOf(members, vehicles, states, byId), warnings);
    }

    /// <summary>The zone a <c>places[].zone</c> value names: its entity id (with the <c>zone.</c> prefix), else the one whose trimmed name matches exactly (case-sensitive). Null when none.</summary>
    public static RawPlace? MatchZone(string zone, IReadOnlyList<RawPlace> zones)
    {
        var value = zone.Trim();
        return zones.FirstOrDefault(z => ZonePrefix + z.Id == value)
            ?? zones.OrderBy(z => z.Id, StringComparer.Ordinal).FirstOrDefault(z => z.Name.Trim() == value);
    }

    // ---- members ------------------------------------------------------------------------------------------------

    private static ResolvedMember ResolveLive(
        MemberOption option,
        int index,
        List<Person> persons,
        Dictionary<string, HaEntitySnapshot> byId,
        HashSet<string> life360,
        HashSet<string> mobileApp,
        HashSet<string> ignored,
        List<string> warnings,
        Func<string> nextColour)
    {
        var trackerId = Explicit(option.Life360Tracker, byId, ignored, warnings, option.Id, "life360_tracker");
        var person = Explicit(option.Person, byId, ignored, warnings, option.Id, "person") is { } explicitPerson
            ? persons.FirstOrDefault(p => p.Id == explicitPerson)
            : trackerId is null ? null : persons.FirstOrDefault(p => p.Trackers.Contains(trackerId, StringComparer.Ordinal));
        trackerId ??= person?.Trackers.FirstOrDefault(life360.Contains);

        var companion = Explicit(option.CompanionTracker, byId, ignored, warnings, option.Id, "companion_tracker")
            ?? (person is null ? null : ChooseCompanion(person, mobileApp, byId, ignored, option.Id, warnings));

        var snapshot = trackerId is null ? null : byId.GetValueOrDefault(trackerId);
        var member = new ResolvedMember(
            Id: option.Id,
            DisplayName: string.IsNullOrWhiteSpace(option.DisplayName) ? Life360FirstName(trackerId ?? option.Id, snapshot) : option.DisplayName,
            LoreTitle: option.LoreTitle,
            Kind: MemberKind.Live,
            Color: option.Color ?? nextColour(),
            SortOrder: option.SortOrder ?? index,
            InDrivingReport: option.InDrivingReport,
            PersonId: person?.Id,
            UserId: person?.UserId,
            Life360TrackerId: trackerId,
            CompanionTrackerId: companion,
            Sensors: SensorsOf(companion, byId),
            PhoneCapable: false,
            AvatarUpstream: ChooseAvatar(option.Avatar, person?.Picture, Text(snapshot, "entity_picture")),
            StaticLabel: null,
            StaticAddress: null,
            StaticLat: null,
            StaticLon: null,
            StaticShowAddress: false);
        return WithPhoneCapable(member);
    }

    private static ResolvedMember ResolveStatic(
        MemberOption option,
        int index,
        List<Person> persons,
        Dictionary<string, HaEntitySnapshot> byId,
        HashSet<string> ignored,
        List<string> warnings,
        Func<string> nextColour)
    {
        var personId = Explicit(option.Person, byId, ignored, warnings, option.Id, "person");
        var person = personId is null ? null : persons.FirstOrDefault(p => p.Id == personId);
        return new ResolvedMember(
            Id: option.Id,
            DisplayName: option.DisplayName,
            LoreTitle: option.LoreTitle,
            Kind: MemberKind.Static,
            Color: option.Color ?? nextColour(),
            SortOrder: option.SortOrder ?? index,
            InDrivingReport: false,
            PersonId: person?.Id,
            UserId: person?.UserId,
            Life360TrackerId: null,
            CompanionTrackerId: null,
            Sensors: null,
            PhoneCapable: false,
            AvatarUpstream: ChooseAvatar(option.Avatar, person?.Picture, null),
            StaticLabel: option.StaticLabel,
            StaticAddress: option.StaticAddress,
            StaticLat: option.StaticLatitude,
            StaticLon: option.StaticLongitude,
            StaticShowAddress: option.StaticShowAddress);
    }

    // An explicit entity id of an options entry: used when it exists in HA and is not ignored; an id HA does not know is skipped with a warning.
    private static string? Explicit(string? entityId, Dictionary<string, HaEntitySnapshot> byId, HashSet<string> ignored, List<string> warnings, string memberId, string key)
    {
        if (string.IsNullOrWhiteSpace(entityId) || ignored.Contains(entityId))
        {
            return null;
        }

        if (byId.ContainsKey(entityId))
        {
            return entityId;
        }

        warnings.Add($"member '{memberId}': {key} names an entity that Home Assistant does not have; that source is skipped");
        return null;
    }

    // A person's one mobile_app tracker (02 section 2.3 rule 1). Two or more need companion_tracker (R-065): one warning, no companion source, and nothing is
    // picked by recency, which would flip the source silently after a restart.
    private static string? ChooseCompanion(
        Person person,
        HashSet<string> mobileApp,
        Dictionary<string, HaEntitySnapshot> byId,
        HashSet<string> ignored,
        string? memberId,
        List<string>? warnings,
        DateTimeOffset? requireRecent = null)
    {
        var candidates = person.Trackers
            .Where(id => mobileApp.Contains(id) && !ignored.Contains(id) && byId.TryGetValue(id, out var snapshot) && IsGps(snapshot))
            .Where(id => requireRecent is not { } since || ReportedSince(byId[id], since))
            .ToList();
        if (candidates.Count > 1)
        {
            warnings?.Add($"member '{memberId}': the person owns {candidates.Count} companion trackers; set companion_tracker, no companion source is used");
        }

        return candidates.Count == 1 ? candidates[0] : null;
    }

    private static bool IsGps(HaEntitySnapshot snapshot) =>
        Text(snapshot, "source_type") is not { } type || string.Equals(type, "gps", StringComparison.OrdinalIgnoreCase);

    private static bool ReportedSince(HaEntitySnapshot snapshot, DateTimeOffset since) =>
        snapshot.Attributes.TryGetValue("latitude", out var lat) && lat.ValueKind == JsonValueKind.Number
        && snapshot.Attributes.TryGetValue("longitude", out var lon) && lon.ValueKind == JsonValueKind.Number
        && snapshot.LastUpdatedUtc >= since;

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

    private static ResolvedMember WithPhoneCapable(ResolvedMember member) =>
        member with { PhoneCapable = member.Sensors?.Interactive is not null };

    private static void ClaimSources(ResolvedMember member, HashSet<string> trackers, HashSet<string> persons)
    {
        if (member.Life360TrackerId is not null)
        {
            trackers.Add(member.Life360TrackerId);
        }

        if (member.CompanionTrackerId is not null)
        {
            trackers.Add(member.CompanionTrackerId);
        }

        if (member.PersonId is not null)
        {
            persons.Add(member.PersonId);
        }
    }

    // 02 section 2.6: auto takes the person's picture, then the Life360 tracker's; person and life360 take that one only; none takes none.
    private static string? ChooseAvatar(string mode, string? personPicture, string? life360Picture)
    {
        string? Servable(string? picture) => AvatarUpstream.IsServable(picture) ? picture!.Trim() : null;
        return mode switch
        {
            "none" => null,
            "person" => Servable(personPicture),
            "life360" => Servable(life360Picture),
            _ => Servable(personPicture) ?? Servable(life360Picture),
        };
    }

    // ---- ids and names ------------------------------------------------------------------------------------------

    // l360_<first 8 hex of the Life360 member uuid>, the path segment after user_images/ of the tracker's picture; else the first 8 hex of the SHA-256 of the entity id.
    private static string AutoLife360Id(string trackerId, HaEntitySnapshot? snapshot)
    {
        var match = Text(snapshot, "entity_picture") is { } picture ? UserImages().Match(picture) : Match.Empty;
        return "l360_" + (match.Success ? match.Groups[1].Value[..8].ToLowerInvariant() : Hash8(trackerId));
    }

    private static string Hash8(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..8].ToLowerInvariant();

    // 02 section 1.5: the Life360 first name, from the tracker's friendly name without its "Life360 " prefix.
    private static string Life360FirstName(string trackerOrMemberId, HaEntitySnapshot? snapshot)
    {
        var name = Text(snapshot, "friendly_name");
        if (name is not null && name.StartsWith("Life360 ", StringComparison.OrdinalIgnoreCase))
        {
            name = name["Life360 ".Length..];
        }

        var stem = ObjectId(trackerOrMemberId);
        return FirstToken(name) ?? Capitalize(stem.StartsWith("life360_", StringComparison.Ordinal) ? stem["life360_".Length..] : stem);
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

    private static IEnumerable<string> CompanionSensorIds(string companionTracker)
    {
        var stem = ObjectId(companionTracker);
        yield return $"sensor.{stem}_battery_level";
        yield return $"sensor.{stem}_battery_state";
        yield return $"binary_sensor.{stem}_interactive";
        yield return $"binary_sensor.{stem}_device_locked";
        yield return $"binary_sensor.{stem}_android_auto";
    }

    private static void AddIfPresent(HashSet<string> ids, string? id)
    {
        if (!string.IsNullOrWhiteSpace(id))
        {
            ids.Add(id);
        }
    }

    [GeneratedRegex("user_images/([0-9a-fA-F][0-9a-fA-F-]{7,})", RegexOptions.CultureInvariant)]
    private static partial Regex UserImages();

    // ---- vehicles and the watch list -----------------------------------------------------------------------------

    private static bool IsFordPass(VehicleOption vehicle) =>
        string.Equals(vehicle.Integration, "fordpass", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(vehicle.EntityPrefix);

    private static List<ResolvedVehicle> ResolveVehicles(RealmOptions options, Dictionary<string, HaEntitySnapshot> byId)
    {
        var vehicles = new List<ResolvedVehicle>();
        for (var i = 0; i < options.Vehicles.Count; i++)
        {
            var option = options.Vehicles[i];
            var fordPass = IsFordPass(option);
            var trackerId = fordPass ? $"{TrackerPrefix}{option.EntityPrefix}_tracker" : null;
            vehicles.Add(new ResolvedVehicle(
                Id: option.Id,
                Name: option.Name,
                LoreTitle: option.LoreTitle,
                Glyph: option.Glyph,
                IsPlaceholder: !fordPass,
                PlaceholderNote: fordPass ? null : option.PlaceholderNote,
                SortOrder: option.SortOrder ?? i,
                Prefix: fordPass ? option.EntityPrefix : null,
                TrackerId: trackerId is not null && byId.ContainsKey(trackerId) ? trackerId : null,
                SensorIds: fordPass
                    ? VehicleSensors.Select(name => $"sensor.{option.EntityPrefix}_{name}").Where(byId.ContainsKey).ToArray()
                    : []));
        }

        return vehicles;
    }

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
            foreach (var id in vehicle.SensorIds)
            {
                ids.Add(id);
            }
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
