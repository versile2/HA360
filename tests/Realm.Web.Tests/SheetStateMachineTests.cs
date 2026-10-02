using Realm.Domain;
using Realm.Web.Layout;
using Realm.Web.State;
using Xunit;

namespace Realm.Web.Tests;

/// <summary>
/// <see cref="SheetStateMachine"/> and <see cref="SheetBody.BodyOf"/> (01 section 5.7, 03 sections 3.6 and 8.3, D45, D46): every named row of the event table, and the
/// properties that hold over every reachable state. Pure: no browser, no component. The rows are named by what they assert, so a failing row names itself.
/// </summary>
public sealed class SheetStateMachineTests
{
    private static readonly EntityRef Jester = new(EntityKind.Member, "jester");
    private static readonly EntityRef King = new(EntityKind.Member, "king");
    private static readonly EntityRef Wagon = new(EntityKind.Vehicle, "wagon");
    private static readonly EntityRef Hall = new(EntityKind.Place, "hall");

    private static EntityRef Entity(string name) => name switch
    {
        "member" => Jester,
        "vehicle" => Wagon,
        "place" => Hall,
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, null),
    };

    private static SheetState At(EntityRef? selection, SheetSize size, Section section = Section.Drivers) => new(section, selection, size);

    private static SheetState Reduce(SheetState state, SheetEvent sheetEvent, LayoutMode mode = LayoutMode.Compact) =>
        SheetStateMachine.Reduce(state, sheetEvent, mode);

    // ---- 01 section 5.7, the rows named in 03 section 8.3 -------------------------------------------------------------------------------------------------------------

    [Fact]
    public void AppOpens_DriversNothingSelectedPeek()
    {
        Assert.Equal(new SheetState(Section.Drivers, null, SheetSize.Peek), SheetState.Initial);
        Assert.IsType<SheetBody.List>(SheetBody.BodyOf(SheetState.Initial, LayoutMode.Compact));
    }

    [Theory]
    [InlineData("member")]
    [InlineData("vehicle")]
    [InlineData("place")]
    public void Select_FromRowAtTall_CollapsesToPeekWithSelection(string kind)
    {
        var entity = Entity(kind);
        var next = Reduce(At(null, SheetSize.Tall, Section.Vehicles), new SheetEvent.RowTap(entity));

        Assert.Equal(new SheetState(Section.Vehicles, entity, SheetSize.Peek), next);
        Assert.Equal(new SheetBody.Header(entity), SheetBody.BodyOf(next, LayoutMode.Compact));
    }

    [Theory]
    [InlineData("member")]
    [InlineData("vehicle")]
    [InlineData("place")]
    public void Select_FromRowAtPeek_GoesToPeekWithSelection(string kind)
    {
        var entity = Entity(kind);

        Assert.Equal(At(entity, SheetSize.Peek), Reduce(At(null, SheetSize.Peek), new SheetEvent.RowTap(entity)));
    }

    [Theory]
    [InlineData("member")]
    [InlineData("vehicle")]
    [InlineData("place")]
    public void Select_FromPinAtPeek_KeepsPeek(string kind)
    {
        var entity = Entity(kind);

        Assert.Equal(At(entity, SheetSize.Peek), Reduce(At(null, SheetSize.Peek), new SheetEvent.PinTap(entity)));
        Assert.Equal(At(entity, SheetSize.Peek), Reduce(At(King, SheetSize.Peek), new SheetEvent.PinTap(entity)));   // another entity replaces the selection, still at Peek
    }

    [Theory]
    [InlineData("member")]
    [InlineData("vehicle")]
    [InlineData("place")]
    public void Select_FromPinAtTall_CollapsesToPeek(string kind)
    {
        var entity = Entity(kind);

        Assert.Equal(At(entity, SheetSize.Peek), Reduce(At(null, SheetSize.Tall), new SheetEvent.PinTap(entity)));
        Assert.Equal(At(entity, SheetSize.Peek), Reduce(At(King, SheetSize.Tall), new SheetEvent.PinTap(entity)));   // from the detail of another entity
    }

    [Theory]
    [InlineData(SheetSize.Peek, false)]
    [InlineData(SheetSize.Peek, true)]
    [InlineData(SheetSize.Tall, false)]
    [InlineData(SheetSize.Tall, true)]
    public void Select_SingleBubble_EqualsPinTap(SheetSize size, bool alreadySelected)
    {
        var from = At(alreadySelected ? Jester : King, size, Section.Places);
        var viaBubble = Reduce(from, new SheetEvent.BubbleTap([Jester.Id]));

        Assert.Equal(Reduce(from, new SheetEvent.PinTap(Jester)), viaBubble);
        Assert.Equal(Reduce(from, new SheetEvent.PinTap(Jester), LayoutMode.Expanded), Reduce(from, new SheetEvent.BubbleTap([Jester.Id]), LayoutMode.Expanded));
    }

    [Theory]
    [InlineData(false, SheetSize.Peek)]
    [InlineData(false, SheetSize.Tall)]
    [InlineData(true, SheetSize.Peek)]
    [InlineData(true, SheetSize.Tall)]
    public void Select_ClusterBubble_ChangesNothing(bool selected, SheetSize size)
    {
        var from = At(selected ? Wagon : null, size, Section.Vehicles);

        foreach (var mode in new[] { LayoutMode.Compact, LayoutMode.Expanded })
        {
            Assert.Equal(from, Reduce(from, new SheetEvent.BubbleTap([King.Id, Jester.Id]), mode));
            Assert.Equal(from, Reduce(from, new SheetEvent.BubbleTap([King.Id, Jester.Id, "prince"]), mode));
            Assert.Equal(from, Reduce(from, new SheetEvent.BubbleTap([]), mode));
        }
    }

    [Theory]
    [InlineData("member", SheetSize.Peek)]
    [InlineData("member", SheetSize.Tall)]
    [InlineData("vehicle", SheetSize.Peek)]
    [InlineData("vehicle", SheetSize.Tall)]
    [InlineData("place", SheetSize.Peek)]
    [InlineData("place", SheetSize.Tall)]
    public void PinTap_AlreadySelected_ChangesNothing(string kind, SheetSize size)
    {
        var entity = Entity(kind);
        var from = At(entity, size);

        Assert.Equal(from, Reduce(from, new SheetEvent.PinTap(entity)));   // nothing expands (and at Tall nothing collapses)
        Assert.Equal(from, Reduce(from, new SheetEvent.PinTap(entity), LayoutMode.Expanded));
        if (entity.Kind == EntityKind.Member)
        {
            Assert.Equal(from, Reduce(from, new SheetEvent.BubbleTap([entity.Id])));
        }
    }

    [Theory]
    [InlineData(SheetSize.Peek, LayoutMode.Compact)]
    [InlineData(SheetSize.Tall, LayoutMode.Compact)]
    [InlineData(SheetSize.Tall, LayoutMode.Expanded)]
    public void HereNowTap_SelectsMemberAtPeek(SheetSize size, LayoutMode mode)
    {
        var next = Reduce(At(Hall, size, Section.Places), new SheetEvent.HereNowTap(Jester.Id), mode);

        Assert.Equal(new SheetState(Section.Places, Jester, SheetSize.Peek), next);   // the member replaces the place; the section is untouched
    }

    [Theory]
    [InlineData("member")]
    [InlineData("vehicle")]
    [InlineData("place")]
    public void HandleToggle_WithSelection_SwapsHeaderAndDetail(string kind)
    {
        var entity = Entity(kind);
        var header = At(entity, SheetSize.Peek, Section.Vehicles);
        Assert.Equal(new SheetBody.Header(entity), SheetBody.BodyOf(header, LayoutMode.Compact));

        // D46: a tap on the Peek header body is this very event, raised by the header instead of the handle; the handle and the header cannot disagree.
        var detail = Reduce(header, new SheetEvent.HandleToggle());
        Assert.Equal(new SheetState(Section.Vehicles, entity, SheetSize.Tall), detail);
        Assert.Equal(new SheetBody.Detail(entity), SheetBody.BodyOf(detail, LayoutMode.Compact));

        var back = Reduce(detail, new SheetEvent.HandleToggle());
        Assert.Equal(header, back);
        Assert.Equal(new SheetBody.Header(entity), SheetBody.BodyOf(back, LayoutMode.Compact));
    }

    [Fact]
    public void HandleToggle_WithoutSelection_SwapsListAndList()
    {
        var peek = At(null, SheetSize.Peek, Section.Places);
        var tall = Reduce(peek, new SheetEvent.HandleToggle());

        Assert.Equal(At(null, SheetSize.Tall, Section.Places), tall);
        Assert.IsType<SheetBody.List>(SheetBody.BodyOf(peek, LayoutMode.Compact));
        Assert.IsType<SheetBody.List>(SheetBody.BodyOf(tall, LayoutMode.Compact));
        Assert.Equal(peek, Reduce(tall, new SheetEvent.HandleToggle()));
    }

    [Fact]
    public void HeaderTap_IsTheHandleToggle_ThereIsNoSeparateEvent()
    {
        // D46: the header raises HandleToggle (its X raises ClearTap and never also a toggle); a HeaderTap event would be a second way to reach 80 %.
        var cases = typeof(SheetEvent).GetNestedTypes().Select(t => t.Name).Order(StringComparer.Ordinal).ToArray();

        Assert.DoesNotContain(cases, name => name.Contains("Header", StringComparison.Ordinal));
        Assert.Contains("HandleToggle", cases);
        Assert.Contains("ClearTap", cases);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HandleSet_SetsTheSize_AndTheCurrentSizeChangesNothing(bool withSelection)
    {
        var peek = At(withSelection ? Jester : null, SheetSize.Peek, Section.Places);
        var tall = peek with { Size = SheetSize.Tall };

        Assert.Equal(tall, Reduce(peek, new SheetEvent.HandleSet(SheetSize.Tall)));
        Assert.Equal(peek, Reduce(tall, new SheetEvent.HandleSet(SheetSize.Peek)));
        Assert.Equal(peek, Reduce(peek, new SheetEvent.HandleSet(SheetSize.Peek)));   // the OnSizeChanged echo of a programmatic collapse is harmless
        Assert.Equal(tall, Reduce(tall, new SheetEvent.HandleSet(SheetSize.Tall)));
    }

    [Fact]
    public void HandleSet_AValueThatIsNotASize_ChangesNothing()
    {
        var from = At(Jester, SheetSize.Peek);

        Assert.Equal(from, Reduce(from, new SheetEvent.HandleSet((SheetSize)7)));
    }

    [Theory]
    [InlineData("member")]
    [InlineData("vehicle")]
    [InlineData("place")]
    public void MapTap_AtTallWithSelection_PeekKeepsSelection(string kind)
    {
        var entity = Entity(kind);

        var next = Reduce(At(entity, SheetSize.Tall, Section.Vehicles), new SheetEvent.MapTap());

        Assert.Equal(new SheetState(Section.Vehicles, entity, SheetSize.Peek), next);   // detail to header, selection kept
        Assert.Equal(new SheetBody.Header(entity), SheetBody.BodyOf(next, LayoutMode.Compact));
    }

    [Fact]
    public void MapTap_AtTallWithoutSelection_CollapsesTheList()
    {
        Assert.Equal(At(null, SheetSize.Peek, Section.Places), Reduce(At(null, SheetSize.Tall, Section.Places), new SheetEvent.MapTap()));
    }

    [Theory]
    [InlineData("member")]
    [InlineData("vehicle")]
    [InlineData("place")]
    public void MapTap_AtPeekWithSelection_ClearsSelection(string kind) =>
        Assert.Equal(At(null, SheetSize.Peek, Section.Places), Reduce(At(Entity(kind), SheetSize.Peek, Section.Places), new SheetEvent.MapTap()));

    [Fact]
    public void MapTap_AtPeekNoSelection_NoChange()
    {
        var from = At(null, SheetSize.Peek, Section.Vehicles);

        Assert.Equal(from, Reduce(from, new SheetEvent.MapTap()));
    }

    [Theory]
    [InlineData(SheetSize.Peek)]
    [InlineData(SheetSize.Tall)]
    public void MapTap_InExpanded_ClearsTheSelectionAndCollapsesNothing(SheetSize size)
    {
        Assert.Equal(At(null, size), Reduce(At(Jester, size), new SheetEvent.MapTap(), LayoutMode.Expanded));
        Assert.Equal(At(null, size), Reduce(At(null, size), new SheetEvent.MapTap(), LayoutMode.Expanded));
    }

    [Theory]
    [InlineData(SheetSize.Peek)]
    [InlineData(SheetSize.Tall)]
    public void ClearTap_ClearsSelectionAtPeek(SheetSize from)
    {
        var next = Reduce(At(Jester, from, Section.Places), new SheetEvent.ClearTap());

        Assert.Equal(At(null, SheetSize.Peek, Section.Places), next);
        Assert.IsType<SheetBody.List>(SheetBody.BodyOf(next, LayoutMode.Compact));
    }

    [Fact]
    public void ClearTap_NeverAlsoToggles_FromThePeekHeaderTheResultIsTheListAtPeek()
    {
        // The header's X raises ClearTap only (D46): a toggle after the clear would end at Tall.
        var next = Reduce(At(Jester, SheetSize.Peek), new SheetEvent.ClearTap());

        Assert.Equal(SheetSize.Peek, next.Size);
        Assert.Null(next.Selection);
    }

    [Theory]
    [InlineData(Section.Drivers)]
    [InlineData(Section.Vehicles)]
    [InlineData(Section.Places)]
    public void SegmentTap_AtTallWithSelection_ClearsSelectionKeepsSize(Section section)
    {
        var next = Reduce(At(Jester, SheetSize.Tall, Section.Drivers), new SheetEvent.SegmentTap(section));

        Assert.Equal(new SheetState(section, null, SheetSize.Tall), next);
        Assert.IsType<SheetBody.List>(SheetBody.BodyOf(next, LayoutMode.Compact));
    }

    [Theory]
    [InlineData(SheetSize.Peek)]
    [InlineData(SheetSize.Tall)]
    public void SegmentTap_NoSelection_ShowsTheListOfThatSection(SheetSize size) =>
        Assert.Equal(At(null, size, Section.Places), Reduce(At(null, size, Section.Drivers), new SheetEvent.SegmentTap(Section.Places)));

    [Theory]
    [InlineData(false, SheetSize.Peek)]
    [InlineData(false, SheetSize.Tall)]
    [InlineData(true, SheetSize.Peek)]
    [InlineData(true, SheetSize.Tall)]
    public void NavReTap_CollapsesAndClears(bool selected, SheetSize size)
    {
        var next = Reduce(At(selected ? Wagon : null, size, Section.Vehicles), new SheetEvent.NavReTap());

        Assert.Equal(At(null, SheetSize.Peek, Section.Vehicles), next);   // the section is remembered (01 section 2.2)
    }

    [Theory]
    [InlineData(SheetSize.Peek)]
    [InlineData(SheetSize.Tall)]
    public void Expanded_Select_ShowsDetailAndSetsPeek(SheetSize startSize)
    {
        var from = At(null, startSize);

        foreach (SheetEvent select in new SheetEvent[] { new SheetEvent.RowTap(Jester), new SheetEvent.PinTap(Jester), new SheetEvent.BubbleTap([Jester.Id]), new SheetEvent.HereNowTap(Jester.Id) })
        {
            var next = Reduce(from, select, LayoutMode.Expanded);

            Assert.Equal(At(Jester, SheetSize.Peek), next);                                                // D45: Peek in both layouts ...
            Assert.Equal(new SheetBody.Detail(Jester), SheetBody.BodyOf(next, LayoutMode.Expanded));      // ... and the panel always shows the detail
            Assert.Equal(new SheetBody.Header(Jester), SheetBody.BodyOf(next, LayoutMode.Compact));       // folding back from the panel returns to the header
        }
    }

    [Theory]
    [InlineData(SheetSize.Peek)]
    [InlineData(SheetSize.Tall)]
    public void Expanded_HandleEvents_ChangeNothing(SheetSize size)
    {
        var from = At(Jester, size);

        Assert.Equal(from, Reduce(from, new SheetEvent.HandleToggle(), LayoutMode.Expanded));
        Assert.Equal(from, Reduce(from, new SheetEvent.HandleSet(SheetSize.Peek), LayoutMode.Expanded));
        Assert.Equal(from, Reduce(from, new SheetEvent.HandleSet(SheetSize.Tall), LayoutMode.Expanded));
    }

    [Fact]
    public void BodyOf_IsDerivedForAllStates()
    {
        EntityRef?[] selections = [null, Jester, Wagon, Hall];
        var rows = 0;
        foreach (var section in Enum.GetValues<Section>())
        {
            foreach (var selection in selections)
            {
                foreach (var size in Enum.GetValues<SheetSize>())
                {
                    foreach (var mode in new[] { LayoutMode.Unknown, LayoutMode.Compact, LayoutMode.Expanded })
                    {
                        var state = new SheetState(section, selection, size);
                        SheetBody expected = selection is null ? new SheetBody.List()
                            : mode == LayoutMode.Expanded || size == SheetSize.Tall ? new SheetBody.Detail(selection)
                            : new SheetBody.Header(selection);

                        Assert.Equal(expected, SheetBody.BodyOf(state, mode));   // List iff no selection; Header iff a selection at Peek in Compact; Detail iff Tall or Expanded
                        rows++;
                    }
                }
            }
        }

        Assert.Equal(3 * 4 * 2 * 3, rows);
    }

    // ---- properties over every reachable state ---------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Reachable_EverySectionSelectionAndSizeCombination_AndNothingElse()
    {
        var walk = SheetWalk.Run();

        Assert.Equal(3 * 5 * 2, walk.States.Select(s => s.State).Distinct().Count());   // 3 sections x (none + 4 entities) x 2 sizes
        Assert.All(walk.States, s => Assert.True(Enum.IsDefined(s.State.Size) && Enum.IsDefined(s.State.Section)));
    }

    [Fact]
    public void Reduce_IsTotalAndPure_ForEveryReachableStateAndEvent()
    {
        var walk = SheetWalk.Run();

        foreach (var (state, mode) in walk.States)
        {
            foreach (var sheetEvent in SheetWalk.Events)
            {
                var first = SheetStateMachine.Reduce(state, sheetEvent, mode);
                Assert.Equal(first, SheetStateMachine.Reduce(state, sheetEvent, mode));
            }
        }

        Assert.Equal(SheetWalk.Events.Count * walk.States.Count, walk.Edges.Count);
    }

    [Fact]
    public void Tall_IsReachedOnlyThroughTheHandle()
    {
        // D45 and D46: 80 % only through the handle or the Peek header (HandleToggle), or HandleSet (the keyboard, the OnSizeChanged echo); never through a selection.
        foreach (var edge in SheetWalk.Run().Edges.Where(e => e.From.Size == SheetSize.Peek && e.To.Size == SheetSize.Tall))
        {
            Assert.Equal(LayoutMode.Compact, edge.Mode);
            Assert.True(edge.Event is SheetEvent.HandleToggle or SheetEvent.HandleSet { Size: SheetSize.Tall }, $"{edge.Event} took {edge.From} to {edge.To}");
        }
    }

    [Fact]
    public void EverySelectionEvent_LeavesAtPeek_UnlessTheEntityWasAlreadySelected()
    {
        var checkedEdges = 0;
        foreach (var edge in SheetWalk.Run().Edges)
        {
            var target = edge.Event switch
            {
                SheetEvent.RowTap row => row.Entity,
                SheetEvent.PinTap pin => pin.Entity,
                SheetEvent.BubbleTap { Ids.Count: 1 } bubble => new EntityRef(EntityKind.Member, bubble.Ids[0]),
                SheetEvent.HereNowTap here => new EntityRef(EntityKind.Member, here.MemberId),
                _ => null,
            };
            if (target is null)
            {
                continue;
            }

            checkedEdges++;
            var unchanged = edge.Event is SheetEvent.PinTap or SheetEvent.BubbleTap && edge.From.Selection == target;
            Assert.Equal(unchanged ? edge.From : edge.From with { Selection = target, Size = SheetSize.Peek }, edge.To);
        }

        Assert.True(checkedEdges > 0);
    }

    [Fact]
    public void Section_ChangesOnlyOnSegmentTap_AndASegmentTapClearsTheSelection()
    {
        foreach (var edge in SheetWalk.Run().Edges)
        {
            if (edge.Event is SheetEvent.SegmentTap segment)
            {
                Assert.Equal(segment.Section, edge.To.Section);
                Assert.Null(edge.To.Selection);
                Assert.Equal(edge.From.Size, edge.To.Size);
            }
            else
            {
                Assert.Equal(edge.From.Section, edge.To.Section);
            }
        }
    }

    [Fact]
    public void Selection_IsSetAndClearedOnlyByTheEventsThatDoThat()
    {
        foreach (var edge in SheetWalk.Run().Edges.Where(e => e.From.Selection != e.To.Selection))
        {
            if (edge.To.Selection is null)
            {
                Assert.True(
                    edge.Event is SheetEvent.SegmentTap or SheetEvent.ClearTap or SheetEvent.NavReTap or SheetEvent.MapTap or SheetEvent.Escape,
                    $"{edge.Event} cleared the selection of {edge.From}");
            }
            else
            {
                Assert.True(
                    edge.Event is SheetEvent.RowTap or SheetEvent.PinTap or SheetEvent.BubbleTap or SheetEvent.HereNowTap,
                    $"{edge.Event} set the selection of {edge.From}");
            }
        }
    }

    [Fact]
    public void HandleEvents_NeverChangeSectionOrSelection_AndDoNothingInExpanded()
    {
        foreach (var edge in SheetWalk.Run().Edges.Where(e => e.Event is SheetEvent.HandleToggle or SheetEvent.HandleSet))
        {
            Assert.Equal(edge.From.Section, edge.To.Section);
            Assert.Equal(edge.From.Selection, edge.To.Selection);
            if (edge.Mode == LayoutMode.Expanded)
            {
                Assert.Equal(edge.From, edge.To);
            }
        }
    }

    [Fact]
    public void Escape_Event_IsTheBackReducersEscape()
    {
        foreach (var (state, mode) in SheetWalk.Run().States)
        {
            Assert.Equal(BackReducer.Escape(state, mode).State, SheetStateMachine.Reduce(state, new SheetEvent.Escape(), mode));
        }
    }

    [Fact]
    public void Reduce_NullEvent_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => SheetStateMachine.Reduce(SheetState.Initial, null!, LayoutMode.Compact));
    }

    // ---- RealmUiState: the reducers behind one object --------------------------------------------------------------------------------------------------------------

    [Fact]
    public void RealmUiState_StartsAsTheAppOpens()
    {
        var ui = new RealmUiState();

        Assert.Equal(SheetState.Initial, ui.Sheet);
        Assert.Equal(Section.Drivers, ui.Section);
        Assert.Null(ui.Selection);
        Assert.Equal(SheetSize.Peek, ui.SheetSize);
        Assert.Equal(0, ui.Depth);
        Assert.Empty(ui.Overlays);
        Assert.Equal(0, ui.SelectedWeek);
        Assert.Null(ui.MeId);
        Assert.Null(ui.LastCamera);
        Assert.Null(ui.FollowMemberId);
    }

    [Fact]
    public void RealmUiState_Apply_RunsTheReducerAndRaisesChangedOnlyForAChange()
    {
        var ui = new RealmUiState { Layout = new LayoutSnapshot(LayoutMode.Compact, false, 412, 915, new Realm.Web.Map.Padding(0, 0, 0, 0)) };
        var raised = 0;
        ui.Changed += () => raised++;

        Assert.True(ui.Apply(new SheetEvent.PinTap(Jester)));
        Assert.Equal(1, raised);
        Assert.Equal(new SheetBody.Header(Jester), ui.Body);
        Assert.Equal(1, ui.Depth);

        Assert.False(ui.Apply(new SheetEvent.PinTap(Jester)));   // the selected pin again
        Assert.Equal(1, raised);

        Assert.True(ui.Apply(new SheetEvent.HandleToggle()));   // the header tap (D46)
        Assert.Equal(2, raised);
        Assert.Equal(new SheetBody.Detail(Jester), ui.Body);
        Assert.Equal(2, ui.Depth);
    }

    [Fact]
    public void RealmUiState_LayoutChange_KeepsTheSelection()
    {
        var ui = new RealmUiState();
        var pad = new Realm.Web.Map.Padding(0, 0, 0, 0);
        ui.Layout = new LayoutSnapshot(LayoutMode.Compact, false, 412, 915, pad);
        ui.Apply(new SheetEvent.PinTap(Jester));
        ui.Apply(new SheetEvent.HandleToggle());

        ui.Layout = new LayoutSnapshot(LayoutMode.Expanded, false, 884, 916, pad);   // unfold (AC-12)

        Assert.Equal(Jester, ui.Selection);
        Assert.Equal(new SheetBody.Detail(Jester), ui.Body);
        Assert.Equal(1, ui.Depth);   // the size term is 0 in Expanded

        ui.Layout = new LayoutSnapshot(LayoutMode.Compact, false, 412, 915, pad);   // fold again

        Assert.Equal(Jester, ui.Selection);
        Assert.Equal(2, ui.Depth);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    public void RealmUiState_SelectedWeek_IsLimitedTo0To3(int week)
    {
        var ui = new RealmUiState();

        Assert.Throws<ArgumentOutOfRangeException>(() => ui.SelectedWeek = week);

        ui.SelectedWeek = 3;
        Assert.Equal(3, ui.SelectedWeek);
    }
}

