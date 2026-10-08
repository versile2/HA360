using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Realm.Demo;
using Realm.Domain;
using Realm.Web.Components.Shell;
using Xunit;

namespace Realm.Web.Tests;

/// <summary>
/// Settings, "Who's on the map" (01 section 7.9, D113), rendered from the Demo session whose roster is four people, the wagon and two entries under Not tracked: the three groups and their
/// counts, the "kind · source" line, the ⋮ "Move to..." menu (what it offers, that Esc closes it), a move by the menu and by the drag and drop events, the editor (name, title, colour; Save and
/// Cancel) and the roster that a session without one lists. The drag itself and the focus that follows a move are the browser's (roster.js); the Playwright spec covers them.
/// </summary>
public sealed class RosterSectionTests : ComponentTestBase
{
    private const string King = "person.king";
    private const string Prince = "person.prince";
    private const string Hatchback = "device_tracker.hatchback";

    [Fact]
    public async Task TheThreeGroups_ListTheDemoRoster_WithTheirCounts()
    {
        await using var session = new DemoRealmSessionFactory().Create(null);

        var cut = Open(session);

        Assert.Equal(["PEOPLE4", "TRACKERS1", "NOTTRACKED2"], cut.FindAll(".realm-roster__heading").Select(heading => System.Text.RegularExpressions.Regex.Replace(heading.TextContent, "\\s+", string.Empty)));
        Assert.Equal("4", cut.Find("[data-testid='roster-count-people']").TextContent);
        Assert.Equal("1", cut.Find("[data-testid='roster-count-vehicles']").TextContent);
        Assert.Equal("2", cut.Find("[data-testid='roster-count-not-tracked']").TextContent);
        Assert.Equal(
            ["person-king", "person-queen", "person-jester", "person-cryptid"],
            Rows(cut, "people"));
        Assert.Equal(["device-tracker-wagon"], Rows(cut, "vehicles"));
        Assert.Equal(["person-prince", "device-tracker-hatchback"], Rows(cut, "not-tracked"));
    }

    [Fact]
    public async Task EachRow_ReadsKindAndSource_AndARowMovedByTheRulesSaysSo()
    {
        await using var session = new DemoRealmSessionFactory().Create(null);

        var cut = Open(session);

        Assert.Equal("Person · Home Assistant + Life360", cut.Find("[data-testid='roster-meta-person-king']").TextContent);
        Assert.Equal("Person · Life360", cut.Find("[data-testid='roster-meta-person-jester']").TextContent);
        Assert.Equal("Tracker · Home Assistant", cut.Find("[data-testid='roster-meta-device-tracker-wagon']").TextContent);
        Assert.Equal("Person · Home Assistant · moved automatically", cut.Find("[data-testid='roster-meta-person-prince']").TextContent);
        Assert.Equal("Tracker · Home Assistant", cut.Find("[data-testid='roster-meta-device-tracker-hatchback']").TextContent);
        Assert.Equal(DemoCast.King.Name, cut.Find("[data-testid='roster-row-person-king'] .realm-roster__name > span").TextContent);
    }

    [Fact]
    public async Task TheMoreButton_OffersTheOtherGroups_AndMoveDownOnlyWhereThereIsARowBelow()
    {
        await using var session = new DemoRealmSessionFactory().Create(null);
        var cut = Open(session);

        var more = cut.Find("[data-testid='roster-more-person-king']");
        Assert.Equal("false", more.GetAttribute("aria-expanded"));
        Assert.Equal($"Move {DemoCast.King.Name} to…", more.GetAttribute("aria-label"));
        await more.TriggerEventAsync("onclick", new MouseEventArgs());

        Assert.Equal("true", cut.Find("[data-testid='roster-more-person-king']").GetAttribute("aria-expanded"));
        Assert.Equal(["Move to Trackers", "Move to Not tracked", "Move down"], MenuItems(cut));
        Assert.Single(cut.FindAll("[data-testid='roster-menu']"));
    }

    [Fact]
    public async Task TheMenu_OfTheLastRowOfAGroup_OffersMoveUpButNotMoveDown()
    {
        await using var session = new DemoRealmSessionFactory().Create(null);
        var cut = Open(session);

        await cut.Find("[data-testid='roster-more-person-cryptid']").TriggerEventAsync("onclick", new MouseEventArgs());

        Assert.Equal(["Move to Trackers", "Move to Not tracked", "Move up"], MenuItems(cut));
    }

