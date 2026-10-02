using Microsoft.Extensions.Logging;
using Realm.Domain;
using Realm.Infrastructure.Ha;
using Realm.Infrastructure.Stats;
using Xunit;

namespace Realm.Ingestion.Tests;

/// <summary>
/// 03 section 2.4 and 02 section 7.3: the live half of the trip life cycle. The pipeline's detector closes a trip, the recorder (which only queues it on the
/// pipeline's thread) hands it to the statistics service in order, and it is written after the flush that holds its last fix. Fixes stored by the live
/// feed give the same trips as a backfill of the same rows, a restart in the middle of a drive does not lose it, a trip that cannot be written does not stop
/// the next one, and what is queued at shutdown is written first. Real database, manual clock; every id is fictional.
/// </summary>
public sealed class TripRecorderTests
{
    private static readonly DateTimeOffset Tuesday = new(2026, 9, 29, 14, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Wednesday = new(2026, 9, 30, 14, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ALiveDrive_IsWrittenOnceItCloses_AndTheStatsVersionCountsUp()
    {
        await using var rig = await StoreRig.StartAsync(now: Tuesday.AddMinutes(-10));
        await rig.DiscoverAsync(King());
        using var recorder = rig.NewRecorder();
        await recorder.StartAsync(CancellationToken.None);
        var version = rig.State.Current.StatsVersion;

        await FeedAsync(rig, Drives.DriveRows(Tuesday));
        await WaitForTripsAsync(recorder, 1);
        await rig.Writer.FlushAsync();
        await recorder.StopAsync(CancellationToken.None);

        Assert.Equal(1, rig.Count("trips"));
        Assert.Equal(30, rig.Count("fixes"));
        Assert.Equal(version + 1, rig.State.Current.StatsVersion);
        Assert.Equal(Tuesday.ToUnixTimeMilliseconds(), rig.Long("SELECT start_ts FROM trips"));
        Assert.Equal(0, recorder.QueueDepth);
        Assert.False(recorder.Health.IsFaulted);
    }

    [Fact]
    public async Task FixesStoredByTheLiveFeed_GiveTheSameTripsAndDecisions_AsABackfillOfTheSameRows()
    {
        await using var live = await StoreRig.StartAsync(now: Tuesday.AddMinutes(-10));
        await live.DiscoverAsync(King());
        using var recorder = live.NewRecorder();
        await recorder.StartAsync(CancellationToken.None);
        await FeedAsync(live, [.. Drives.DriveRows(Tuesday), .. Drives.DriveRows(Wednesday)]);
        await WaitForTripsAsync(recorder, 2);
        await live.Writer.FlushAsync();
        await recorder.StopAsync(CancellationToken.None);

        await using var filled = await StoreRig.StartAsync();
        filled.History.Add(Drives.DriveRows(Tuesday));
        filled.History.Add(Drives.DriveRows(Wednesday));
        await filled.DiscoverAsync(King());
        await filled.NewBackfill().RunOnceAsync(CancellationToken.None);

        Assert.Equal(2, live.Count("trips"));
        Assert.Equal(live.Rows($"SELECT {StoreRig.TripColumns} FROM trips ORDER BY start_ts"), filled.Rows($"SELECT {StoreRig.TripColumns} FROM trips ORDER BY start_ts"));
        Assert.Equal(live.Rows($"SELECT {StoreRig.FixColumns} FROM fixes ORDER BY ts"), filled.Rows($"SELECT {StoreRig.FixColumns} FROM fixes ORDER BY ts"));
    }

    [Fact]
    public async Task ARestartInTheMiddleOfADrive_DoesNotLoseTheTrip()
    {
        var rows = Drives.DriveRows(Tuesday);
        var before = rows.Where(r => r.LastUpdatedUtc <= Tuesday.AddSeconds(120)).ToList();   // the lead-in and four fixes of driving
        var after = rows.Except(before).ToList();
        await using var rig = await StoreRig.StartAsync(now: Tuesday.AddMinutes(-10));
        await rig.DiscoverAsync(King());
        await FeedAsync(rig, before);
        await rig.Writer.FlushAsync();   // what the feed stored before the add-on went down

        rig.Restart();
        using var recorder = rig.NewRecorder();
        await recorder.StartAsync(CancellationToken.None);
        await rig.DiscoverAsync(King());   // the hydration replays the stored half hour: the drive is already open
        await FeedAsync(rig, after);
        await WaitForTripsAsync(recorder, 1);
        await recorder.StopAsync(CancellationToken.None);

        Assert.Equal(1, rig.Count("trips"));
        var trip = Drives.ClosedTrip(Tuesday);
        Assert.Equal(Tuesday.ToUnixTimeMilliseconds(), rig.Long("SELECT start_ts FROM trips"));   // the departure, not where the new process first saw the car moving
        Assert.Equal(trip.DistanceGpsM, rig.Double("SELECT distance_gps_m FROM trips"), 3);
    }

    [Fact]
    public async Task ATripThatCannotBeWritten_IsLoggedAndDropped_AndTheNextOneIsWritten()
    {
        await using var rig = await StoreRig.StartAsync(now: Tuesday.AddMinutes(-10));
        await rig.DiscoverAsync(King());
        var writer = new FailsOnce(rig.Writer);
        var stats = new StatsService(rig.State, rig.Discovery, rig.Notifier, rig.Queries, writer, rig.Options, rig.Time);
        var log = new RecordingLogger<TripRecorder>();
        using var recorder = new TripRecorder(rig.Pipeline, stats, rig.Time, log);
        await recorder.StartAsync(CancellationToken.None);

        await FeedAsync(rig, [.. Drives.DriveRows(Tuesday), .. Drives.DriveRows(Wednesday)]);
        await WaitForTripsAsync(recorder, 1);
        await recorder.StopAsync(CancellationToken.None);

        Assert.Equal(1, rig.Count("trips"));
        Assert.Equal(Wednesday.ToUnixTimeMilliseconds(), rig.Long("SELECT start_ts FROM trips"));
        var error = Assert.Single(log.Messages(LogLevel.Error));
        Assert.DoesNotContain("33.1", error, StringComparison.Ordinal);   // nothing that is logged carries a position
        Assert.DoesNotContain("84.7", error, StringComparison.Ordinal);
        Assert.False(recorder.Health.IsFaulted);
    }

    [Fact]
    public async Task WhatIsQueuedAtShutdown_IsWrittenFirst()
    {
        await using var rig = await StoreRig.StartAsync(now: Tuesday.AddMinutes(-10));
        await rig.DiscoverAsync(King());
        using var recorder = rig.NewRecorder();   // not started yet: the pipeline's thread only queues
        await FeedAsync(rig, [.. Drives.DriveRows(Tuesday), .. Drives.DriveRows(Wednesday)]);
        Assert.Equal(2, recorder.QueueDepth);
        Assert.Equal(0, rig.Count("trips"));

        await recorder.StartAsync(CancellationToken.None);
        Assert.True(SpinWait.SpinUntil(() => recorder.QueueDepth < 2, TimeSpan.FromSeconds(20)), "The loop did not start");   // it is demonstrably reading the queue
        await recorder.StopAsync(CancellationToken.None);   // the stop completes the queue and waits for the rest to be written

        Assert.Equal(2, rig.Count("trips"));
        Assert.Equal(2, recorder.RecordedCount);
        Assert.Equal(0, recorder.QueueDepth);
    }

    // Each row goes in as a state change at its own time: the clock is moved to it first, as the passing of time would have.
    private static async Task FeedAsync(StoreRig rig, IEnumerable<HaEntitySnapshot> rows)
    {
        foreach (var row in rows)
        {
            if (row.LastUpdatedUtc is { } at && at > rig.Time.GetUtcNow())
            {
                rig.Time.Advance(at - rig.Time.GetUtcNow());
            }

            await rig.FeedAsync(row);
        }
    }

    private static Task WaitForTripsAsync(TripRecorder recorder, int count)
    {
        Assert.True(SpinWait.SpinUntil(() => recorder.RecordedCount >= count, TimeSpan.FromSeconds(20)), $"Expected {count} trips written, saw {recorder.RecordedCount}");
        return Task.CompletedTask;
    }

    private static ResolvedMember King() => Plans.Member("king", life360: Plans.KingTracker);

    // The first trip it is asked to write fails, as a database that is briefly locked would; everything else goes to the real writer.
    private sealed class FailsOnce(IRealmWriter inner) : IRealmWriter
    {
        private int _failed;

        public bool EnqueueFix(string memberId, RawFix fix, bool inTrack = true, TrackReason? reason = null) => inner.EnqueueFix(memberId, fix, inTrack, reason);

        public bool EnqueueVehicleSample(VehicleSample sample) => inner.EnqueueVehicleSample(sample);

        public bool EnqueueSignal(string memberId, PhoneSignal signal) => inner.EnqueueSignal(memberId, signal);

        public bool EnqueueMeta(string key, string value) => inner.EnqueueMeta(key, value);

        public Task<bool> WriteTripAsync(string memberId, DetectedTrip trip, int algoVersion, string deriveHash, CancellationToken cancellationToken = default) =>
            Interlocked.Exchange(ref _failed, 1) == 0
                ? Task.FromException<bool>(new IOException("database is locked"))
                : inner.WriteTripAsync(memberId, trip, algoVersion, deriveHash, cancellationToken);

        public Task FlushAsync(CancellationToken cancellationToken = default) => inner.FlushAsync(cancellationToken);
    }
}
