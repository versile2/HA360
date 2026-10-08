namespace Realm.Infrastructure.Options;

/// <summary>
/// The cross-field validation of 02 section 3.3 that needs nothing but the options. Since 0.2.0 it refuses one thing: an offline threshold that is not above
/// the stale one (both are fixed values now, so this guards a change of the defaults rather than anything the owner can set). Everything that used to be
/// checked here belonged to the removed members, vehicles and places options.
/// </summary>
public static class OptionsValidator
{
    public static OptionsValidation Validate(RealmOptions options)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        if (options.UiOfflineAfterHours * 60 <= options.UiStaleAfterMinutes)
        {
            errors.Add("ui_offline_after_hours: the offline threshold (hours x 60) must be greater than ui_stale_after_minutes");
        }

        return new OptionsValidation(errors, warnings);
    }
}
