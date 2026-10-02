using System.Text.RegularExpressions;
using Realm.Demo;
using Realm.Domain;
using Realm.Web.Layout;
using Realm.Web.Map;
using Realm.Web.State;
using Xunit;

namespace Realm.Web.Tests;

/// <summary>
/// The one path every tap takes into the state (<see cref="SheetDispatch"/>, 03 section 3.6): a member pin, a vehicle pin, a zone, a bubble and the empty map each become one
/// <see cref="SheetEvent"/> and run through the reducer, with the Follow mirror and the focus rule settled in the same place (01 sections 4.13 and 4.14, D45, D84, D89). A repeat tap on the
/// selected entity changes nothing in the state, which is what makes <c>MapView</c> run the selection flight again (see <c>MapViewTests</c>). The last rows pin that the page
/// binds each raiser to its event, since a page is not rendered here.
/// </summary>
public sealed class SheetDispatchTests
{
    private static readonly EntityRef King = new(EntityKind.Member, DemoCast.King.Id);
    private static readonly EntityRef Queen = new(EntityKind.Member, DemoCast.Queen.Id);
    private static readonly EntityRef Wagon = new(EntityKind.Vehicle, DemoCast.Wagon.Id);
    private static readonly EntityRef Hearth = new(EntityKind.Place, DemoPlaces.Home.Id);

    // The demo cast at its fixed instant: the Queen is driving with a fresh fix, nobody else is (02 section 9.2).
    private static readonly IReadOnlyList<MemberVm> Members = DrivingFormatterTests.Demo().Current.Members;

    private static RealmUiState NewUi(LayoutMode mode = LayoutMode.Compact) =>
        new() { Layout = new LayoutSnapshot(mode, false, mode == LayoutMode.Compact ? 412 : 900, 915, new Padding(0, 0, 0, 0)) };

    // ---- a pin ---------------------------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void PinTap_OnADrivingMemberWithAFreshFix_SelectsIt_AtPeek_AndFollowsIt()
    {
        var ui = NewUi();

        var focusDetail = SheetDispatch.Apply(ui, new SheetEvent.PinTap(Queen), Members);

        Assert.False(focusDetail);
        Assert.Equal(Queen, ui.Selection);
        Assert.Equal(SheetSize.Peek, ui.SheetSize);
        Assert.Equal(new SheetBody.Header(Queen), ui.Body);
        Assert.Equal(Queen.Id, ui.FollowMemberId);
    }

    [Fact]
    public void PinTap_OnAMemberWhoIsNotDriving_SelectsIt_AndFollowsNobody()
    {
        var ui = NewUi();

        SheetDispatch.Apply(ui, new SheetEvent.PinTap(King), Members);

        Assert.Equal(King, ui.Selection);
        Assert.Null(ui.FollowMemberId);
    }

    [Fact]
    public void PinTap_OnADrivingMemberWhoseFixIsNotFresh_FollowsNobody()
    {
        var ui = NewUi();
        var queenGoneStale = Members.Select(member => member.Id == Queen.Id ? member with { Freshness = Freshness.Stale } : member).ToList();

        SheetDispatch.Apply(ui, new SheetEvent.PinTap(Queen), queenGoneStale);

        Assert.Equal(Queen, ui.Selection);
        Assert.Null(ui.FollowMemberId);
    }

    [Fact]
    public void PinTap_OnAVehicle_AndOnAZone_SelectThemAndFollowNobody()
    {
        var ui = NewUi();

        SheetDispatch.Apply(ui, new SheetEvent.PinTap(Wagon), Members);
        Assert.Equal(Wagon, ui.Selection);
        Assert.Null(ui.FollowMemberId);

        SheetDispatch.Apply(ui, new SheetEvent.PinTap(Hearth), Members);
        Assert.Equal(Hearth, ui.Selection);
        Assert.Equal(new SheetBody.Header(Hearth), ui.Body);
        Assert.Null(ui.FollowMemberId);
    }

