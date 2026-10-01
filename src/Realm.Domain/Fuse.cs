namespace Realm.Domain;

/// <summary>
/// Position fusion for one person (02 section 4, D22): the freshest of the person's sources wins, and a fix
/// with an accuracy worse than 1 km is discarded while a better fix under 5 minutes old exists. Pure: the
/// clock and every threshold that is an option arrive as parameters.
/// </summary>
public static class Fuse
{
    // An unknown accuracy ranks as 100 m: between a good GPS fix and a poor one.
    private const double UnknownAccuracyM = 100;
    private const double DiscardAboveAccuracyM = 1000;
    private const double AddressMaxDistanceM = 250;

    private static readonly TimeSpan DiscardBetterFixMaxAge = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan TieWindow = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan BatteryMaxAge = TimeSpan.FromMinutes(60);
    private static readonly TimeSpan SpeedMaxAge = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan AddressMaxAge = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Fuses the latest accepted (already validated) fix of each of one person's sources.
    /// </summary>
    /// <param name="latestFixes">At most one fix per source.</param>
    /// <param name="now">The instant to age the fixes against.</param>
    /// <param name="offlineAfterHours">The option ui_offline_after_hours.</param>
    /// <returns>Null when there is no fix at all (NoFix).</returns>
    public static FusedPosition? Position(IReadOnlyCollection<RawFix> latestFixes, DateTimeOffset now, int offlineAfterHours)
    {
        if (latestFixes.Count == 0)
        {
            return null;
        }

        // Rule 1: a fix older than the offline limit stays a candidate only if nothing newer exists.
        var newest = latestFixes.Max(f => f.Ts);
        var offline = TimeSpan.FromHours(offlineAfterHours);
        var candidates = latestFixes.Where(f => now - f.Ts <= offline || f.Ts >= newest).ToList();

        // Rule 2 (D22): drop a fix worse than 1 km when a strictly better fix under 5 minutes old exists.
        var remaining = candidates
            .Where(f => !(EffectiveAccuracyM(f.AccuracyM) > DiscardAboveAccuracyM
                && candidates.Any(g => EffectiveAccuracyM(g.AccuracyM) < EffectiveAccuracyM(f.AccuracyM) && now - g.Ts < DiscardBetterFixMaxAge)))
            .ToList();

        // Rules 3 and 4: the greatest timestamp wins; fixes within 2 s of it tie, then the lower accuracy value, then companion before life360.
        var latest = remaining.Max(f => f.Ts);
        var winner = remaining
            .Where(f => latest - f.Ts <= TieWindow)
            .OrderBy(f => EffectiveAccuracyM(f.AccuracyM))
            .ThenBy(f => SourceRank(f.Source))
            .ThenByDescending(f => f.Ts)
            .First();

        var alts = remaining
            .Where(f => !ReferenceEquals(f, winner))
            .OrderByDescending(f => f.Ts)
            .ThenBy(f => EffectiveAccuracyM(f.AccuracyM))
            .ThenBy(f => SourceRank(f.Source))
            .ToList();

        var battery = ChooseBattery(latestFixes, now);

        return new FusedPosition(
            Lat: winner.Lat,
            Lon: winner.Lon,
            AccuracyM: winner.AccuracyM,
            Ts: winner.Ts,
            WinnerSource: winner.Source,
            SpeedMps: ChooseSpeed(latestFixes, winner, now),
            BatteryPct: battery?.Fix.BatteryPct,
            Charging: battery?.Fix.Charging,
            BatteryAsOfUtc: battery?.AsOf,
            Address: ChooseAddress(latestFixes, winner, now),
            Alts: alts);
    }

    /// <summary>A fix's accuracy for ranking: its own, or 100 m when unknown.</summary>
    internal static double EffectiveAccuracyM(double? accuracyM) => accuracyM ?? UnknownAccuracyM;

    // companion before life360 before the vehicle tracker
    private static int SourceRank(FixSource source) => source switch
    {
        FixSource.Companion => 0,
        FixSource.Life360 => 1,
        _ => 2,
    };

    // The newest non-null battery reading among the sources, no older than 60 minutes; a tie goes to the companion.
    private static (RawFix Fix, DateTimeOffset AsOf)? ChooseBattery(IReadOnlyCollection<RawFix> fixes, DateTimeOffset now)
    {
        (RawFix Fix, DateTimeOffset AsOf)? best = null;
        foreach (var fix in fixes.Where(f => f.BatteryPct is not null))
        {
            var asOf = fix.BatteryAsOfUtc ?? fix.Ts;
            if (now - asOf > BatteryMaxAge)
            {
                continue;
            }

            if (best is not { } current
                || asOf > current.AsOf
                || (asOf == current.AsOf && SourceRank(fix.Source) < SourceRank(current.Fix.Source)))
            {
                best = (fix, asOf);
            }
        }

        return best;
    }

    // The winner's reported speed, else the newest reported speed of any source; only a reading no older than 90 s counts.
    private static double? ChooseSpeed(IReadOnlyCollection<RawFix> fixes, RawFix winner, DateTimeOffset now)
    {
        if (winner.SpeedMps is { } winnerSpeed && now - winner.Ts <= SpeedMaxAge)
        {
            return winnerSpeed;
        }

        return fixes
            .Where(f => f.SpeedMps is not null && now - f.Ts <= SpeedMaxAge)
            .OrderByDescending(f => f.Ts)
            .ThenBy(f => SourceRank(f.Source))
            .FirstOrDefault()?.SpeedMps;
    }

    // The newest Life360 address within 250 m of the output position and no older than 30 minutes.
    private static string? ChooseAddress(IReadOnlyCollection<RawFix> fixes, RawFix winner, DateTimeOffset now)
    {
        return fixes
            .Where(f => f.Source == FixSource.Life360
                && !string.IsNullOrWhiteSpace(f.Address)
                && now - f.Ts <= AddressMaxAge
                && Geo.DistanceM(winner.Lat, winner.Lon, f.Lat, f.Lon) <= AddressMaxDistanceM)
            .OrderByDescending(f => f.Ts)
            .FirstOrDefault()?.Address;
    }
}
