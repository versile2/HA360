using System.Text.RegularExpressions;
using Realm.Demo;
using Realm.Domain;
using Realm.Web.Components.Sheet;
using Realm.Web.Layout;
using Realm.Web.State;
using Xunit;

namespace Realm.Web.Tests;

/// <summary>
/// The strings of the sheet's one polite live region (01 section 10.3, <see cref="SheetAnnouncements"/>): "Showing Cass" for a selection, the count of a section ("4 drivers"), "List opened",
/// "Details opened" and "List collapsed" for a size change, and "a selection that collapses the sheet announces only 'Showing Cass'". They are derived from two sheet states, so the same words
/// follow a tap, the Android Back, Esc and the navigation bar's re-tap. The page's own wiring is pinned by its source at the end, as the page is not rendered here.
/// </summary>
public sealed class SheetAnnouncementsTests
{
    private static readonly RealmSnapshot Demo = FullCast.Session(null).Current;

    private static readonly EntityRef Jester = new(EntityKind.Member, DemoCast.Jester.Id);
    private static readonly EntityRef King = new(EntityKind.Member, DemoCast.King.Id);
    private static readonly EntityRef Wagon = new(EntityKind.Vehicle, DemoCast.Wagon.Id);
    private static readonly EntityRef Hearth = new(EntityKind.Place, DemoPlaces.Home.Id);

    private static string? Between(SheetState before, SheetState after, bool compact = true) =>
        SheetAnnouncements.Between(before, after, compact, Demo.Members, Demo.Vehicles, Demo.Places);

    private static SheetState State(EntityRef? selection, SheetSize size, Section section = Section.Drivers) => new(section, selection, size);

    // ---- a selection ------------------------------------------------------------------------------------------------------------------------

    [Fact(DisplayName = "[AC-47a] A row tapped at 80 percent announces only \"Showing Cass\", though the sheet collapses with it")]
    public void ARowAtTall_AnnouncesOnlyShowing_NotTheCollapse() =>
        Assert.Equal("Showing Cass", Between(State(null, SheetSize.Tall), State(Jester, SheetSize.Peek)));

    [Fact]
    public void ASelection_IsNamedByTheNameTheHeaderShows_ForAPersonAVehicleAndAPlace()
    {
        Assert.Equal("Showing Cass", Between(State(null, SheetSize.Peek), State(Jester, SheetSize.Peek)));
        Assert.Equal("Showing " + Demo.Vehicles.Single(vehicle => vehicle.Id == Wagon.Id).Name, Between(State(null, SheetSize.Peek, Section.Vehicles), State(Wagon, SheetSize.Peek, Section.Vehicles)));
        Assert.Equal("Showing Hearth Haven", Between(State(null, SheetSize.Peek, Section.Places), State(Hearth, SheetSize.Peek, Section.Places)));
    }

    [Fact]
    public void TheSelectionMovingToAnother_AnnouncesTheNewOne()
    {
        // A pin tapped while another is selected, and a person tapped in a place's Here-now list.
        Assert.Equal("Showing Alden", Between(State(Jester, SheetSize.Peek), State(King, SheetSize.Peek)));
        Assert.Equal("Showing Cass", Between(State(Hearth, SheetSize.Tall, Section.Places), State(Jester, SheetSize.Peek, Section.Places)));
    }

    [Fact]
    public void ASelectionThatHasLeftTheLists_AnnouncesNothing()
    {
        var gone = new EntityRef(EntityKind.Member, "nobody");

        Assert.Null(Between(State(null, SheetSize.Peek), State(gone, SheetSize.Peek)));
    }

    [Fact]
    public void TheSameSelection_AnnouncesNothingNew()
    {
        // The tap on the entity that is already selected changes nothing in the state (D89), and so says nothing.
        Assert.Null(Between(State(Jester, SheetSize.Peek), State(Jester, SheetSize.Peek)));
    }

    [Fact(DisplayName = "[AC-47a] Clearing the selection empties the region, so that the same words can be said again")]
    public void ClearingTheSelection_EmptiesTheRegion() =>
        Assert.Equal(string.Empty, Between(State(Jester, SheetSize.Peek), State(null, SheetSize.Peek)));

    // ---- the size ---------------------------------------------------------------------------------------------------------------------------

    [Fact(DisplayName = "[AC-47a] The handle at 80 percent with nothing selected announces \"List opened\", and back at Peek \"List collapsed\"")]
    public void TheList_OpensAndCollapses()
    {
        Assert.Equal("List opened", Between(State(null, SheetSize.Peek), State(null, SheetSize.Tall)));
        Assert.Equal("List collapsed", Between(State(null, SheetSize.Tall), State(null, SheetSize.Peek)));
    }

