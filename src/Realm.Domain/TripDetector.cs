namespace Realm.Domain;

/// <summary>
/// The trip detector of one member (02 section 5): the track filters and source priority, then the
/// Idle, Driving, Settling state machine, with speeding episodes and top speed computed when a trip closes.
/// One deterministic function of the fixes: live ingestion calls <see cref="Process"/> and <see cref="Tick"/>,
/// and backfill and cold-start replay call <see cref="Replay"/>, which is only a loop over the same two. Time
/// comes from the fixes and from the instants passed in, never from a clock. Not thread-safe.
/// </summary>
/// <remarks>
/// Phone use is not computed here (it needs the phone signals): apply <see cref="PhoneUseDetector"/> to a closed trip.
/// A trip that ends because no fix arrived for 600 s is held back until it can no longer merge with the next
/// trip (02 post-pass M2), so it is emitted up to 900 s after its end.
/// </remarks>
public sealed class TripDetector
{
    private const int RingSize = 5;
    private const int RecentSize = 8;
    private const int FeedingRanks = 3;
    private const int ConsistentRejectsToReanchor = 3;
    private const double DenseMaxGapS = 120;
    private const double MaxAccuracyM = 100;
    private const double MaxSpeedMps = 90;
    private const double GapFlagS = 180;
    private const double SpikeSpeedMismatch = 0.3;
    private const double StreetMaxDistanceM = 250;
    private const double StreetMaxAgeS = 300;

    private static readonly TimeSpan AddressKeep = TimeSpan.FromMinutes(15);

    private readonly TripOptions _trips;
    private readonly DrivingOptions _driving;

    // Track construction (02 section 5.1 and 5.2).
    private readonly List<TrackEntry> _recent = [];
    private readonly DateTimeOffset?[] _lastByRank = new DateTimeOffset?[FeedingRanks];
    private readonly List<RawFix> _rejects = [];
    private readonly List<AddressFix> _addresses = [];
    private bool _companionReportsSpeed;
    private DateTimeOffset? _lastAnyTs;
    private DateTimeOffset _clock = DateTimeOffset.MinValue;

    // The state machine (02 section 5.3).
    private readonly List<TrackEntry> _ring = [];
    private readonly List<TrackEntry> _run = [];
    private readonly List<OpenTrip> _pending = [];
    private TripState _state = TripState.Idle;
    private TrackEntry? _stop;
    private (double Lat, double Lon)? _anchor;
    private OpenTrip? _trip;
    private OpenTrip? _held;
    private OpenTrip? _coarse;
    private MachineSnapshot? _undo;

    /// <summary>Creates a detector with the default parameters of 02 section 5.3, 5.8 and 5.9 unless others are given.</summary>
    public TripDetector(TripOptions? trips = null, DrivingOptions? driving = null)
    {
        _trips = trips ?? new TripOptions();
        _driving = driving ?? new DrivingOptions();
    }

    /// <summary>The drawn zones, used to name trip endpoints and for the arrival shortcut. Replace it when the zones change.</summary>
    public IReadOnlyList<RawPlace> Zones { get; set; } = [];

    /// <summary>The state of the state machine.</summary>
    public TripState State => _state;

    /// <summary>The start of the open trip, null when the member is not in one.</summary>
    public DateTimeOffset? OpenSinceUtc => _trip?.StartUtc;

    /// <summary>
    /// Whether the member counts as driving now (02 section 5.4): in a trip, except that a stop of at least 45 s
    /// inside a drawn zone ends it at once. The Life360 driving flag never sets this by itself.
    /// </summary>
    public bool IsDriving(DateTimeOffset now) => _state switch
    {
        TripState.Driving => true,
        TripState.Settling => !ArrivalShortcut(now),
        _ => false,
    };

    /// <summary>Feeds one fix of any source, in arrival order. Time-driven closes up to the fix's own time happen first.</summary>
    public TripStep Process(RawFix fix)
    {
        var step = new StepAccumulator();
        ProcessInto(fix, step);
        return step.ToStep();
    }