    [Fact]
    public void PinTap_OnAnotherEntity_ReplacesTheSelection_AndFoldsTheDetailBackToThePeekHeader()
    {
        var ui = NewUi();
        SheetDispatch.Apply(ui, new SheetEvent.PinTap(King), Members);
        SheetDispatch.Apply(ui, new SheetEvent.HandleToggle(), Members);
        Assert.Equal(new SheetBody.Detail(King), ui.Body);

        SheetDispatch.Apply(ui, new SheetEvent.PinTap(Queen), Members);

        Assert.Equal(new SheetBody.Header(Queen), ui.Body);
        Assert.Equal(Queen.Id, ui.FollowMemberId);
    }

    [Fact(DisplayName = "[R1-04] A repeat tap on the selected pin changes nothing in the state, at Peek or at 80 percent, so the map alone runs the flight again")]
    public void PinTap_OnTheSelectedEntity_ChangesNothing_AtEitherSize()
    {
        var ui = NewUi();
        SheetDispatch.Apply(ui, new SheetEvent.PinTap(Queen), Members);
        var changes = 0;
        ui.Changed += () => changes++;

        SheetDispatch.Apply(ui, new SheetEvent.PinTap(Queen), Members);   // at Peek
        SheetDispatch.Apply(ui, new SheetEvent.HandleToggle(), Members);
        Assert.Equal(1, changes);
        var tall = ui.Sheet;

        SheetDispatch.Apply(ui, new SheetEvent.PinTap(Queen), Members);   // at 80 percent: neither collapses nor expands

        Assert.Equal(tall, ui.Sheet);
        Assert.Equal(SheetSize.Tall, ui.SheetSize);
        Assert.Equal(1, changes);
        Assert.Equal(Queen.Id, ui.FollowMemberId);
    }

    [Fact]
    public void PinTap_OnTheSelectedVehicleAndZone_ChangesNothingEither()
    {
        var ui = NewUi();
        foreach (var entity in new[] { Wagon, Hearth })
        {
            SheetDispatch.Apply(ui, new SheetEvent.PinTap(entity), Members);
            var before = ui.Sheet;
            var changes = 0;
            void Count() => changes++;
            ui.Changed += Count;

            SheetDispatch.Apply(ui, new SheetEvent.PinTap(entity), Members);

            ui.Changed -= Count;
            Assert.Equal(before, ui.Sheet);
            Assert.Equal(0, changes);
        }
    }

    // ---- a bubble (D84) ----------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void BubbleTap_WithOneId_IsTheSameSelectionAsThePinTap()
    {
        var byPin = NewUi();
        var byBubble = NewUi();

        SheetDispatch.Apply(byPin, new SheetEvent.PinTap(Queen), Members);
        SheetDispatch.Apply(byBubble, new SheetEvent.BubbleTap([Queen.Id]), Members);

        Assert.Equal(byPin.Sheet, byBubble.Sheet);
        Assert.Equal(byPin.FollowMemberId, byBubble.FollowMemberId);
        Assert.Equal(Queen.Id, byBubble.FollowMemberId);
    }

    [Fact]
    public void BubbleTap_OnTheSelectedMember_ChangesNothing()
    {
        var ui = NewUi();
        SheetDispatch.Apply(ui, new SheetEvent.PinTap(Queen), Members);
        SheetDispatch.Apply(ui, new SheetEvent.HandleToggle(), Members);
        var before = ui.Sheet;

        SheetDispatch.Apply(ui, new SheetEvent.BubbleTap([Queen.Id]), Members);

        Assert.Equal(before, ui.Sheet);
        Assert.Equal(Queen.Id, ui.FollowMemberId);
    }

