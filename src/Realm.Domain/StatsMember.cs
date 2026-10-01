namespace Realm.Domain;

/// <summary>A report driver as the weekly statistics rules need them.</summary>
/// <param name="DisplayName">Breaks ties when drivers are ordered.</param>
/// <param name="PhoneCapable">An Android companion with the screen sensor enabled: a null phone count means "not recorded" rather than "never available".</param>
/// <param name="RecordingStart">The earliest stored fix of the member (members.recording_start); null when nothing has been recorded.</param>
public record StatsMember(
    string MemberId,
    string DisplayName,
    bool PhoneCapable,
    DateTimeOffset? RecordingStart);