    [Fact(DisplayName = "[AC-47a] The handle or the header at 80 percent with a selection announces \"Details opened\", and Back to Peek \"List collapsed\"")]
    public void TheDetail_OpensAndCollapses()
    {
        Assert.Equal("Details opened", Between(State(Jester, SheetSize.Peek), State(Jester, SheetSize.Tall)));
        Assert.Equal("List collapsed", Between(State(Jester, SheetSize.Tall), State(Jester, SheetSize.Peek)));
    }

    [Fact]
    public void InThePanel_ThereIsNoSize_SoNoSizeIsAnnounced() =>
        Assert.Null(Between(State(null, SheetSize.Peek), State(null, SheetSize.Tall), compact: false));

    [Fact]
    public void InThePanel_ASelectionIsStillNamed_AndClearingStillEmpties()
    {
        Assert.Equal("Showing Cass", Between(State(null, SheetSize.Peek), State(Jester, SheetSize.Peek), compact: false));
        Assert.Equal(string.Empty, Between(State(Jester, SheetSize.Peek), State(null, SheetSize.Peek), compact: false));
    }

    // ---- the section ------------------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(Section.Drivers, "5 drivers")]
    [InlineData(Section.Vehicles, "2 vehicles")]
    [InlineData(Section.Places, "14 places")]
    public void ASectionChange_AnnouncesTheCountOfTheSectionThatShows(Section section, string expected)
    {
        var from = section == Section.Drivers ? Section.Places : Section.Drivers;

        Assert.Equal(expected, Between(State(null, SheetSize.Peek, from), State(null, SheetSize.Peek, section)));
    }

    [Theory]
    [InlineData(Section.Drivers, "1 driver")]
    [InlineData(Section.Vehicles, "1 vehicle")]
    [InlineData(Section.Places, "1 place")]
    public void OneRow_IsSingular(Section section, string expected) =>
        Assert.Equal(expected, SheetAnnouncements.SectionCount(section, 1, 1, 1));

    [Fact]
    public void NoRows_AreStillACount() =>
        Assert.Equal("0 drivers", SheetAnnouncements.SectionCount(Section.Drivers, 0, 0, 0));

    [Fact]
    public void NoChange_AnnouncesNothing()
    {
        Assert.Null(Between(State(null, SheetSize.Peek), State(null, SheetSize.Peek)));
        Assert.Null(Between(State(null, SheetSize.Tall, Section.Places), State(null, SheetSize.Tall, Section.Places)));
    }

    // ---- the words themselves ---------------------------------------------------------------------------------------------------------------

    [Fact]
    public void TheWords_AreTheSpecs()
    {
        Assert.Equal("Details opened", SheetAnnouncements.DetailsOpened);
        Assert.Equal("List opened", SheetAnnouncements.ListOpened);
        Assert.Equal("List collapsed", SheetAnnouncements.ListCollapsed);
        Assert.Equal("Showing Cass", SheetAnnouncements.Showing("Cass"));
    }

    [Fact]
    public void ANameIsRequired_AndSoAreTheLists()
    {
        Assert.Throws<ArgumentException>(() => SheetAnnouncements.Showing(string.Empty));
        Assert.Throws<ArgumentNullException>(() => SheetAnnouncements.Showing(null!));
        Assert.Throws<ArgumentNullException>(() => SheetAnnouncements.Between(default, default, true, null!, Demo.Vehicles, Demo.Places));
        Assert.Throws<ArgumentNullException>(() => SheetAnnouncements.Between(default, default, true, Demo.Members, null!, Demo.Places));
        Assert.Throws<ArgumentNullException>(() => SheetAnnouncements.Between(default, default, true, Demo.Members, Demo.Vehicles, null!));
    }

    // ---- the page ---------------------------------------------------------------------------------------------------------------------------

    [Fact(DisplayName = "[AC-47a] The page announces from the transition of the state, whoever caused it, and hands the text to the one live region")]
    public void ThePage_AnnouncesFromTheTransitionOfTheState()
    {
        var page = Regex.Replace(
            File.ReadAllText(Path.Combine(PayloadContractTests.FindRepositoryRoot(), "src", "Realm.Web", "Pages", "LocationPage.razor")),
            @"\s+",
            " ");

        Assert.Contains("SheetAnnouncements.Between(_announced, sheet, _mode == LayoutMode.Compact, Snapshot.Members, Snapshot.Vehicles, Snapshot.Places)", page, StringComparison.Ordinal);
        Assert.Contains("Announcement=\"@_announcement\"", page, StringComparison.Ordinal);
        Assert.Contains("_announced = Ui.Sheet;", page, StringComparison.Ordinal);

        // It runs where every change of the state lands (the Android Back, Esc and the re-tap never pass the page's own handlers), and nowhere else.
        Assert.Contains("_ = InvokeAsync(() => { Announce(); RefreshMapLayout(); StateHasChanged(); });", page, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(page, @"SheetAnnouncements\.Between\("));
    }
}
