using Realm.Domain;
using Realm.Web.Map;

namespace Realm.Web.Sheet;

/// <summary>
/// What every row of one list render shares (03 section 3.10): the session's clock and zone, the unit system, the <c>Ui:*</c> thresholds, who "me" is and where
/// (the origin of "far away" and of the distance on a row), and the lookups for zones and people. Built once per render by <see cref="Create"/> from the same
/// lists the map reads, so a row and its pin agree about the place, the status and "far".
/// </summary>
public sealed class RowFacts
{
    private readonly Dictionary<string, PlaceVm> _places = [];
    private readonly Dictionary<string, MemberVm> _members = [];

    private RowFacts(DateTimeOffset now, TimeZoneInfo zone, UnitSystem units, MapPayloadOptions options, string? meId, (double Lat, double Lon)? origin)
    {
        Now = now;
        Zone = zone;
        Units = units;
        Options = options;
        MeId = meId;
        Origin = origin;
    }

    /// <summary>The session's instant (never the wall clock).</summary>
    public DateTimeOffset Now { get; }

    /// <summary>The session's zone (America/Chicago in v1): every clock time in a row is shown in it, never in the browser's.</summary>
    public TimeZoneInfo Zone { get; }

    /// <summary>The unit system (imperial only in v1).</summary>
    public UnitSystem Units { get; }

    /// <summary>The thresholds: poor accuracy, low battery, far away.</summary>
    public MapPayloadOptions Options { get; }

    /// <summary>The viewer's member id (resolved: the first live member when none was asked for); null when nobody is live.</summary>
    public string? MeId { get; }

    /// <summary>My position, or the home zone when I have no fix; null when neither exists. Distances on a row are measured from here.</summary>
    public (double Lat, double Lon)? Origin { get; }

    /// <summary>
    /// Builds the context. <paramref name="meId"/> goes through <see cref="MapPayloadFactory.ResolveMeId"/>, as the map's does.
    /// </summary>
    public static RowFacts Create(
        IReadOnlyList<MemberVm> members,
        IReadOnlyList<PlaceVm> places,
        string? meId,
        DateTimeOffset now,
        TimeZoneInfo zone,
        MapPayloadOptions? options = null,
        UnitSystem units = UnitSystem.Imperial)
    {
        var me = MapPayloadFactory.ResolveMeId(members, meId);
        var context = new RowFacts(now, zone, units, options ?? MapPayloadOptions.Default, me, DefaultViewPlanner.Origin(members, places, me));
        foreach (var place in places)
        {
            context._places.TryAdd(place.Id, place);
        }

        foreach (var member in members)
        {
            context._members.TryAdd(member.Id, member);
        }

        return context;
    }

    /// <summary>The zone with this id, or null (no id, or a zone that is not in the list).</summary>
    public PlaceVm? PlaceOf(string? placeId) => placeId is not null && _places.TryGetValue(placeId, out var place) ? place : null;

    /// <summary>The person with this id, or null.</summary>
    public MemberVm? MemberOf(string memberId) => _members.GetValueOrDefault(memberId);
}
