using Realm.Domain;
using Xunit;

namespace Realm.Demo.Tests;

// "+ Add place" in the Demo (0.2.2, D119): the creation is simulated. The place joins the session's own list in memory, a refusal adds nothing, and nothing outside the session changes.
public class DemoPlaceEditorTests
{
    private static readonly NewZone Park = new("Dog Park", 31.1, -85.3, 150, "mdi:tree");

    [Fact]
    public async Task APlaceCreatedInTheDemo_JoinsThePlacesAndTheSessionChanges()
    {
        await using var session = new DemoRealmSessionFactory().Create(null);
        var before = session.Current.Places.Count;
        var changed = 0;
        session.Changed += () => changed++;

        var result = await session.PlaceEditor.CreateAsync(Park);

        Assert.True(result.Ok);
        var added = Assert.Single(session.Current.Places, place => place.DisplayName == "Dog Park");
        Assert.Equal(before + 1, session.Current.Places.Count);
        Assert.Equal(PlaceKind.Park, added.Kind);
        Assert.Equal(150, added.RadiusM);
        Assert.Equal(31.1, added.Lat);
        Assert.StartsWith("added_", added.Id, StringComparison.Ordinal);
        Assert.True(changed > 0);
    }

    [Fact]
    public async Task ARefusedPlace_AddsNothing()
    {
        await using var session = new DemoRealmSessionFactory().Create(null);
        var before = session.Current.Places.Count;

        var result = await session.PlaceEditor.CreateAsync(Park with { Name = " " });

        Assert.False(result.Ok);
        Assert.Equal(before, session.Current.Places.Count);
    }

    [Fact]
    public async Task EachSessionHasItsOwnPlaces()
    {
        await using var one = new DemoRealmSessionFactory().Create(null);
        await using var two = new DemoRealmSessionFactory().Create(null);

        await one.PlaceEditor.CreateAsync(Park);

        Assert.DoesNotContain(two.Current.Places, place => place.DisplayName == "Dog Park");
    }

    [Fact]
    public async Task TwoPlacesAddedInARow_GetDistinctIds()
    {
        await using var session = new DemoRealmSessionFactory().Create(null);

        await session.PlaceEditor.CreateAsync(Park);
        await session.PlaceEditor.CreateAsync(Park with { Name = "Dog Park 2" });

        var ids = session.Current.Places.Select(place => place.Id).ToArray();
        Assert.Equal(ids.Length, ids.Distinct().Count());
    }

    [Fact]
    public void TheDemoRoster_NamesTheHatchbackByItsCastId()
    {
        var roster = DemoRoster.EveryoneOnTheMap();
        var hatchback = roster.Entries.Single(entry => entry.EntityId == "device_tracker.hatchback");

        Assert.Equal(DemoCast.Chariot.Id, roster.SnapshotIdOf(hatchback));
        Assert.Equal(DemoCast.King.Id, roster.SnapshotIdOf(roster.Entries.Single(entry => entry.EntityId == "person.king")));
    }
}
