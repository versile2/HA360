namespace Realm.Domain;

/// <summary>The speeding and phone-use parameters (02 sections 5.8 and 5.9), with their defaults.</summary>
/// <param name="SpeedingMps">The speeding threshold: the option driving_speeding_mph (80) converted with the one mph factor.</param>
/// <param name="SpeedingMinS">The least time between the first and last fix of a speeding episode.</param>
/// <param name="SpeedingRearmMps">An episode ends at a fix this far below the threshold.</param>
/// <param name="PhoneMinMovingMps">A segment is moving when both its fixes are at least this fast (10 mph).</param>
/// <param name="PhoneMergeGapS">Phone-use spans closer than this are one event.</param>
/// <param name="PhoneMinS">The least total screen-in-use time of an event: the option driving_phone_min_seconds.</param>
public record DrivingOptions(
    double SpeedingMps = 80 * FixParser.MphToMps,
    double SpeedingMinS = 30,
    double SpeedingRearmMps = 2.0,
    double PhoneMinMovingMps = 4.5,
    double PhoneMergeGapS = 20,
    double PhoneMinS = 10);
