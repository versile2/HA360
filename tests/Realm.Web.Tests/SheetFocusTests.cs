using System.Text.RegularExpressions;
using Realm.Demo;
using Realm.Domain;
using Realm.Web.Components.Sheet;
using Realm.Web.Layout;
using Realm.Web.State;
using Xunit;

namespace Realm.Web.Tests;

/// <summary>
/// The focus flow of D45 (01 section 10.2, <see cref="SheetFocus"/>): where the focus goes after each event and each Back step. A row, and the Here-now row of a place, take it to the handle; Back from
/// the detail and the list's collapse leave it there; clearing the selection (the X, Back, Esc or the empty map) takes it to the section tab; a pin or a bubble leaves it where it is. That the detail's
/// own back button takes it when the handle or the header opened the detail is <see cref="SheetDispatch.Apply"/>'s bool (<c>SheetDispatchTests</c>); the components that act on a request are in
/// <c>SheetPartsTests</c>, and the page that joins them is pinned at the end by its source, as the page is not rendered here.
/// </summary>
public sealed class SheetFocusTests
{
    private static readonly EntityRef Jester = new(EntityKind.Member, DemoCast.Jester.Id);
    private static readonly EntityRef Wagon = new(EntityKind.Vehicle, DemoCast.Wagon.Id);

    // ---- after an event ---------------------------------------------------------------------------------------------------------------------

    [Fact(DisplayName = "[AC-47a] A row tap, from either section, moves the focus to the sheet handle")]
    public void ARowTap_MovesTheFocusToTheHandle()
    {
        Assert.Equal(FocusTarget.Handle, SheetFocus.After(new SheetEvent.RowTap(Jester), null, Jester));
        Assert.Equal(FocusTarget.Handle, SheetFocus.After(new SheetEvent.RowTap(Wagon), Jester, Wagon));
    }

    [Fact(DisplayName = "[AC-47a] A person tapped in a place's Here-now list moves the focus to the handle, like a row")]
    public void AHereNowTap_MovesTheFocusToTheHandle() =>
        Assert.Equal(FocusTarget.Handle, SheetFocus.After(new SheetEvent.HereNowTap(Jester.Id), new EntityRef(EntityKind.Place, DemoPlaces.Home.Id), Jester));

    [Fact(DisplayName = "[AC-47a] Clearing the selection with the X or the empty map moves the focus to the section tab")]
    public void ClearingTheSelection_MovesTheFocusToTheSectionTab()
    {
        Assert.Equal(FocusTarget.SectionTab, SheetFocus.After(new SheetEvent.ClearTap(), Jester, null));
        Assert.Equal(FocusTarget.SectionTab, SheetFocus.After(new SheetEvent.MapTap(), Jester, null));
    }

    [Fact]
    public void AnEmptyMapTap_ThatKeepsTheSelection_OrClearsNothing_MovesNothing()
    {
        // At 80 percent the map tap collapses the sheet and keeps the selection; with nothing selected there is nothing to clear.
        Assert.Equal(FocusTarget.None, SheetFocus.After(new SheetEvent.MapTap(), Jester, Jester));
        Assert.Equal(FocusTarget.None, SheetFocus.After(new SheetEvent.MapTap(), null, null));
        Assert.Equal(FocusTarget.None, SheetFocus.After(new SheetEvent.ClearTap(), null, null));
    }

    [Fact(DisplayName = "[AC-47a] A pin or a bubble selects where the focus is: it moves nothing")]
    public void APinOrABubble_LeavesTheFocusWhereItIs()
    {
        Assert.Equal(FocusTarget.None, SheetFocus.After(new SheetEvent.PinTap(Jester), null, Jester));
        Assert.Equal(FocusTarget.None, SheetFocus.After(new SheetEvent.BubbleTap([Jester.Id]), null, Jester));
        Assert.Equal(FocusTarget.None, SheetFocus.After(new SheetEvent.BubbleTap([Jester.Id, DemoCast.Prince.Id]), null, null));
    }

    [Fact(DisplayName = "[AC-47a] The handle and the header open the detail through the detail's own focus, so the focus logic adds no move of its own")]
    public void TheHandleAndTheHeader_AddNoMoveOfTheirOwn()
    {
        Assert.Equal(FocusTarget.None, SheetFocus.After(new SheetEvent.HandleToggle(), Jester, Jester));
        Assert.Equal(FocusTarget.None, SheetFocus.After(new SheetEvent.HandleSet(SheetSize.Tall), Jester, Jester));
        Assert.Equal(FocusTarget.None, SheetFocus.After(new SheetEvent.HandleSet(SheetSize.Peek), Jester, Jester));
    }