    [Fact]
    public void BubbleTap_OfACluster_ChangesNothing_NotEvenTheFollowOfTheSelection()
    {
        var ui = NewUi();
        SheetDispatch.Apply(ui, new SheetEvent.PinTap(Queen), Members);
        var before = ui.Sheet;

        SheetDispatch.Apply(ui, new SheetEvent.BubbleTap([King.Id, Queen.Id]), Members);

        Assert.Equal(before, ui.Sheet);
        Assert.Equal(Queen.Id, ui.FollowMemberId);
    }

    // ---- the empty map ------------------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void MapTap_AtPeek_ClearsTheSelection_AndTheFollow()
    {
        var ui = NewUi();
        SheetDispatch.Apply(ui, new SheetEvent.PinTap(Queen), Members);

        SheetDispatch.Apply(ui, new SheetEvent.MapTap(), Members);

        Assert.Null(ui.Selection);
        Assert.Null(ui.FollowMemberId);
    }

    [Fact]
    public void MapTap_AtEightyPercent_FoldsTheSheet_AndKeepsTheSelectionAndTheFollow()
    {
        var ui = NewUi();
        SheetDispatch.Apply(ui, new SheetEvent.PinTap(Queen), Members);
        SheetDispatch.Apply(ui, new SheetEvent.HandleToggle(), Members);

        SheetDispatch.Apply(ui, new SheetEvent.MapTap(), Members);

        Assert.Equal(new SheetBody.Header(Queen), ui.Body);
        Assert.Equal(Queen.Id, ui.FollowMemberId);
    }

    [Fact]
    public void MapTap_InThePanel_AlwaysClearsTheSelection()
    {
        var ui = NewUi(LayoutMode.Expanded);
        SheetDispatch.Apply(ui, new SheetEvent.PinTap(Queen), Members);

        SheetDispatch.Apply(ui, new SheetEvent.MapTap(), Members);

        Assert.Null(ui.Selection);
        Assert.Null(ui.FollowMemberId);
    }

    // ---- the other raisers, and the focus rule ---------------------------------------------------------------------------------------------------------

    [Fact]
    public void RowTap_AndHereNowTap_MirrorFollowLikeAPin()
    {
        var ui = NewUi();

        SheetDispatch.Apply(ui, new SheetEvent.RowTap(Queen), Members);
        Assert.Equal(Queen.Id, ui.FollowMemberId);

        SheetDispatch.Apply(ui, new SheetEvent.RowTap(King), Members);
        Assert.Null(ui.FollowMemberId);

        SheetDispatch.Apply(ui, new SheetEvent.HereNowTap(Queen.Id), Members);
        Assert.Equal(Queen, ui.Selection);
        Assert.Equal(Queen.Id, ui.FollowMemberId);
    }

    [Fact]
    public void TheSelectionGoingAway_EndsFollow_WhateverRaisedIt()
    {
        var ui = NewUi();
        SheetDispatch.Apply(ui, new SheetEvent.PinTap(Queen), Members);
        SheetDispatch.Apply(ui, new SheetEvent.ClearTap(), Members);
        Assert.Null(ui.FollowMemberId);

        SheetDispatch.Apply(ui, new SheetEvent.PinTap(Queen), Members);
        SheetDispatch.Apply(ui, new SheetEvent.SegmentTap(Section.Vehicles), Members);
        Assert.Null(ui.Selection);
        Assert.Null(ui.FollowMemberId);

        SheetDispatch.Apply(ui, new SheetEvent.PinTap(Queen), Members);
        SheetDispatch.Apply(ui, new SheetEvent.NavReTap(), Members);
        Assert.Null(ui.FollowMemberId);
    }

