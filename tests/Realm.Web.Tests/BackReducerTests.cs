using Realm.Domain;
using Realm.Web.Layout;
using Realm.Web.State;
using Xunit;

namespace Realm.Web.Tests;

/// <summary>
/// <see cref="BackReducer"/> and <see cref="SheetState.Depth"/> (01 section 5.7 Back chain, 03 sections 3.6, 3.7 and 8.3, AC-24, D45, D46): every row of the chain, Esc,
/// the Expanded chain, and the two properties over every reachable state and overlay count 0 to 2 with the history tokens on and off. Pure: no browser, no component.
/// The history is a test-local model of the <c>setDepth</c> contract of 03 section 3.7 (<see cref="FakeHistory"/>); the real helper arrives with the shell script.
/// </summary>
public sealed class BackReducerTests
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

    // ---- the Back chain of 01 section 5.7, one row each --------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("member")]
    [InlineData("vehicle")]
    [InlineData("place")]
    public void Back_DetailAtTall_ReturnsToPeekWithSelectionKept(string kind)
    {
        var entity = Entity(kind);

        var (state, step) = BackReducer.Reduce(At(entity, SheetSize.Tall, Section.Places), LayoutMode.Compact);

        Assert.Equal(BackStep.Detail, step);
        Assert.Equal(new SheetState(Section.Places, entity, SheetSize.Peek), state);   // the selection is kept: the summary header
        Assert.Equal(new SheetBody.Header(entity), SheetBody.BodyOf(state, LayoutMode.Compact));
    }

    [Theory]
    [InlineData("member")]
    [InlineData("vehicle")]
    [InlineData("place")]
    public void Back_Header_ClearsSelectionListAtPeek(string kind)
    {
        var (state, step) = BackReducer.Reduce(At(Entity(kind), SheetSize.Peek, Section.Vehicles), LayoutMode.Compact);

        Assert.Equal(BackStep.Selection, step);
        Assert.Equal(At(null, SheetSize.Peek, Section.Vehicles), state);
        Assert.IsType<SheetBody.List>(SheetBody.BodyOf(state, LayoutMode.Compact));
    }

    [Theory]
    [InlineData(Section.Drivers)]
    [InlineData(Section.Vehicles)]
    [InlineData(Section.Places)]
    public void Back_ListAtTall_CollapsesToPeek(Section section)
    {
        var (state, step) = BackReducer.Reduce(At(null, SheetSize.Tall, section), LayoutMode.Compact);

        Assert.Equal(BackStep.List, step);
        Assert.Equal(At(null, SheetSize.Peek, section), state);
    }

    [Theory]
    [InlineData(Section.Drivers)]
    [InlineData(Section.Vehicles)]
    [InlineData(Section.Places)]
    public void Back_ListAtPeek_Leaves(Section section)
    {
        var from = At(null, SheetSize.Peek, section);

        var (state, step) = BackReducer.Reduce(from, LayoutMode.Compact);

        Assert.Equal(BackStep.Leave, step);
        Assert.Equal(from, state);   // no state change; the browser leaves
    }

    [Theory]
    [InlineData(1, false, SheetSize.Peek)]
    [InlineData(1, false, SheetSize.Tall)]
    [InlineData(1, true, SheetSize.Peek)]
    [InlineData(1, true, SheetSize.Tall)]
    [InlineData(2, true, SheetSize.Tall)]
    [InlineData(2, false, SheetSize.Peek)]
    public void Back_OverlayFirst(int overlays, bool selected, SheetSize size)
    {
        var from = At(selected ? Jester : null, size);

        foreach (var mode in new[] { LayoutMode.Compact, LayoutMode.Expanded })
        {
            var (state, step) = BackReducer.Reduce(from, mode, overlays);

            Assert.Equal(BackStep.Overlay, step);   // an open overlay comes before every sheet step; the sheet state is the overlay stack's business, not touched here
            Assert.Equal(from, state);
            Assert.Equal(from.Depth(mode, overlays) - 1, from.Depth(mode, overlays - 1));
        }
    }

    [Theory]
    [InlineData(SheetSize.Peek)]
    [InlineData(SheetSize.Tall)]
    public void Esc_RunsOnlySteps2And3(SheetSize size)
    {
        // Detail at Tall: step 2. Header at Peek: step 3.
        Assert.Equal((At(Jester, SheetSize.Peek), (BackStep?)BackStep.Detail), BackReducer.Escape(At(Jester, SheetSize.Tall), LayoutMode.Compact));
        Assert.Equal((At(null, SheetSize.Peek), (BackStep?)BackStep.Selection), BackReducer.Escape(At(Jester, SheetSize.Peek), LayoutMode.Compact));

        // A list is never collapsed and Esc never leaves: no step, the same state.
        var list = At(null, size, Section.Places);
        Assert.Equal((list, (BackStep?)null), BackReducer.Escape(list, LayoutMode.Compact));
        Assert.Equal((list, (BackStep?)null), BackReducer.Escape(list, LayoutMode.Expanded));
    }

    [Theory]
    [InlineData(SheetSize.Peek)]
    [InlineData(SheetSize.Tall)]
    public void Esc_InExpanded_RunsTheSelectionStepOnly(SheetSize size)
    {
        Assert.Equal((At(null, size), (BackStep?)BackStep.Selection), BackReducer.Escape(At(Jester, size), LayoutMode.Expanded));
    }

    [Fact]
    public void Back_TheCompactChain_FromTheDetail_IsDetailSelectionLeave()
    {
        var state = At(Jester, SheetSize.Tall);
        var steps = new List<BackStep>();
        var depths = new List<int> { state.Depth(LayoutMode.Compact) };

        for (var i = 0; i < 4 && steps.LastOrDefault() != BackStep.Leave; i++)
        {
            (state, var step) = BackReducer.Reduce(state, LayoutMode.Compact);
            steps.Add(step);
            depths.Add(state.Depth(LayoutMode.Compact));
        }

        Assert.Equal(new[] { BackStep.Detail, BackStep.Selection, BackStep.Leave }, steps);
        Assert.Equal(new[] { 2, 1, 0, 0 }, depths);
        Assert.Equal(At(null, SheetSize.Peek), state);
    }

    [Fact]
    public void Back_ARowTappedAtTall_ThenBack_IsTheListAtPeek_AndTheNextBackLeaves()
    {
        // AC-24: a row tapped at Tall collapses to the header (D45); Back clears the selection to the list at Peek; Back leaves.
        var tall = At(null, SheetSize.Tall);
        var header = SheetStateMachine.Reduce(tall, new SheetEvent.RowTap(Jester), LayoutMode.Compact);

        var (list, step) = BackReducer.Reduce(header, LayoutMode.Compact);
        Assert.Equal(BackStep.Selection, step);
        Assert.Equal(At(null, SheetSize.Peek), list);

        var (final, leave) = BackReducer.Reduce(list, LayoutMode.Compact);
        Assert.Equal(BackStep.Leave, leave);
        Assert.Equal(list, final);
    }

    [Fact]
    public void Back_ListAtTallWithNothingSelected_IsPeekThenLeaves()
    {
        var (peek, first) = BackReducer.Reduce(At(null, SheetSize.Tall), LayoutMode.Compact);
        var (_, second) = BackReducer.Reduce(peek, LayoutMode.Compact);

        Assert.Equal(BackStep.List, first);
        Assert.Equal(BackStep.Leave, second);
    }

    [Theory]
    [InlineData(SheetSize.Peek)]
    [InlineData(SheetSize.Tall)]
    public void Back_TheExpandedChain_IsDetailToListThenLeave_AndKeepsTheSize(SheetSize size)
    {
        var (list, first) = BackReducer.Reduce(At(Jester, size, Section.Places), LayoutMode.Expanded);
        var (same, second) = BackReducer.Reduce(list, LayoutMode.Expanded);

        Assert.Equal(BackStep.Selection, first);
        Assert.Equal(At(null, size, Section.Places), list);
        Assert.Equal(BackStep.Leave, second);   // a list at Tall is not collapsed: the panel has no size
        Assert.Equal(list, same);
    }

    [Fact]
    public void Back_LayoutUnknown_IsTheCompactChain()
    {
        Assert.Equal(BackReducer.Reduce(At(Jester, SheetSize.Tall), LayoutMode.Compact), BackReducer.Reduce(At(Jester, SheetSize.Tall), LayoutMode.Unknown));
        Assert.Equal(At(Jester, SheetSize.Tall).Depth(LayoutMode.Compact), At(Jester, SheetSize.Tall).Depth(LayoutMode.Unknown));
    }

    [Fact]
    public void Back_ANegativeOverlayCount_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BackReducer.Reduce(SheetState.Initial, LayoutMode.Compact, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => SheetState.Initial.Depth(LayoutMode.Compact, -1));
    }

    // ---- D46: the header tap opens the detail; Back returns to the header --------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Back_AHeaderOpenedDetail_HasDepth2_AndBackReturnsToDepth1WithTheSelectionKept(bool historyTokens)
    {
        var history = new FakeHistory(historyTokens);

        var header = SheetStateMachine.Reduce(SheetState.Initial, new SheetEvent.PinTap(Jester), LayoutMode.Compact);
        history.SetDepth(header.Depth(LayoutMode.Compact));
        Assert.Equal(1, history.Recorded);

        // A tap on the Peek header body raises the same event as the handle (D46).
        var detail = SheetStateMachine.Reduce(header, new SheetEvent.HandleToggle(), LayoutMode.Compact);
        history.SetDepth(detail.Depth(LayoutMode.Compact));
        Assert.Equal(new SheetBody.Detail(Jester), SheetBody.BodyOf(detail, LayoutMode.Compact));
        Assert.Equal(2, detail.Depth(LayoutMode.Compact));
        Assert.Equal(historyTokens ? 2 : 0, history.Entries);

        if (historyTokens)
        {
            history.UserPop();   // the Android Back popped the entry of the detail
        }

        var (back, step) = BackReducer.Reduce(detail, LayoutMode.Compact);
        var callsBefore = history.Calls;
        history.SetDepth(back.Depth(LayoutMode.Compact));

        Assert.Equal(BackStep.Detail, step);
        Assert.Equal(1, back.Depth(LayoutMode.Compact));
        Assert.Equal(Jester, back.Selection);
        Assert.Equal(SheetSize.Peek, back.Size);
        Assert.Equal(new SheetBody.Header(Jester), SheetBody.BodyOf(back, LayoutMode.Compact));
        Assert.Equal(callsBefore, history.Calls);   // the popped entry already matches: the sync pushes and pops nothing more
        Assert.Equal(1, history.Recorded);
        Assert.Equal(historyTokens ? 1 : 0, history.Entries);
    }

    // ---- depth (03 section 3.7) ----------------------------------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(false, SheetSize.Peek, LayoutMode.Compact, 0, 0)]
    [InlineData(false, SheetSize.Tall, LayoutMode.Compact, 0, 1)]
    [InlineData(true, SheetSize.Peek, LayoutMode.Compact, 0, 1)]
    [InlineData(true, SheetSize.Tall, LayoutMode.Compact, 0, 2)]
    [InlineData(false, SheetSize.Tall, LayoutMode.Expanded, 0, 0)]
    [InlineData(true, SheetSize.Tall, LayoutMode.Expanded, 0, 1)]
    [InlineData(true, SheetSize.Peek, LayoutMode.Expanded, 0, 1)]
    [InlineData(false, SheetSize.Peek, LayoutMode.Compact, 1, 1)]
    [InlineData(true, SheetSize.Tall, LayoutMode.Compact, 2, 4)]
    public void Depth_IsOverlaysPlusSelectionPlusANonPeekCompactSize(bool selected, SheetSize size, LayoutMode mode, int overlays, int expected) =>
        Assert.Equal(expected, At(selected ? Jester : null, size).Depth(mode, overlays));

    // ---- properties over every reachable state and overlay count 0 to 2 ------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Back_LowersDepthByExactlyOne(bool historyTokens)
    {
        var checkedStates = 0;
        foreach (var (state, mode) in SheetWalk.Run().States)
        {
            for (var overlays = 0; overlays <= 2; overlays++)
            {
                var before = state.Depth(mode, overlays);
                var history = new FakeHistory(historyTokens);
                history.SetDepth(before);
                var leaves = before == 0;
                if (historyTokens && !leaves)
                {
                    history.UserPop();   // one Back gesture pops one entry; at depth 0 the browser leaves instead
                }

                var (next, step) = BackReducer.Reduce(state, mode, overlays);
                var afterOverlays = step == BackStep.Overlay ? overlays - 1 : overlays;
                var after = next.Depth(mode, afterOverlays);
                var callsBefore = history.Calls;
                history.SetDepth(after);

                var where = $"{state} {mode} overlays {overlays}";
                Assert.Equal(leaves, step == BackStep.Leave);   // Leave exactly at depth 0
                Assert.Equal(leaves ? before : before - 1, after);   // every step except Leave lowers it by exactly 1
                if (leaves)
                {
                    Assert.Equal(state, next);
                }

                Assert.True(callsBefore == history.Calls, $"the sync after Back pushed or popped again: {where}");
                Assert.Equal(after, history.Recorded);
                Assert.Equal(historyTokens ? after : 0, history.Entries);
                checkedStates++;
            }
        }

        Assert.Equal(60 * 3, checkedStates);
    }

    [Fact]
    public void Back_ReachesLeaveInExactlyDepthSteps_InTheOrderOfTheTable()
    {
        foreach (var (state, mode) in SheetWalk.Run().States)
        {
            for (var overlays = 0; overlays <= 2; overlays++)
            {
                var expected = Enumerable.Repeat(BackStep.Overlay, overlays).Concat(ExpectedSheetSteps(state, mode)).ToList();
                Assert.Equal(state.Depth(mode, overlays), expected.Count);

                var current = state;
                var left = overlays;
                var actual = new List<BackStep>();
                for (var i = 0; i < expected.Count; i++)
                {
                    var (next, step) = BackReducer.Reduce(current, mode, left);
                    actual.Add(step);
                    current = next;
                    left = step == BackStep.Overlay ? left - 1 : left;
                }

                Assert.Equal(expected, actual);
                Assert.Equal(BackStep.Leave, BackReducer.Reduce(current, mode, left).Step);
                Assert.Equal(0, current.Depth(mode, left));
            }
        }
    }

    private static IEnumerable<BackStep> ExpectedSheetSteps(SheetState state, LayoutMode mode)
    {
        var selected = state.Selection is not null;
        if (mode == LayoutMode.Expanded)
        {
            return selected ? [BackStep.Selection] : [];
        }

        return (selected, state.Size) switch
        {
            (true, SheetSize.Tall) => [BackStep.Detail, BackStep.Selection],
            (true, _) => [BackStep.Selection],
            (false, SheetSize.Tall) => [BackStep.List],
            _ => [],
        };
    }

    [Fact]
    public void Esc_NeverCollapsesAListNorLeaves_AndLowersDepthByOneWhenItSteps()
    {
        foreach (var (state, mode) in SheetWalk.Run().States)
        {
            var (next, step) = BackReducer.Escape(state, mode);

            if (state.Selection is null)
            {
                Assert.Null(step);
                Assert.Equal(state, next);
            }
            else
            {
                Assert.True(step is BackStep.Detail or BackStep.Selection, $"{state} {mode} took {step}");
                Assert.Equal(state.Depth(mode) - 1, next.Depth(mode));
            }
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RowTapAtTall_KeepsDepth(bool historyTokens)
    {
        // A row (or a pin) tapped while the list is at Tall: the selection adds one level, the collapse removes one, so depth 1 stays 1 and nothing is pushed or popped.
        foreach (var entity in new[] { Jester, Wagon, Hall })
        {
            foreach (SheetEvent select in new SheetEvent[] { new SheetEvent.RowTap(entity), new SheetEvent.PinTap(entity) })
            {
                for (var overlays = 0; overlays <= 2; overlays++)
                {
                    var list = At(null, SheetSize.Tall, Section.Places);
                    var history = new FakeHistory(historyTokens);
                    history.SetDepth(list.Depth(LayoutMode.Compact, overlays));
                    var callsBefore = history.Calls;

                    var header = SheetStateMachine.Reduce(list, select, LayoutMode.Compact);
                    history.SetDepth(header.Depth(LayoutMode.Compact, overlays));

                    Assert.Equal(1 + overlays, list.Depth(LayoutMode.Compact, overlays));
                    Assert.Equal(list.Depth(LayoutMode.Compact, overlays), header.Depth(LayoutMode.Compact, overlays));
                    Assert.Equal(callsBefore, history.Calls);
                    Assert.Equal(1 + overlays, history.Recorded);
                    Assert.Equal(historyTokens ? 1 + overlays : 0, history.Entries);

                    var (cleared, step) = BackReducer.Reduce(header, LayoutMode.Compact, 0);   // the next Back still clears the selection
                    if (overlays == 0)
                    {
                        Assert.Equal(BackStep.Selection, step);
                        Assert.Equal(At(null, SheetSize.Peek, Section.Places), cleared);
                    }
                }
            }
        }
    }

    [Fact]
    public void SelectionTransitions_MoveTheDepthAsSection37Says()
    {
        // Selecting from the list at Peek 0 to 1 (a push), a handle tap on the selection 1 to 2, a Here-now row at Tall 2 to 1 (a pop).
        var list = At(null, SheetSize.Peek, Section.Places);
        var header = SheetStateMachine.Reduce(list, new SheetEvent.RowTap(Hall), LayoutMode.Compact);
        var detail = SheetStateMachine.Reduce(header, new SheetEvent.HandleToggle(), LayoutMode.Compact);
        var other = SheetStateMachine.Reduce(detail, new SheetEvent.HereNowTap(Jester.Id), LayoutMode.Compact);

        Assert.Equal(new[] { 0, 1, 2, 1 }, new[] { list, header, detail, other }.Select(s => s.Depth(LayoutMode.Compact)));
        Assert.Equal(new SheetBody.Header(Jester), SheetBody.BodyOf(other, LayoutMode.Compact));
    }

    // ---- RealmUiState: the overlay stack in front of the reducer --------------------------------------------------------------------------------------------------------

    private static RealmUiState Compact()
    {
        var ui = new RealmUiState { Layout = new LayoutSnapshot(LayoutMode.Compact, false, 412, 915, new Realm.Web.Map.Padding(0, 0, 0, 0)) };
        return ui;
    }

    [Fact]
    public void RealmUiState_Back_ClosesTheTopmostOverlayFirst_ThenRunsTheChain()
    {
        var ui = Compact();
        ui.Apply(new SheetEvent.PinTap(Jester));
        ui.Apply(new SheetEvent.HandleToggle());
        ui.OpenOverlay(new OverlayLayer(OverlayKind.Settings));
        ui.OpenOverlay(new OverlayLayer(OverlayKind.Popover, "style"));
        var raised = 0;
        ui.Changed += () => raised++;

        var steps = new List<BackStep>();
        var depths = new List<int> { ui.Depth };
        for (var i = 0; i < 5; i++)
        {
            steps.Add(ui.Back());
            depths.Add(ui.Depth);
        }

        Assert.Equal(new[] { BackStep.Overlay, BackStep.Overlay, BackStep.Detail, BackStep.Selection, BackStep.Leave }, steps);
        Assert.Equal(new[] { 4, 3, 2, 1, 0, 0 }, depths);
        Assert.Equal(4, raised);   // Leave changes nothing and raises nothing
        Assert.Empty(ui.Overlays);
        Assert.Equal(SheetState.Initial, ui.Sheet);
    }

    [Fact]
    public void RealmUiState_Escape_ClosesAnOverlayFirst_ThenRunsSteps2And3Only()
    {
        var ui = Compact();
        ui.Apply(new SheetEvent.PinTap(Jester));
        ui.Apply(new SheetEvent.HandleToggle());
        ui.OpenOverlay(new OverlayLayer(OverlayKind.Dialog));

        Assert.Equal(BackStep.Overlay, ui.Escape());
        Assert.Equal(BackStep.Detail, ui.Escape());
        Assert.Equal(new SheetBody.Header(Jester), ui.Body);
        Assert.Equal(BackStep.Selection, ui.Escape());

        ui.Apply(new SheetEvent.HandleToggle());   // the list at Tall: Esc never collapses it and never leaves
        var raised = 0;
        ui.Changed += () => raised++;
        Assert.Null(ui.Escape());
        Assert.Equal(SheetSize.Tall, ui.SheetSize);
        Assert.Equal(0, raised);
    }

    [Fact]
    public void RealmUiState_CloseOverlay_ClosesItByItsOwnButton()
    {
        var ui = Compact();
        var settings = new OverlayLayer(OverlayKind.Settings);
        var popover = new OverlayLayer(OverlayKind.Popover, "style");
        ui.OpenOverlay(settings);
        ui.OpenOverlay(popover);

        Assert.True(ui.CloseOverlay(settings));
        Assert.Equal(new[] { popover }, ui.Overlays);
        Assert.False(ui.CloseOverlay(settings));
        Assert.Equal(1, ui.Depth);
    }
}

/// <summary>
/// A model of the <c>setDepth(n)</c> contract of 03 section 3.7: with the history tokens on it pushes the missing entries or goes back by the surplus; with
/// <c>HistoryTokens = false</c> it only records the target and never touches the history, so the Android Back simply leaves the app. <see cref="Calls"/> counts every
/// push and every go, so a test can assert that a transition pushed or popped nothing.
/// </summary>
internal sealed class FakeHistory(bool tokens)
{
    /// <summary>The entries this document has pushed above its base entry.</summary>
    public int Entries { get; private set; }

    /// <summary>The depth the last <see cref="SetDepth"/> recorded.</summary>
    public int Recorded { get; private set; }

    /// <summary>The number of <c>pushState</c> and <c>history.go</c> calls so far.</summary>
    public int Calls { get; private set; }

    public void SetDepth(int depth)
    {
        if (tokens && depth > Entries)
        {
            Calls += depth - Entries;
            Entries = depth;
        }
        else if (tokens && depth < Entries)
        {
            Calls++;
            Entries = depth;
        }

        Recorded = depth;
    }

    /// <summary>The Back gesture popped one entry (only with the tokens on; without them nothing was pushed to pop).</summary>
    public void UserPop()
    {
        Assert.True(tokens);
        Assert.True(Entries > 0);
        Entries--;
    }
}