/// <summary>Walks every state that is reachable from <see cref="SheetState.Initial"/> by the events of <see cref="Events"/> and layout changes, in Compact and in Expanded.</summary>
internal static class SheetWalk
{
    private static readonly EntityRef Jester = new(EntityKind.Member, "jester");
    private static readonly EntityRef King = new(EntityKind.Member, "king");
    private static readonly EntityRef Wagon = new(EntityKind.Vehicle, "wagon");
    private static readonly EntityRef Hall = new(EntityKind.Place, "hall");

    /// <summary>One of every kind of event, with several entities, a single and a cluster bubble, every section and both sizes.</summary>
    public static IReadOnlyList<SheetEvent> Events { get; } =
    [
        new SheetEvent.RowTap(Jester),
        new SheetEvent.RowTap(King),
        new SheetEvent.RowTap(Wagon),
        new SheetEvent.RowTap(Hall),
        new SheetEvent.PinTap(Jester),
        new SheetEvent.PinTap(King),
        new SheetEvent.PinTap(Wagon),
        new SheetEvent.PinTap(Hall),
        new SheetEvent.BubbleTap([King.Id]),
        new SheetEvent.BubbleTap([Jester.Id]),
        new SheetEvent.BubbleTap([King.Id, Jester.Id]),
        new SheetEvent.HereNowTap(Jester.Id),
        new SheetEvent.HereNowTap(King.Id),
        new SheetEvent.SegmentTap(Section.Drivers),
        new SheetEvent.SegmentTap(Section.Vehicles),
        new SheetEvent.SegmentTap(Section.Places),
        new SheetEvent.ClearTap(),
        new SheetEvent.MapTap(),
        new SheetEvent.HandleToggle(),
        new SheetEvent.HandleSet(SheetSize.Peek),
        new SheetEvent.HandleSet(SheetSize.Tall),
        new SheetEvent.Escape(),
        new SheetEvent.NavReTap(),
    ];