    [Fact(DisplayName = "[D46] The handle and the header body raise one event, and opening the detail with it asks for the focus")]
    public void HandleToggle_OpeningTheDetail_AsksForTheFocus_AndNothingElseDoes()
    {
        var ui = NewUi();
        Assert.False(SheetDispatch.Apply(ui, new SheetEvent.PinTap(Queen), Members));

        Assert.True(SheetDispatch.Apply(ui, new SheetEvent.HandleToggle(), Members));
        Assert.Equal(new SheetBody.Detail(Queen), ui.Body);
        Assert.False(SheetDispatch.Apply(ui, new SheetEvent.HandleToggle(), Members));   // closing it does not

        Assert.True(SheetDispatch.Apply(ui, new SheetEvent.HandleSet(SheetSize.Tall), Members));
        Assert.False(SheetDispatch.Apply(ui, new SheetEvent.HandleSet(SheetSize.Tall), Members));   // already there
    }

    [Fact]
    public void ADetailThatAppearsBecauseAPinWasTapped_InThePanel_LeavesTheFocusWhereItIs()
    {
        var ui = NewUi(LayoutMode.Expanded);

        var focusDetail = SheetDispatch.Apply(ui, new SheetEvent.PinTap(Queen), Members);

        Assert.False(focusDetail);
        Assert.Equal(new SheetBody.Detail(Queen), ui.Body);
    }

    [Fact]
    public void Apply_RefusesNothingButNull()
    {
        var ui = NewUi();

        Assert.Throws<ArgumentNullException>(() => SheetDispatch.Apply(null!, new SheetEvent.MapTap(), Members));   // null-forgiving: the guard clause is what is under test
        Assert.Throws<ArgumentNullException>(() => SheetDispatch.Apply(ui, null!, Members));   // null-forgiving: the guard clause is what is under test
        Assert.Throws<ArgumentNullException>(() => SheetDispatch.Apply(ui, new SheetEvent.MapTap(), null!));   // null-forgiving: the guard clause is what is under test
    }

    // ---- the page binds each raiser to its event (the page is not rendered here) -----------------------------------------------------------------------

    [Fact]
    public void ThePage_TurnsEachRaiserIntoItsEvent_AndRunsThemAllThroughSheetDispatch()
    {
        var page = Regex.Replace(File.ReadAllText(Path.Combine(PayloadContractTests.FindRepositoryRoot(), "src", "Realm.Web", "Pages", "LocationPage.razor")), @"\s+", " ");

        // The map's four taps, and the handle and the header body on one handler (D46, R2-04).
        Assert.Contains("OnPinTapAsync(EntityRef entity) => ApplyAsync(new SheetEvent.PinTap(entity));", page, StringComparison.Ordinal);
        Assert.Contains("OnBubbleTapAsync(IReadOnlyList<string> ids) => ApplyAsync(new SheetEvent.BubbleTap(ids));", page, StringComparison.Ordinal);
        Assert.Contains("OnMapTapAsync() => ApplyAsync(new SheetEvent.MapTap());", page, StringComparison.Ordinal);
        Assert.Contains("OnToggleAsync() => ApplyAsync(new SheetEvent.HandleToggle());", page, StringComparison.Ordinal);
        Assert.Contains("OnPinTap=\"OnPinTapAsync\"", page, StringComparison.Ordinal);
        Assert.Contains("OnBubbleTap=\"OnBubbleTapAsync\"", page, StringComparison.Ordinal);
        Assert.Contains("OnMapTap=\"OnMapTapAsync\"", page, StringComparison.Ordinal);
        Assert.Equal(2, Regex.Matches(page, "OnToggle=\"OnToggleAsync\"").Count);   // the sheet host's handle and the selection header

        // One path: nothing but SheetDispatch applies a tap to the state. The one other Apply is the Demo start-up override (sheet=80), which is no tap and runs once per circuit.
        Assert.Contains("SheetDispatch.Apply(Ui, sheetEvent, Snapshot.Members)", page, StringComparison.Ordinal);
        Assert.Contains("if (Ui.TryBeginStartup() && Overrides?.Sheet == \"80\") { Ui.Apply(new SheetEvent.HandleSet(SheetSize.Tall)); }", page, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(page, @"Ui\.Apply\("));
    }
}
