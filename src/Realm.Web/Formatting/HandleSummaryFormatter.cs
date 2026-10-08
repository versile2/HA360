using Realm.Domain;

namespace Realm.Web.Formatting;

/// <summary>
/// The summary line in the sheet handle (01 section 8.2), the same at Peek and at 80 %: always the <b>section</b> summary, and it does not change
/// while something is selected. Pure: the instant is passed in (the session's own clock), so the 24 hour rule needs no wall clock.
/// </summary>
public static class HandleSummaryFormatter
{
    /// <summary>Before the first data snapshot.</summary>
    public const string Loading = "Summoning the court…";

    /// <summary>Drivers, when nobody has a live fix inside the last 24 hours.</summary>
    public const string Empty = "The Realm is empty";

    /// <summary>A live fix older than this no longer counts as "in the Realm".</summary>
    public static readonly TimeSpan LiveWindow = TimeSpan.FromHours(24);

    /// <summary>
    /// The summary of the section that is showing. A snapshot with no people, no vehicles and no places is the state before the first data arrives
    /// (a configured Realm always has at least one of them), so every section reads <see cref="Loading"/> then.
    /// </summary>
    public static string Format(
        Section section,
        IReadOnlyList<MemberVm> members,
        IReadOnlyList<VehicleVm> vehicles,
        IReadOnlyList<PlaceVm> places,
        DateTimeOffset now)
    {
        if (members.Count == 0 && vehicles.Count == 0 && places.Count == 0)
        {
            return Loading;
        }

        return section switch
        {
            Section.Vehicles => Vehicles(vehicles),
            Section.Places => Places(places),
            _ => Drivers(members, now),
        };
    }

    /// <summary>"{n} in the Realm" and " · {d} driving" when d &gt; 0; "The Realm is empty" when n is 0.</summary>
    /// <remarks>
    /// n counts people with a live fix inside the last 24 hours (the static prince is not counted, nor anyone with no fix); d counts the
    /// members who are driving and fresh. Freshness is the data layer's decision, never made here.
    /// </remarks>
    public static string Drivers(IReadOnlyList<MemberVm> members, DateTimeOffset now)
    {
        var live = members.Where(member => HasLiveFix(member, now)).ToList();
        if (live.Count == 0)
        {
            return Empty;
        }

        var driving = live.Count(member => member.IsDriving && member.Freshness == Freshness.Fresh);
        return driving > 0
            ? FormattableString.Invariant($"{live.Count} in the Realm · {driving} driving")
            : FormattableString.Invariant($"{live.Count} in the Realm");
    }

    /// <summary>"{n} vehicles" (singular "1 vehicle") and " · {k} on the road" when k &gt; 0, else " · all parked".</summary>
    /// <remarks>k counts <see cref="VehicleVm.IsMoving"/>; a vehicle with no data is parked.</remarks>
    public static string Vehicles(IReadOnlyList<VehicleVm> vehicles)
    {
        var moving = vehicles.Count(vehicle => vehicle.IsMoving);
        var count = vehicles.Count == 1 ? "1 vehicle" : FormattableString.Invariant($"{vehicles.Count} vehicles");
        return moving > 0 ? FormattableString.Invariant($"{count} · {moving} on the road") : $"{count} · all parked";
    }

    /// <summary>"{n} places" and " · {m} occupied" when m &gt; 0, else " · all quiet"; a place is occupied when a <b>person</b> is inside it, never because of a vehicle (01 section 5.3, R2-009).</summary>
    /// <remarks>
    /// 01 section 5.3: "A place is occupied when at least one person is inside it (PlaceVm.MemberIdsInside, which includes stale members, 02 §4.5); a vehicle never makes a place occupied."
    /// This summary counts the same set as the row's "n here", the occupied-first sort and the zone's occupied fill, so the line and the rows agree.
    /// </remarks>
    public static string Places(IReadOnlyList<PlaceVm> places)
    {
        var occupied = places.Count(place => place.MemberIdsInside.Count > 0);
        var count = places.Count == 1 ? "1 place" : FormattableString.Invariant($"{places.Count} places");
        return occupied > 0 ? FormattableString.Invariant($"{count} · {occupied} occupied") : $"{count} · all quiet";
    }

    private static bool HasLiveFix(MemberVm member, DateTimeOffset now) =>
        member.Kind == MemberKind.Live
        && member.Freshness is not (Freshness.NoFix or Freshness.Static)
        && member.LastUpdateUtc is { } updated
        && now - updated <= LiveWindow;
}
