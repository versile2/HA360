using Realm.Domain;
using Xunit;

namespace Realm.Demo.Tests;

// The Demo's Location History (0.3.0, D123): ten generated days of fixes and trips for the four live people, in the fictional geography, ending where the map shows them. The days are
// built by the production rules (HistoryRange), so a visit is named by its zone or its town and a drive has a from and a to. Fixture clock: Wed 2026-09-30 21:25 CDT.
public class DemoHistoryTests
{
    private static readonly DateOnly Today = new(2026, 9, 30);

    private static IRealmSession NewSession() => new DemoRealmSessionFactory().Create(null);

    [Theory]
    [InlineData("king")]
    [InlineData("queen")]
    [InlineData("jester")]
    [InlineData("cryptid")]
    public async Task Every_live_person_has_a_believable_week_of_visits_and_drives(string id)
    {
        var session = NewSession();

        var days = await session.GetHistoryRangeAsync(id, Today.AddDays(-6), Today, CancellationToken.None);

        Assert.Equal(7, days.Count);
        Assert.Equal(Enumerable.Range(0, 7).Select(back => Today.AddDays(-back)), days.Select(day => day.Day));
        Assert.All(days, day => Assert.True(day.Recorded));
        Assert.All(days, day => Assert.NotEmpty(day.Stays));
        Assert.True(days.Sum(day => day.DriveCount) >= 7);
        Assert.All(days.SelectMany(day => day.Entries), entry => Assert.True(entry.Duration > TimeSpan.Zero));
        Assert.All(days, day => Assert.Empty(day.Trail));
        await session.DisposeAsync();
    }

    [Fact]
    public async Task Aldens_day_is_home_work_and_home_again_with_the_zone_names()
    {
        var session = NewSession();

        var day = await session.GetHistoryDayAsync("king", new DateOnly(2026, 9, 29), CancellationToken.None);

        Assert.NotNull(day);
        Assert.Equal(["Hearth Haven", "Work", "Hearth Haven"], day.Stays.Select(stay => stay.Label));
        Assert.Equal(["Hearth Haven", "Work"], day.Drives.Select(drive => drive.FromLabel));
        Assert.Equal(["Work", "Hearth Haven"], day.Drives.Select(drive => drive.ToLabel));
        Assert.All(day.Drives, drive => Assert.True(drive.Meters > 1000));
        Assert.All(day.Stays, stay => Assert.DoesNotContain("nknown", stay.Label));
        Assert.NotEmpty(day.Trail);
        Assert.NotNull(day.Start);
        await session.DisposeAsync();
    }

    [Fact]
    public async Task Today_ends_where_the_map_shows_each_person_and_the_last_visit_is_still_going_on()
    {
        var session = NewSession();

        var king = await session.GetHistoryDayAsync("king", Today, CancellationToken.None);
        var jester = await session.GetHistoryDayAsync("jester", Today, CancellationToken.None);
        var cryptid = await session.GetHistoryDayAsync("cryptid", Today, CancellationToken.None);

        var atHome = king!.Stays.Last();
        Assert.Equal("Hearth Haven", atHome.Label);
        Assert.True(atHome.IsOngoing);
        Assert.Equal(session.Time.GetUtcNow(), atHome.EndUtc);
        Assert.Equal("The Jester's Hall", jester!.Stays.Last().Label);
        Assert.True(jester.Stays.Last().IsOngoing);
        // Dara is on a road trip, far from any zone: named by the town of her address, not by a zone.
        var far = cryptid!.Stays.Last();
        Assert.Null(far.PlaceId);
        Assert.Equal("near Fernhollow", far.Label);
        Assert.True(far.IsOngoing);
        await session.DisposeAsync();
    }

    [Fact]
    public async Task A_drive_has_a_path_a_distance_and_some_have_events()
    {
        var session = NewSession();

        var days = await session.GetHistoryRangeAsync("queen", Today.AddDays(-6), Today.AddDays(-1), CancellationToken.None);
        var drives = days.SelectMany(day => day.Drives).ToList();
        var withTrail = await session.GetHistoryDayAsync("queen", Today.AddDays(-1), CancellationToken.None);

        Assert.All(drives, drive => Assert.NotNull(drive.TopSpeedMps));
        Assert.Contains(drives, drive => drive.SpeedingCount > 0);
        Assert.All(withTrail!.Drives, drive => Assert.Contains(withTrail.Trail, segment => segment.EntryId == drive.Id));
        await session.DisposeAsync();
    }

    [Fact]
    public async Task Someone_who_is_not_tracked_a_static_pin_or_an_unknown_id_has_no_history()
    {
        var session = NewSession();

        Assert.Null(await session.GetHistoryDayAsync("prince", Today, CancellationToken.None));
        Assert.Null(await session.GetHistoryDayAsync("wagon", Today, CancellationToken.None));
        Assert.Null(await session.GetHistoryDayAsync("nobody", Today, CancellationToken.None));
        Assert.Empty(await session.GetHistoryRangeAsync("prince", Today.AddDays(-6), Today, CancellationToken.None));
        await session.DisposeAsync();
    }

    [Fact]
    public async Task A_day_with_no_generated_data_is_there_but_not_recorded_and_a_day_beyond_the_retention_is_the_oldest_kept()
    {
        var session = NewSession();

        var quiet = await session.GetHistoryDayAsync("king", new DateOnly(2026, 6, 1), CancellationToken.None);
        var tooOld = await session.GetHistoryDayAsync("king", new DateOnly(2020, 1, 1), CancellationToken.None);

        Assert.NotNull(quiet);
        Assert.False(quiet.Recorded);
        Assert.Empty(quiet.Entries);
        Assert.Equal(Today.AddDays(-400), tooOld!.Day);
        await session.DisposeAsync();
    }

    [Fact]
    public async Task The_same_day_is_the_same_every_time_and_ids_are_unique()
    {
        var first = await NewSession().GetHistoryDayAsync("jester", Today.AddDays(-2), CancellationToken.None);
        var second = await NewSession().GetHistoryDayAsync("jester", Today.AddDays(-2), CancellationToken.None);

        Assert.Equal(first!.Entries.Select(entry => (entry.Id, entry.StartUtc, entry.EndUtc)), second!.Entries.Select(entry => (entry.Id, entry.StartUtc, entry.EndUtc)));
        Assert.Equal(first.Entries.Count, first.Entries.Select(entry => entry.Id).Distinct().Count());
    }

    [Fact]
    public async Task Someone_moved_to_not_tracked_loses_their_history_in_this_session()
    {
        var session = NewSession();
        await session.Roster.MoveAsync("person.king", RosterGroup.NotTracked, null, CancellationToken.None);

        Assert.Null(await session.GetHistoryDayAsync("king", Today, CancellationToken.None));
        await session.DisposeAsync();
    }
}
