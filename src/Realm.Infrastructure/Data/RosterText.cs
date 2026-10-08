using Realm.Domain;

namespace Realm.Infrastructure.Data;

/// <summary>The words of <see cref="RosterKind"/> and <see cref="RosterGroup"/> in the <c>roster</c> table (02 section 7.2): <c>person</c> or <c>tracker</c>; <c>people</c>, <c>vehicles</c> or <c>not_tracked</c>.</summary>
internal static class RosterText
{
    public static string Kind(RosterKind kind) => kind == RosterKind.Person ? "person" : "tracker";

    public static RosterKind ParseKind(string text) => text == "person" ? RosterKind.Person : RosterKind.Tracker;

    public static string Group(RosterGroup group) => group switch
    {
        RosterGroup.People => "people",
        RosterGroup.Vehicles => "vehicles",
        _ => "not_tracked",
    };

    public static RosterGroup ParseGroup(string text) => text switch
    {
        "people" => RosterGroup.People,
        "vehicles" => RosterGroup.Vehicles,
        _ => RosterGroup.NotTracked,
    };
}