    /// <summary>One transition of <see cref="SheetStateMachine.Reduce"/>.</summary>
    public readonly record struct Edge(SheetState From, LayoutMode Mode, SheetEvent Event, SheetState To);

    /// <summary>The reachable <c>(state, layout)</c> pairs and every transition out of them.</summary>
    public sealed record Result(IReadOnlyList<(SheetState State, LayoutMode Mode)> States, IReadOnlyList<Edge> Edges);

    /// <summary>Breadth first from the opening state in Compact and in Expanded (a layout change keeps the state, AC-12).</summary>
    public static Result Run()
    {
        var seen = new HashSet<(SheetState, LayoutMode)>();
        var order = new List<(SheetState State, LayoutMode Mode)>();
        var edges = new List<Edge>();
        var queue = new Queue<(SheetState State, LayoutMode Mode)>();

        void Visit(SheetState state, LayoutMode mode)
        {
            if (seen.Add((state, mode)))
            {
                order.Add((state, mode));
                queue.Enqueue((state, mode));
            }
        }

        Visit(SheetState.Initial, LayoutMode.Compact);
        Visit(SheetState.Initial, LayoutMode.Expanded);
        while (queue.Count > 0)
        {
            var (state, mode) = queue.Dequeue();
            Visit(state, mode == LayoutMode.Compact ? LayoutMode.Expanded : LayoutMode.Compact);
            foreach (var sheetEvent in Events)
            {
                var next = SheetStateMachine.Reduce(state, sheetEvent, mode);
                edges.Add(new Edge(state, mode, sheetEvent, next));
                Visit(next, mode);
            }
        }

        return new Result(order, edges);
    }
}
