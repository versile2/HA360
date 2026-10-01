using System.Globalization;
using Realm.Domain;

namespace Realm.Infrastructure.Options;

/// <summary>
/// The cross-field validation of 02 section 3.3 that needs nothing but the options. It refuses duplicate member or vehicle ids, a FordPass vehicle
/// without <c>entity_prefix</c> and an offline threshold that is not above the stale one; it warns about a static member without both coordinates and
/// about <c>me_fallback_member</c> naming no member. The checks that need Home Assistant (an unmatched <c>places[].zone</c>, an unknown entity id,
/// a person with several <c>mobile_app</c> trackers and no <c>companion_tracker</c>) belong to the discovery that has the entity list.
/// </summary>
public static class OptionsValidator
{
    private const string FordPass = "fordpass";

    public static OptionsValidation Validate(RealmOptions options)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        if (options.UiOfflineAfterHours * 60 <= options.UiStaleAfterMinutes)
        {
            errors.Add("ui_offline_after_hours: the offline threshold (hours x 60) must be greater than ui_stale_after_minutes");
        }

        var memberIds = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < options.Members.Count; i++)
        {
            var member = options.Members[i];
            if (!memberIds.Add(member.Id))
            {
                errors.Add(Indexed("members", i, "id") + ": the id is already used by an earlier member");
            }

            if (member.Kind == MemberKind.Static && (member.StaticLatitude is null || member.StaticLongitude is null))
            {
                warnings.Add(Indexed("members", i, "static_latitude") + ": a static member needs static_latitude and static_longitude; it shows as No fix");
            }
        }

        var vehicleIds = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < options.Vehicles.Count; i++)
        {
            var vehicle = options.Vehicles[i];
            if (!vehicleIds.Add(vehicle.Id))
            {
                errors.Add(Indexed("vehicles", i, "id") + ": the id is already used by an earlier vehicle");
            }

            if (string.Equals(vehicle.Integration, FordPass, StringComparison.Ordinal) && string.IsNullOrWhiteSpace(vehicle.EntityPrefix))
            {
                errors.Add(Indexed("vehicles", i, "entity_prefix") + ": required when integration is fordpass");
            }
        }

        if (options.MeFallbackMember.Length > 0 && !memberIds.Contains(options.MeFallbackMember))
        {
            warnings.Add("me_fallback_member: names no member");
        }

        return new OptionsValidation(errors, warnings);
    }

    private static string Indexed(string list, int index, string key)
    {
        return string.Create(CultureInfo.InvariantCulture, $"{list}[{index}].{key}");
    }
}