    /// <summary>Advances time without a fix (the 30 s tick): closes a stop that lasted the merge time, a trip with no fix for too long, and held trips.</summary>
    public TripStep Tick(DateTimeOffset now)
    {
        var step = new StepAccumulator();
        AdvanceTo(now, step);
        return step.ToStep();
    }

    /// <summary>
    /// Replays fixes (stably ordered by time) and then advances to <paramref name="asOf"/>: the backfill and
    /// cold-start entry point. The detector stays usable afterwards, so live processing can continue from it.
    /// </summary>
    public TripStep Replay(IEnumerable<RawFix> fixes, DateTimeOffset asOf)
    {
        var step = new StepAccumulator();
        foreach (var fix in fixes.OrderBy(f => f.Ts))
        {
            ProcessInto(fix, step);
        }

        AdvanceTo(asOf, step);
        return step.ToStep();
    }

    private void ProcessInto(RawFix fix, StepAccumulator step)
    {
        AdvanceTo(fix.Ts, step);
        NoteAddress(fix);
        var decision = Consider(fix, step);
        step.Decisions.Add(decision);

        // "No fix at all" counts the fixes that enter the track and the ones a better source outranked, not the ones
        // rejected as inaccurate, as spikes or as out of order: a phone spraying useless fixes does not keep a trip open.
        if ((decision.InTrack || decision.Reason == TrackReason.Priority) && (_lastAnyTs is not { } last || fix.Ts > last))
        {
            _lastAnyTs = fix.Ts;
        }
    }

    // ---- time -------------------------------------------------------------------------------------------------

    private void AdvanceTo(DateTimeOffset t, StepAccumulator step)
    {
        if (t > _clock)
        {
            _clock = t;
        }

        var now = _clock;
        foreach (var done in _pending)
        {
            Emit(done, step);
        }

        _pending.Clear();

        if (_state == TripState.Settling && _stop is { } stop && (now - stop.Ts).TotalSeconds >= _trips.StopMergeS)
        {
            CloseAtStop(stop, step);
        }

        if (_state == TripState.Driving && _lastAnyTs is { } last && (now - last).TotalSeconds >= _trips.NoFixEndS)
        {
            CloseForNoFix(step);
        }

        if (_held is { } held && (now - held.EndUtc).TotalSeconds > _trips.GapMergeS)
        {
            _held = null;
            Emit(held, step);
        }

        if (_coarse is { } coarse && (now - coarse.EndUtc).TotalSeconds > _trips.SparseMaxGapS)
        {
            _coarse = null;
            Emit(coarse, step);
        }
    }

    private bool ArrivalShortcut(DateTimeOffset now)
    {
        if (_stop is not { } stop || _recent.Count == 0)
        {
            return false;
        }

        var latest = _recent[^1];
        return (now - stop.Ts).TotalSeconds >= _trips.ArrivalAnnounceS
            && latest.VEff < _trips.StopSpeedMps
            && PlaceResolver.Resolve(latest.Lat, latest.Lon, latest.Fix.AccuracyM, Zones, []).PlaceId is not null;
    }

    // ---- track construction (02 sections 5.1 and 5.2) -----------------------------------------------------------

    // The rank belongs to the source, not to the fix (02 section 5.2): the companion app of an Android phone reports a
    // speed and an iPhone's never does, so the companion ranks first once it has reported a speed at all and last
    // until then. One member has one phone, so one flag per detector is enough, and replay learns it like live does.
    private int Rank(RawFix fix) => fix.Source switch
    {
        FixSource.Companion => _companionReportsSpeed ? 0 : 2,
        FixSource.Life360 => 1,
        _ => FeedingRanks,                                      // a vehicle tracker never feeds a member's track
    };

