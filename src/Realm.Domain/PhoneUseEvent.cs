namespace Realm.Domain;

/// <summary>One phone-use event (02 section 5.9): the trip_events row of kind phone.</summary>
/// <param name="StartUtc">The start of the first screen-in-use span while moving.</param>
/// <param name="EndUtc">The end of the last such span merged into this event.</param>
/// <param name="Seconds">The total screen-in-use time while moving inside the event.</param>
public record PhoneUseEvent(
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    double Seconds);