    [Fact]
    public async Task Escape_ClosesTheMenu_AndOnlyTheMenu()
    {
        await using var session = new DemoRealmSessionFactory().Create(null);
        var cut = Open(session);
        await cut.Find("[data-testid='roster-more-person-king']").TriggerEventAsync("onclick", new MouseEventArgs());

        await cut.Find("[data-testid='roster-menu']").TriggerEventAsync("onkeydown", new KeyboardEventArgs { Key = "Escape" });

        Assert.Empty(cut.FindAll("[data-testid='roster-menu']"));
        Assert.Equal("false", cut.Find("[data-testid='roster-more-person-king']").GetAttribute("aria-expanded"));
    }

    [Fact]
    public async Task OpeningAnotherMenu_ClosesTheFirst()
    {
        await using var session = new DemoRealmSessionFactory().Create(null);
        var cut = Open(session);
        await cut.Find("[data-testid='roster-more-person-king']").TriggerEventAsync("onclick", new MouseEventArgs());

        await cut.Find("[data-testid='roster-more-person-queen']").TriggerEventAsync("onclick", new MouseEventArgs());

        Assert.Single(cut.FindAll("[data-testid='roster-menu']"));
        Assert.Equal("false", cut.Find("[data-testid='roster-more-person-king']").GetAttribute("aria-expanded"));
        Assert.Equal("true", cut.Find("[data-testid='roster-more-person-queen']").GetAttribute("aria-expanded"));
    }

    [Fact]
    public async Task MoveToNotTracked_TakesThePersonOffThePeopleGroup_UpdatesTheCounts_AndSaysSo()
    {
        await using var session = new DemoRealmSessionFactory().Create(null);
        var cut = Open(session);

        await cut.Find("[data-testid='roster-more-person-king']").TriggerEventAsync("onclick", new MouseEventArgs());
        await cut.Find("[data-testid='roster-move-not-tracked']").TriggerEventAsync("onclick", new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.Equal("3", cut.Find("[data-testid='roster-count-people']").TextContent));
        Assert.Equal("3", cut.Find("[data-testid='roster-count-not-tracked']").TextContent);
        Assert.Equal(RosterGroup.NotTracked, Entry(session, King).Group);
        Assert.Equal($"{DemoCast.King.Name} moved to Not tracked", cut.Find("[data-testid='roster-status']").TextContent);
        Assert.Empty(cut.FindAll("[data-testid='roster-menu']"));
        Assert.Contains("person-king", Rows(cut, "not-tracked"));
    }