    private FixDecision Consider(RawFix fix, StepAccumulator step)
    {
        if (fix.Source == FixSource.Companion && fix.SpeedMps is not null)
        {
            _companionReportsSpeed = true;
        }

        var rank = Rank(fix);
        var better = false;
        if (rank < FeedingRanks)
        {
            for (var r = 0; r < rank; r++)
            {
                if (_lastByRank[r] is { } seen && (fix.Ts - seen).TotalSeconds <= _trips.TrackFailoverS)
                {
                    better = true;
                }
            }
        }

        if (rank >= FeedingRanks || better)
        {
            return new FixDecision(fix, false, TrackReason.Priority);
        }

        var previous = _recent.Count > 0 ? _recent[^1] : null;
        if (previous is not null
            && (fix.Ts < previous.Ts || (fix.Ts == previous.Ts && fix.Lat == previous.Lat && fix.Lon == previous.Lon)))
        {
            return new FixDecision(fix, false, TrackReason.Dropped);
        }

        if (fix.AccuracyM > MaxAccuracyM)
        {
            return new FixDecision(fix, false, TrackReason.Accuracy);
        }

        var implied = ImpliedSpeed(previous, fix);
        var reanchor = false;
        if (implied is { } jump
            && jump > _trips.MaxPlausibleSpeedMps
            && (fix.SpeedMps is not { } reportedSpeed || Math.Abs(reportedSpeed - jump) > SpikeSpeedMismatch * jump))
        {
            var beforePrevious = _recent.Count > 1 ? _recent[^2] : null;
            if (beforePrevious is not null && IsPlausible(beforePrevious, fix))
            {
                // The earlier fix was the outlier (a duplicated position with a later timestamp): retract it.
                RetractLast(step);
                previous = beforePrevious;
                implied = ImpliedSpeed(previous, fix);
                _rejects.Clear();
            }
            else if (StaysRejected(fix))
            {
                return new FixDecision(fix, false, TrackReason.Spike);
            }
            else
            {
                // Three consistent rejects: the jump was real. Re-anchor on this fix.
                reanchor = true;
                implied = null;
                _recent.Clear();
            }
        }
        else
        {
            _rejects.Clear();
        }

        var speed = fix.SpeedMps ?? implied;
        if (speed > MaxSpeedMps)
        {
            speed = null;
        }

        var reported = fix.SpeedMps is { } usable && usable <= MaxSpeedMps ? usable : (double?)null;
        var entry = new TrackEntry(fix, speed, reported, reanchor);
        _recent.Add(entry);
        if (_recent.Count > RecentSize)
        {
            _recent.RemoveAt(0);
        }

        Push(entry);

        // Only a fix that entered the track silences the sources below its own.
        if (rank < FeedingRanks && (_lastByRank[rank] is not { } own || fix.Ts > own))
        {
            _lastByRank[rank] = fix.Ts;
        }

        return new FixDecision(fix, true, null);
    }

    private static double? ImpliedSpeed(TrackEntry? previous, RawFix fix)
    {
        if (previous is null)
        {
            return null;
        }

        var seconds = (fix.Ts - previous.Ts).TotalSeconds;
        return seconds > 0 && seconds <= DenseMaxGapS
            ? Geo.DistanceM(previous.Lat, previous.Lon, fix.Lat, fix.Lon) / seconds
            : null;
    }

    private bool IsPlausible(TrackEntry earlier, RawFix fix)
    {
        var seconds = (fix.Ts - earlier.Ts).TotalSeconds;
        return seconds > 0
            && Geo.DistanceM(earlier.Lat, earlier.Lon, fix.Lat, fix.Lon) / seconds <= _trips.MaxPlausibleSpeedMps;
    }

    // Remembers a spike reject. False when this is the third consecutive reject that is consistent with the
    // others: the jump was real, so the fix is accepted after all.
    private bool StaysRejected(RawFix fix)
    {
        if (_rejects.Count > 0 && !_rejects.All(r => Consistent(r, fix)))
        {
            _rejects.Clear();
        }

        _rejects.Add(fix);
        if (_rejects.Count < ConsistentRejectsToReanchor)
        {
            return true;
        }

        _rejects.Clear();
        return false;
    }