    [Fact]
    public void ASegmentTapAndTheNavReTap_MoveNothing()
    {
        Assert.Equal(FocusTarget.None, SheetFocus.After(new SheetEvent.SegmentTap(Section.Places), null, null));
        Assert.Equal(FocusTarget.None, SheetFocus.After(new SheetEvent.NavReTap(), null, null));
        Assert.Equal(FocusTarget.None, SheetFocus.After(new SheetEvent.Escape(), Jester, Jester));
    }

    [Fact]
    public void AnEvent_IsRequired()
    {
        Assert.Throws<ArgumentNullException>(() => SheetFocus.After(null!, null, null));
    }

    // ---- after a Back step ------------------------------------------------------------------------------------------------------------------

    [Theory(DisplayName = "[AC-47a] Back from the detail, and the list's collapse, leave the focus on the handle")]
    [InlineData(BackStep.Detail)]
    [InlineData(BackStep.List)]
    public void BackFromTheDetail_OrTheList_ReturnsTheFocusToTheHandle(BackStep step) =>
        Assert.Equal(FocusTarget.Handle, SheetFocus.After(step));

    [Fact(DisplayName = "[AC-47a] Back that clears the selection returns the focus to the active section tab")]
    public void BackThatClearsTheSelection_ReturnsTheFocusToTheSectionTab() =>
        Assert.Equal(FocusTarget.SectionTab, SheetFocus.After(BackStep.Selection));

    [Theory]
    [InlineData(BackStep.Overlay)]
    [InlineData(BackStep.Leave)]
    public void AnOverlayThatCloses_OrALeave_MovesNothing(BackStep step) =>
        Assert.Equal(FocusTarget.None, SheetFocus.After(step));

    [Fact]
    public void EachStepOfTheChain_HasExactlyOneTarget_FromTheRealReducer()
    {
        // The chain as the reducer walks it from a detail at 80 percent: Detail, Selection, List, Leave (Compact).
        var state = new SheetState(Section.Drivers, Jester, SheetSize.Tall);
        var targets = new List<FocusTarget>();
        for (var i = 0; i < 4; i++)
        {
            var (next, step) = BackReducer.Reduce(state, LayoutMode.Compact);
            targets.Add(SheetFocus.After(step));
            state = next;
            if (i == 1)
            {
                state = state with { Size = SheetSize.Tall };   // the list, opened again by the handle
            }
        }

        Assert.Equal([FocusTarget.Handle, FocusTarget.SectionTab, FocusTarget.Handle, FocusTarget.None], targets);
    }

    // ---- a request is its own identity ------------------------------------------------------------------------------------------------------

    [Fact]
    public void ARequest_IsItsOwnIdentity_SoTwoRequestsForOneTargetAreTwoAsks()
    {
        var first = new FocusRequest(FocusTarget.Handle);
        var second = new FocusRequest(FocusTarget.Handle);

        Assert.Equal(FocusTarget.Handle, first.Target);
        Assert.NotSame(first, second);
        Assert.False(first.Equals(second));
    }

    // ---- the page --------------------------------------------------------------------------------------------------------------------------

    [Fact(DisplayName = "[AC-47a] The page asks for the focus after every event and every Back step, and hands the request to the handle and to the tabs")]
    public void ThePage_AsksForTheFocus_AfterEveryEventAndEveryBackStep()
    {
        var page = Regex.Replace(
            File.ReadAllText(Path.Combine(PayloadContractTests.FindRepositoryRoot(), "src", "Realm.Web", "Pages", "LocationPage.razor")),
            @"\s+",
            " ");

        // An event: the target is SheetFocus's, read against the selection before and after; it replaces the last request (none when nothing moves).
        Assert.Contains("var before = Ui.Selection; _focusDetail = SheetDispatch.Apply(Ui, sheetEvent, Snapshot.Members); var target = SheetFocus.After(sheetEvent, before, Ui.Selection);", page, StringComparison.Ordinal);
        Assert.Contains("_focus = target == FocusTarget.None ? null : new FocusRequest(target);", page, StringComparison.Ordinal);

        // A Back step of HistorySync (the Android Back, Esc and the back arrow all reach it), and the subscription is undone.
        Assert.Contains("History.BackTaken += OnBackTaken;", page, StringComparison.Ordinal);
        Assert.Contains("History.BackTaken -= OnBackTaken;", page, StringComparison.Ordinal);
        Assert.Contains("var target = SheetFocus.After(step);", page, StringComparison.Ordinal);

        // The request reaches the handle (through the host) and the segments (through the content); it is dropped after the render that carried it.
        Assert.Equal(2, Regex.Matches(page, "Focus=\"@_focus\"").Count);
        Assert.Contains("_focus = null; if (!firstRender || _started)", page, StringComparison.Ordinal);
    }
}