    [Fact]
    public async Task MoveToPeople_FromNotTracked_ClearsTheAutomaticMark()
    {
        await using var session = new DemoRealmSessionFactory().Create(null);
        var cut = Open(session);

        await cut.Find("[data-testid='roster-more-person-prince']").TriggerEventAsync("onclick", new MouseEventArgs());
        Assert.Equal(["Move to People", "Move to Trackers", "Move down"], MenuItems(cut));
        await cut.Find("[data-testid='roster-move-people']").TriggerEventAsync("onclick", new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.Equal("5", cut.Find("[data-testid='roster-count-people']").TextContent));
        Assert.Null(Entry(session, Prince).AutoMovedUtc);
        Assert.DoesNotContain("moved automatically", cut.Find("[data-testid='roster-meta-person-prince']").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MoveDown_ChangesTheOrderInsideTheGroup()
    {
        await using var session = new DemoRealmSessionFactory().Create(null);
        var cut = Open(session);

        await cut.Find("[data-testid='roster-more-person-king']").TriggerEventAsync("onclick", new MouseEventArgs());
        await cut.Find("[data-testid='roster-move-down']").TriggerEventAsync("onclick", new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.Equal(["person-queen", "person-king", "person-jester", "person-cryptid"], Rows(cut, "people")));
        Assert.Equal($"{DemoCast.King.Name} moved", cut.Find("[data-testid='roster-status']").TextContent);
    }

    [Fact]
    public async Task DroppingARowOnAnotherGroup_MovesIt()
    {
        await using var session = new DemoRealmSessionFactory().Create(null);
        var cut = Open(session);

        await cut.Find("[data-roster-entity='device_tracker.hatchback']").TriggerEventAsync("ondragstart", new DragEventArgs());
        await cut.Find("[data-testid='roster-group-vehicles']").TriggerEventAsync("ondrop", new DragEventArgs());

        cut.WaitForAssertion(() => Assert.Equal("2", cut.Find("[data-testid='roster-count-vehicles']").TextContent));
        Assert.Equal(RosterGroup.Vehicles, Entry(session, Hatchback).Group);
        Assert.Equal(["device-tracker-wagon", "device-tracker-hatchback"], Rows(cut, "vehicles"));
    }

    [Fact]
    public async Task DroppingARowOnARow_PutsItWhereThatRowIs()
    {
        await using var session = new DemoRealmSessionFactory().Create(null);
        var cut = Open(session);

        await cut.Find("[data-roster-entity='person.cryptid']").TriggerEventAsync("ondragstart", new DragEventArgs());
        await cut.Find("[data-testid='roster-row-person-king']").TriggerEventAsync("ondrop", new DragEventArgs());

        cut.WaitForAssertion(() => Assert.Equal(["person-cryptid", "person-king", "person-queen", "person-jester"], Rows(cut, "people")));
    }

    [Fact]
    public async Task ADropWithNothingDragged_DoesNothing()
    {
        await using var session = new DemoRealmSessionFactory().Create(null);
        var cut = Open(session);

        await cut.Find("[data-testid='roster-group-vehicles']").TriggerEventAsync("ondrop", new DragEventArgs());

        Assert.Equal("1", cut.Find("[data-testid='roster-count-vehicles']").TextContent);
        Assert.Equal(string.Empty, cut.Find("[data-testid='roster-status']").TextContent);
    }

    [Fact]
    public async Task TappingARow_OpensTheEditor_AndSaveStoresTheNameTitleAndColour()
    {
        await using var session = new DemoRealmSessionFactory().Create(null);
        var cut = Open(session);
        var palette = RosterRules.Palette;

        await cut.Find("[data-testid='roster-open-person-king']").TriggerEventAsync("onclick", new MouseEventArgs());
        Assert.Equal(DemoCast.King.Name, cut.Find("[data-testid='roster-name']").GetAttribute("value"));
        cut.Find("[data-testid='roster-name']").Input("Alden the Bold");
        cut.Find("[data-testid='roster-title']").Input("Keeper of the Keys");
        await cut.Find("[data-testid='roster-color-2']").TriggerEventAsync("onclick", new MouseEventArgs());
        Assert.Equal("true", cut.Find("[data-testid='roster-color-2']").GetAttribute("aria-checked"));
        await cut.Find("[data-testid='roster-save']").TriggerEventAsync("onclick", new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[data-testid='roster-edit']")));
        var saved = Entry(session, King);
        Assert.Equal("Alden the Bold", saved.DisplayName);
        Assert.Equal("Keeper of the Keys", saved.LoreTitle);
        Assert.Equal(palette[2], saved.Color, ignoreCase: true);
        Assert.Equal("Alden the Bold", cut.Find("[data-testid='roster-row-person-king'] .realm-roster__name > span").TextContent);
    }

    [Fact]
    public void EveryRow_HasTheSameStructure_AndTheAvatarOfThePin()
    {
        using var session = new DemoRealmSessionFactory().Create(null);
        var cut = Open(session);

        var rows = cut.FindAll(".realm-roster__item");
        Assert.NotEmpty(rows);
        foreach (var row in rows)
        {
            Assert.Single(row.QuerySelectorAll(".realm-roster__avatar"));
            Assert.Single(row.QuerySelectorAll(".realm-roster__main"));
            Assert.Single(row.QuerySelectorAll(".realm-roster__more"));
            Assert.Equal(2, row.QuerySelectorAll(".realm-roster__name, .realm-roster__meta").Length);
        }

        var king = cut.Find("[data-testid='roster-row-person-king'] .realm-roster__avatar");
        Assert.Equal("person", king.GetAttribute("data-shape"));
        var wagon = cut.Find("[data-testid='roster-row-device-tracker-wagon'] .realm-roster__avatar");
        Assert.Equal("tracker", wagon.GetAttribute("data-shape"));
        Assert.Equal("glyph", wagon.GetAttribute("data-face"));
    }

    [Fact]
    public async Task TheEditPanel_ListsTheEntities_AndAPictureChoiceIsSaved_ThenResetGivesTheSourceBack()
    {
        await using var session = new DemoRealmSessionFactory().Create(null);
        var cut = Open(session);

        await cut.Find("[data-testid='roster-open-person-king']").TriggerEventAsync("onclick", new MouseEventArgs());
        Assert.Contains("person.king", cut.Find("[data-testid='roster-identity']").TextContent, StringComparison.Ordinal);
        Assert.NotEmpty(cut.FindAll("[data-testid='roster-life360']"));
        Assert.Empty(cut.FindAll("[data-testid='roster-reset']"));

        cut.Find("[data-testid='roster-name']").Input("Alden");
        await cut.Find("[data-testid='roster-icon-glyph-pet']").TriggerEventAsync("onclick", new MouseEventArgs());
        await cut.Find("[data-testid='roster-save']").TriggerEventAsync("onclick", new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[data-testid='roster-edit']")));
        var saved = Entry(session, King);
        Assert.Equal("Alden", saved.DisplayName);
        Assert.Equal(RosterIcons.TokenOf(VehicleGlyph.Pet), saved.Icon);
        Assert.True(saved.IsCustomised);
        Assert.Equal("glyph", cut.Find("[data-testid='roster-row-person-king'] .realm-roster__avatar").GetAttribute("data-face"));

        await cut.Find("[data-testid='roster-open-person-king']").TriggerEventAsync("onclick", new MouseEventArgs());
        await cut.Find("[data-testid='roster-reset']").TriggerEventAsync("onclick", new MouseEventArgs());

        cut.WaitForAssertion(() => Assert.Equal(DemoCast.King.Name, Entry(session, King).DisplayName));
        Assert.Null(Entry(session, King).Icon);
        Assert.False(Entry(session, King).IsCustomised);
    }

    [Fact]
    public async Task Cancel_AndEscape_LeaveTheEntryAsItWas()
    {
        await using var session = new DemoRealmSessionFactory().Create(null);
        var cut = Open(session);

        await cut.Find("[data-testid='roster-open-person-king']").TriggerEventAsync("onclick", new MouseEventArgs());
        cut.Find("[data-testid='roster-name']").Input("Nobody");
        await cut.Find("[data-testid='roster-cancel']").TriggerEventAsync("onclick", new MouseEventArgs());
        Assert.Empty(cut.FindAll("[data-testid='roster-edit']"));

        await cut.Find("[data-testid='roster-open-person-king']").TriggerEventAsync("onclick", new MouseEventArgs());
        cut.Find("[data-testid='roster-name']").Input("Nobody");
        await cut.Find("[data-testid='roster-edit']").TriggerEventAsync("onkeydown", new KeyboardEventArgs { Key = "Escape" });
        Assert.Empty(cut.FindAll("[data-testid='roster-edit']"));

        Assert.Equal(DemoCast.King.Name, Entry(session, King).DisplayName);
    }

    [Fact]
    public async Task ABlankName_KeepsTheOldOne()
    {
        await using var session = new DemoRealmSessionFactory().Create(null);
        var cut = Open(session);

        await cut.Find("[data-testid='roster-open-person-king']").TriggerEventAsync("onclick", new MouseEventArgs());
        cut.Find("[data-testid='roster-name']").Input("   ");
        await cut.Find("[data-testid='roster-save']").TriggerEventAsync("onclick", new MouseEventArgs());

        Assert.Equal(DemoCast.King.Name, Entry(session, King).DisplayName);
    }

    [Fact]
    public async Task ARosterChangeFromOutside_IsShownWithoutATap()
    {
        await using var session = new DemoRealmSessionFactory().Create(null);
        var cut = Open(session);

        await session.Roster.MoveAsync(King, RosterGroup.Vehicles);

        cut.WaitForAssertion(() => Assert.Equal("2", cut.Find("[data-testid='roster-count-vehicles']").TextContent));
    }

    [Fact]
    public void ASessionWithoutARoster_ListsNobody_AndSaysWhy()
    {
        var cut = RenderWithProviders<RosterSection>();

        Assert.Single(cut.FindAll("[data-testid='roster-empty']"));
        Assert.Equal(["0", "0", "0"], cut.FindAll(".realm-roster__count").Select(count => count.TextContent));
    }

    // ---- helpers -----------------------------------------------------------------------------------------------------------------------

    private IRenderedComponent<RosterSection> Open(IRealmSession session) =>
        RenderWithProviders<RosterSection>(parameters => parameters.AddCascadingValue(session));

    private static RosterEntry Entry(IRealmSession session, string entityId) => session.Roster.Entries.Single(entry => entry.EntityId == entityId);

    // The row test ids of a group, minus the prefix.
    private static string[] Rows(IRenderedComponent<RosterSection> cut, string group) =>
        cut.FindAll($"[data-testid='roster-group-{group}'] li[data-testid^='roster-row-']")
            .Select(item => item.GetAttribute("data-testid")!["roster-row-".Length..])
            .ToArray();

    private static string[] MenuItems(IRenderedComponent<RosterSection> cut) =>
        cut.FindAll("[data-testid='roster-menu'] button").Select(item => item.TextContent.Trim()).ToArray();
}