    private bool Consistent(RawFix a, RawFix b)
    {
        var seconds = Math.Abs((b.Ts - a.Ts).TotalSeconds);
        return seconds > 0 && Geo.DistanceM(a.Lat, a.Lon, b.Lat, b.Lon) / seconds <= _trips.MaxPlausibleSpeedMps;
    }

    private void RetractLast(StepAccumulator step)
    {
        var last = _recent[^1];
        _recent.RemoveAt(_recent.Count - 1);
        if (_undo is { } undo)
        {
            Restore(undo);
            _undo = null;
        }

        step.Retracted.Add(last.Fix);
    }

    // ---- the state machine (02 section 5.3) ---------------------------------------------------------------------

    private void Push(TrackEntry e)
    {
        _undo = Snapshot();
        switch (_state)
        {
            case TripState.Idle:
                PushIdle(e);
                break;
            case TripState.Driving:
                PushDriving(e);
                break;
            default:
                PushSettling(e);
                break;
        }
    }

    private void PushIdle(TrackEntry e)
    {
        if (e.Reanchored)
        {
            _ring.Clear();
            _run.Clear();
            _anchor = null;
        }

        HandleCoarse(_recent.Count >= 2 ? _recent[^2] : null, e);

        var fast = e.VEff >= _trips.StartSpeedMps;
        if (fast)
        {
            if (_run.Count == 0 || (e.Ts - _run[^1].Ts).TotalSeconds > _trips.StartMaxGapS)
            {
                AbsorbRun();
                _run.Add(e);
                _anchor = AnchorOf() ?? (e.Lat, e.Lon);
            }
            else
            {
                _run.Add(e);
            }

            if (_run.Count >= _trips.StartConsecutiveFixes
                && _anchor is { } home
                && Geo.DistanceM(e.Lat, e.Lon, home.Lat, home.Lon) >= _trips.StartMinDisplacementM)
            {
                StartTrip(_run[0], e);
                return;
            }
        }
        else
        {
            AbsorbRun();
        }

        // Rule B: the Life360 driving flag together with a long displacement from the home base.
        var flagged = e.Fix.Source == FixSource.Life360 && e.Fix.Driving == true;
        var flagAnchor = _run.Count > 0 ? _anchor : AnchorOf();
        if (flagged
            && flagAnchor is { } center
            && Geo.DistanceM(e.Lat, e.Lon, center.Lat, center.Lon) >= _trips.FlagAssistDisplacementM)
        {
            StartTrip(_run.Count > 0 ? _run[0] : e, e);
            return;
        }

        if (!fast)
        {
            AddToRing(e);
        }
    }

    private void PushDriving(TrackEntry e)
    {
        _trip!.Track.Add(e);
        if (e.VEff < _trips.StopSpeedMps)
        {
            _state = TripState.Settling;
            _stop = e;
        }
    }

    private void PushSettling(TrackEntry e)
    {
        _trip!.Track.Add(e);
        if (e.VEff >= _trips.ResumeSpeedMps
            && _stop is { } stop
            && Geo.DistanceM(e.Lat, e.Lon, stop.Lat, stop.Lon) >= _trips.ResumeDisplacementM)
        {
            _state = TripState.Driving;
            _stop = null;
        }
    }

    private void AddToRing(TrackEntry e)
    {
        _ring.Add(e);
        if (_ring.Count > RingSize)
        {
            _ring.RemoveAt(0);
        }
    }

    // A fast run that did not start a trip is part of where the member has been.
    private void AbsorbRun()
    {
        foreach (var e in _run)
        {
            AddToRing(e);
        }

        _run.Clear();
    }

