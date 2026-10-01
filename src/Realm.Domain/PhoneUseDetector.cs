namespace Realm.Domain;

/// <summary>
/// Phone-use events of one trip (02 section 5.9, D20, D36): the screen in use while the car moves, leaving out
/// time while Android Auto is connected. Pure: the signals arrive as a parameter.
/// </summary>
public static class PhoneUseDetector
{
    // How far before the trip a screen reading may be for the screen state at the start to be known.
    private static readonly TimeSpan KnownStateMaxAge = TimeSpan.FromDays(7);

    // A segment between track fixes is only trusted to be moving when the fixes are this close in time.
    private const double MaxSegmentGapS = 120;

    /// <summary>
    /// Counts the phone-use events of a dense trip. The count is null (never 0) when the member is not
    /// phone-capable, the trip is coarse, the screen state before the trip is unknown (no reading in the 7 days
    /// before it, or an unavailable one), or the screen sensor was unavailable at any time during the trip. A
    /// locked sensor or Android Auto sensor that is absent, unknown or unavailable excludes nothing.
    /// </summary>
    /// <param name="signals">The transitions of the screen, lock and Android Auto sensors up to the trip's end (older ones may be passed; only the 7 days before the start matter for the screen). Any order.</param>
    /// <param name="phoneCapable">Whether the member has an Android companion with the screen sensor enabled.</param>
    public static PhoneUseResult Detect(
        DetectedTrip trip,
        IReadOnlyList<PhoneSignal> signals,
        bool phoneCapable,
        DrivingOptions? options = null)
    {
        var o = options ?? new DrivingOptions();
        var unknown = new PhoneUseResult(null, []);
        if (!phoneCapable || trip.Quality != TripQuality.Dense)
        {
            return unknown;
        }

        var ordered = signals.OrderBy(s => s.Ts).ToList();
        var screen = ordered.Where(s => s.Kind == PhoneSignalKind.Screen).ToList();
        var initialScreen = screen.LastOrDefault(s => s.Ts <= trip.StartUtc);
        if (initialScreen is null
            || trip.StartUtc - initialScreen.Ts > KnownStateMaxAge
            || initialScreen.IsOn is null
            || screen.Any(s => s.Ts > trip.StartUtc && s.Ts <= trip.EndUtc && s.IsOn is null))
        {
            return unknown;
        }

        var use = UseIntervals(trip, ordered, initialScreen);
        var moving = MovingIntervals(trip, o);

        // Intersect, in time order (both lists are ordered and disjoint).
        var pieces = new List<(DateTimeOffset Start, DateTimeOffset End)>();
        foreach (var m in moving)
        {
            foreach (var u in use)
            {
                var start = m.Start > u.Start ? m.Start : u.Start;
                var end = m.End < u.End ? m.End : u.End;
                if (end > start)
                {
                    pieces.Add((start, end));
                }
            }
        }

        pieces.Sort((a, b) => a.Start.CompareTo(b.Start));

        var events = new List<PhoneUseEvent>();
        var spanStart = default(DateTimeOffset);
        var spanEnd = default(DateTimeOffset);
        var seconds = 0.0;
        var open = false;
        foreach (var (start, end) in pieces)
        {
            if (open && (start - spanEnd).TotalSeconds < o.PhoneMergeGapS)
            {
                spanEnd = end > spanEnd ? end : spanEnd;
                seconds += (end - start).TotalSeconds;
                continue;
            }

            if (open && seconds >= o.PhoneMinS)
            {
                events.Add(new PhoneUseEvent(spanStart, spanEnd, seconds));
            }

            spanStart = start;
            spanEnd = end;
            seconds = (end - start).TotalSeconds;
            open = true;
        }

        if (open && seconds >= o.PhoneMinS)
        {
            events.Add(new PhoneUseEvent(spanStart, spanEnd, seconds));
        }

        return new PhoneUseResult(events.Count, events);
    }

    // The spans inside the trip where the screen is on, the phone is not locked and Android Auto is not on.
    private static List<(DateTimeOffset Start, DateTimeOffset End)> UseIntervals(
        DetectedTrip trip,
        List<PhoneSignal> ordered,
        PhoneSignal initialScreen)
    {
        // The state of each sensor at the start: the last reading at or before it (a null value excludes nothing).
        bool StateAtStart(PhoneSignalKind kind) =>
            ordered.LastOrDefault(s => s.Kind == kind && s.Ts <= trip.StartUtc)?.IsOn == true;

        var screenOn = initialScreen.IsOn == true;
        var locked = StateAtStart(PhoneSignalKind.Locked);
        var auto = StateAtStart(PhoneSignalKind.AndroidAuto);

        var intervals = new List<(DateTimeOffset Start, DateTimeOffset End)>();
        var cursor = trip.StartUtc;
        foreach (var change in ordered.Where(s => s.Ts > trip.StartUtc && s.Ts <= trip.EndUtc))
        {
            if (screenOn && !locked && !auto && change.Ts > cursor)
            {
                intervals.Add((cursor, change.Ts));
            }

            cursor = change.Ts;
            var on = change.IsOn == true;
            switch (change.Kind)
            {
                case PhoneSignalKind.Screen:
                    screenOn = on;
                    break;
                case PhoneSignalKind.Locked:
                    locked = on;
                    break;
                default:
                    auto = on;
                    break;
            }
        }

        if (screenOn && !locked && !auto && trip.EndUtc > cursor)
        {
            intervals.Add((cursor, trip.EndUtc));
        }

        return intervals;
    }

    // The segments between consecutive track fixes where both ends are at least the moving speed.
    private static List<(DateTimeOffset Start, DateTimeOffset End)> MovingIntervals(DetectedTrip trip, DrivingOptions options)
    {
        var intervals = new List<(DateTimeOffset Start, DateTimeOffset End)>();
        for (var i = 1; i < trip.Track.Count; i++)
        {
            var from = trip.Track[i - 1];
            var to = trip.Track[i];
            if ((to.Ts - from.Ts).TotalSeconds <= MaxSegmentGapS
                && Math.Min(from.SpeedMps ?? 0, to.SpeedMps ?? 0) >= options.PhoneMinMovingMps)
            {
                intervals.Add((from.Ts, to.Ts));
            }
        }

        return intervals;
    }
}