    // The home base: the median position of the last 5 idle track fixes, or the last fix if there are fewer.
    private (double Lat, double Lon)? AnchorOf()
    {
        if (_ring.Count == 0)
        {
            return null;
        }

        if (_ring.Count < RingSize)
        {
            return (_ring[^1].Lat, _ring[^1].Lon);
        }

        return (Median(_ring.Select(x => x.Lat)), Median(_ring.Select(x => x.Lon)));
    }

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToList();
        var middle = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
    }

    // Starts a dense trip, back-dated to the departure fix: the last idle fix before the first fast fix that
    // lies within 60 m of the home base and at most 240 s before it.
    private void StartTrip(TrackEntry reference, TrackEntry current)
    {
        var anchor = _run.Count > 0 ? _anchor : AnchorOf();
        TrackEntry? departure = null;
        if (anchor is { } home)
        {
            departure = _ring.LastOrDefault(x =>
                x.Ts < reference.Ts
                && (reference.Ts - x.Ts).TotalSeconds <= _trips.DepartureLookbackS
                && Geo.DistanceM(x.Lat, x.Lon, home.Lat, home.Lon) <= _trips.DepartureSnapM);
        }

        var track = new List<TrackEntry>();
        if (departure is not null)
        {
            track.AddRange(_ring.Where(x => x.Ts >= departure.Ts));
        }

        if (_run.Count > 0)
        {
            track.AddRange(_run);
        }
        else
        {
            track.Add(current);
        }

        var trip = new OpenTrip(track, AddressesSince(track[0].Ts), TripQuality.Dense, TripEndedBy.Stop);
        if (_held is { } held)
        {
            // The held trip either merges into this one or can no longer merge with anything: it is emitted at the next call.
            if (CanMerge(held, trip))
            {
                trip = new OpenTrip([.. held.Track, .. trip.Track], [.. held.Addresses, .. trip.Addresses], TripQuality.Dense, TripEndedBy.Stop);
            }
            else
            {
                _pending.Add(held);
            }

            _held = null;
        }

        _trip = trip;
        _state = TripState.Driving;
        _stop = null;
        _ring.Clear();
        _run.Clear();
        _anchor = null;
    }

    // Post-pass M2: a trip that ended for lack of fixes while moving merges with the trip that starts within
    // 900 s, when the chord between them needs no more than 45 m/s.
    private bool CanMerge(OpenTrip held, OpenTrip next)
    {
        var seconds = (next.StartUtc - held.EndUtc).TotalSeconds;
        if (seconds <= 0 || seconds > _trips.GapMergeS || held.Track[^1].VEff < _trips.StartSpeedMps)
        {
            return false;
        }

        var chord = Geo.DistanceM(held.Track[^1].Lat, held.Track[^1].Lon, next.Track[0].Lat, next.Track[0].Lon);
        return chord / seconds <= _trips.GapMergeMaxSpeedMps;
    }

    // Coarse trips (02 section 5.6): consecutive idle fixes 120 to 900 s apart that move at least 1 km at 5 m/s.
    // The first fix after a dead zone is not one: the trip that went silent is held for the merge of 5.3 instead.
    private void HandleCoarse(TrackEntry? previous, TrackEntry e)
    {
        if (previous is not null && !(_held is { } held && held.Track[^1].Ts == previous.Ts))
        {
            var seconds = (e.Ts - previous.Ts).TotalSeconds;
            var metres = Geo.DistanceM(previous.Lat, previous.Lon, e.Lat, e.Lon);
            if (seconds > DenseMaxGapS
                && seconds <= _trips.SparseMaxGapS
                && metres >= _trips.SparseMinDistanceM
                && metres / seconds >= _trips.SparseMinSpeedMps)
            {
                if (_coarse is { } open)
                {
                    open.Track.Add(e);
                }
                else
                {
                    _coarse = new OpenTrip([previous, e], AddressesSince(previous.Ts), TripQuality.Coarse, TripEndedBy.Coarse);
                }

                return;
            }
        }

        if (_coarse is { } finished)
        {
            _coarse = null;
            _pending.Add(finished);
        }
    }

    // ---- closing ------------------------------------------------------------------------------------------------

    // SETTLING for the merge time: the trip ends at the first stopped fix.
    private void CloseAtStop(TrackEntry stop, StepAccumulator step)
    {
        var trip = _trip!;
        var closed = new OpenTrip(trip.Track.Where(x => x.Ts <= stop.Ts), trip.Addresses, TripQuality.Dense, TripEndedBy.Stop);
        var tail = trip.Track.Where(x => x.Ts >= stop.Ts).TakeLast(RingSize).ToList();

        _state = TripState.Idle;
        _trip = null;
        _stop = null;
        _undo = null;
        _run.Clear();
        _anchor = null;
        _ring.Clear();
        _ring.AddRange(tail);
        Emit(closed, step);
    }

    // No fix at all for too long while driving: the trip ends at its last fix and waits to see if it merges.
    private void CloseForNoFix(StepAccumulator step)
    {
        var trip = _trip!;
        if (_held is { } older)
        {
            _held = null;
            Emit(older, step);
        }

        _held = new OpenTrip(trip.Track, trip.Addresses, TripQuality.Dense, TripEndedBy.NoFix);
        _state = TripState.Idle;
        _trip = null;
        _stop = null;
        _undo = null;
        _run.Clear();
        _anchor = null;
        _ring.Clear();
    }

    private void Emit(OpenTrip trip, StepAccumulator step)
    {
        trip.Finalized = true;
        var detected = Build(trip);
        var valid = detected.DistanceGpsM >= _trips.MinDistanceM
            && (detected.EndUtc - detected.StartUtc).TotalSeconds >= _trips.MinDurationS
            && (detected.TopSpeedMps is null || detected.TopSpeedMps >= _trips.StartSpeedMps);
        (valid ? step.Closed : step.Discarded).Add(detected);
    }

    private DetectedTrip Build(OpenTrip trip)
    {
        var track = trip.Track;
        var points = track
            .Select(e => new TrackPoint(e.Ts, e.Lat, e.Lon, e.SpeedMps, e.ReportedSpeedMps))
            .ToList();

        var distance = 0.0;
        var hasGap = false;
        for (var i = 1; i < track.Count; i++)
        {
            distance += Geo.DistanceM(track[i - 1].Lat, track[i - 1].Lon, track[i].Lat, track[i].Lon);
            hasGap |= track[i].Reanchored || (track[i].Ts - track[i - 1].Ts).TotalSeconds > GapFlagS;
        }

        double? topSpeed = null;
        DateTimeOffset? topSpeedAt = null;
        string? topSpeedStreet = null;
        int? speedingCount = null;
        IReadOnlyList<SpeedingEpisode> episodes = [];
        if (trip.Quality == TripQuality.Dense)
        {
            var corroborated = TripSpeeds.Corroborated(points, _trips);
            if (TripSpeeds.TopSpeedIndex(corroborated) is { } top)
            {
                topSpeed = corroborated[top];
                topSpeedAt = points[top].Ts;
                topSpeedStreet = StreetNear(trip.Addresses, points[top].Ts, points[top].Lat, points[top].Lon);
            }

            episodes = TripSpeeds.SpeedingEpisodes(points, _driving, _trips);
            speedingCount = episodes.Count;
        }

        var first = track[0];
        var last = track[^1];
        var sources = track
            .Select(e => e.Fix.Source switch { FixSource.Life360 => "life360", FixSource.Companion => "companion", _ => "fordpass" })
            .Distinct()
            .Order(StringComparer.Ordinal);

        return new DetectedTrip(
            StartUtc: first.Ts,
            EndUtc: last.Ts,
            DurationS: (int)Math.Round((last.Ts - first.Ts).TotalSeconds, MidpointRounding.AwayFromZero),
            StartLat: first.Lat,
            StartLon: first.Lon,
            EndLat: last.Lat,
            EndLon: last.Lon,
            StartPlaceId: PlaceResolver.SnapEndpoint(first.Lat, first.Lon, Zones),
            EndPlaceId: PlaceResolver.SnapEndpoint(last.Lat, last.Lon, Zones),
            StartStreet: StreetNear(trip.Addresses, first.Ts, first.Lat, first.Lon),
            EndStreet: StreetNear(trip.Addresses, last.Ts, last.Lat, last.Lon),
            DistanceGpsM: distance,
            TopSpeedMps: topSpeed,
            TopSpeedAtUtc: topSpeedAt,
            TopSpeedStreet: topSpeedStreet,
            SpeedingCount: speedingCount,
            PhoneCount: null,
            Quality: trip.Quality,
            HasGap: hasGap,
            EndedBy: trip.EndedBy,
            SourceMask: string.Join(',', sources),
            Track: points,
            SpeedingEpisodes: episodes,
            PhoneEvents: []);
    }

    // ---- addresses ----------------------------------------------------------------------------------------------

    private void NoteAddress(RawFix fix)
    {
        if (fix.Source != FixSource.Life360 || AddressParser.Parse(fix.Address) is not { } parsed)
        {
            return;
        }

        var address = new AddressFix(fix.Ts, fix.Lat, fix.Lon, parsed.Street);
        _addresses.RemoveAll(a => fix.Ts - a.Ts > AddressKeep);
        _addresses.Add(address);
        _trip?.Addresses.Add(address);
        _coarse?.Addresses.Add(address);
    }

    private List<AddressFix> AddressesSince(DateTimeOffset start) =>
        _addresses.Where(a => (start - a.Ts).TotalSeconds <= StreetMaxAgeS).ToList();

    // The street of the Life360 address nearest in time within 250 m and 5 minutes of a point.
    private static string? StreetNear(List<AddressFix> addresses, DateTimeOffset ts, double lat, double lon) =>
        addresses
            .Where(a => Math.Abs((a.Ts - ts).TotalSeconds) <= StreetMaxAgeS
                && Geo.DistanceM(a.Lat, a.Lon, lat, lon) <= StreetMaxDistanceM)
            .OrderBy(a => Math.Abs((a.Ts - ts).TotalSeconds))
            .ThenBy(a => a.Ts)
            .Select(a => a.Street)
            .FirstOrDefault();

    // ---- undo for a spike retraction ----------------------------------------------------------------------------

    private MachineSnapshot Snapshot() => new(
        _state,
        _stop,
        [.. _ring],
        [.. _run],
        _anchor,
        _trip,
        _trip?.Track.Count ?? 0,
        _held,
        _coarse,
        _coarse?.Track.Count ?? 0);

    private void Restore(MachineSnapshot s)
    {
        _state = s.State;
        _stop = s.Stop;
        _ring.Clear();
        _ring.AddRange(s.Ring);
        _run.Clear();
        _run.AddRange(s.Run);
        _anchor = s.Anchor;

        _trip = s.Trip;
        if (s.Trip is not null)
        {
            s.Trip.Track.RemoveRange(s.TripTrackCount, s.Trip.Track.Count - s.TripTrackCount);
        }

        // A held or coarse trip that was merged, extended or queued by the undone fix is taken back, unless it has been emitted.
        _held = s.Held is { Finalized: false } ? s.Held : null;
        _coarse = s.Coarse is { Finalized: false } ? s.Coarse : null;
        if (_coarse is not null)
        {
            _coarse.Track.RemoveRange(s.CoarseTrackCount, _coarse.Track.Count - s.CoarseTrackCount);
        }

        _pending.RemoveAll(p => ReferenceEquals(p, s.Held) || ReferenceEquals(p, s.Coarse));
    }
}
