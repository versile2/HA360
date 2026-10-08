using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Realm.Demo;
using Realm.Domain;
using Realm.Web.Components.Shell;
using Realm.Web.Components.Sheet;
using Realm.Web.Layout;
using Realm.Web.Map;
using Xunit;

namespace Realm.Web.Tests;

/// <summary>
/// The pieces of the sheet that do not depend on MudX: the handle button (<see cref="SheetHandle"/>), the three-tab control (<see cref="SheetSegments"/>), the list
/// body (<see cref="SheetContent"/>: its frame here, its rows in <c>SheetContentTests</c>) and the right button stack (<see cref="RightButtonStack"/>). They render in plain bUnit with the
/// canonical setup; what they look like on screen belongs to the Playwright specs.
/// </summary>
public sealed class SheetPartsTests : ComponentTestBase
{
    private static readonly RealmSnapshot Demo = FullCast.Session(null).Current;

    // ---- the handle ------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Handle_IsOneButton_WithTheSummary_AndTheStateInTheAccessibleName()
    {
        var cut = RenderWithProviders<SheetHandle>(handle => handle.Add(p => p.Summary, "4 in the Realm · 1 driving"));

        var button = cut.Find("button[data-testid='sheet-handle']");
        Assert.Equal("button", button.GetAttribute("type"));
        Assert.Equal("Resize list. Currently peek height.", button.GetAttribute("aria-label"));
        Assert.Equal("false", button.GetAttribute("aria-expanded"));
        Assert.Equal("4 in the Realm · 1 driving", cut.Find("[data-testid='sheet-summary']").TextContent);
        Assert.Single(cut.FindAll("button"));
    }

    [Fact]
    public void Handle_AtTall_IsExpanded_AndSaysEightyPercent()
    {
        var cut = RenderWithProviders<SheetHandle>(handle => handle.Add(p => p.Size, SheetSize.Tall));

        var button = cut.Find("[data-testid='sheet-handle']");
        Assert.Equal("true", button.GetAttribute("aria-expanded"));
        Assert.Equal("Resize list. Currently 80 percent height.", button.GetAttribute("aria-label"));
    }

    [Fact]
    public async Task Handle_ATap_AsksForTheToggle_Once()
    {
        var toggles = 0;
        var cut = RenderWithProviders<SheetHandle>(handle => handle.Add(p => p.OnToggle, () =>
        {
            toggles++;
            return Task.CompletedTask;
        }));

        await cut.Find("[data-testid='sheet-handle']").TriggerEventAsync("onclick", new MouseEventArgs());

        Assert.Equal(1, toggles);
    }

    [Theory]
    [InlineData("ArrowUp", SheetSize.Tall)]
    [InlineData("End", SheetSize.Tall)]
    [InlineData("ArrowDown", SheetSize.Peek)]
    [InlineData("Home", SheetSize.Peek)]
    public async Task Handle_TheKeys_AskForTheStateTheyNameOf(string key, SheetSize expected)
    {
        var requests = new List<SheetSize>();
        var cut = RenderWithProviders<SheetHandle>(handle => handle.Add(p => p.OnSet, (SheetSize size) =>
        {
            requests.Add(size);
            return Task.CompletedTask;
        }));

        await cut.Find("[data-testid='sheet-handle']").TriggerEventAsync("onkeydown", new KeyboardEventArgs { Key = key });

        Assert.Equal([expected], requests);
    }

    [Theory]
    [InlineData("a")]
    [InlineData("ArrowLeft")]
    [InlineData("Tab")]
    public async Task Handle_OtherKeys_AskForNothing(string key)
    {
        var requests = new List<SheetSize>();
        var cut = RenderWithProviders<SheetHandle>(handle => handle.Add(p => p.OnSet, (SheetSize size) =>
        {
            requests.Add(size);
            return Task.CompletedTask;
        }));

        await cut.Find("[data-testid='sheet-handle']").TriggerEventAsync("onkeydown", new KeyboardEventArgs { Key = key });

        Assert.Empty(requests);
    }

    [Fact]
    public void Handle_IsAPlainButton_ATapIsItsClick_WithNoPointerHandlingOfItsOwn()
    {
        // D73: there is no drag, so no pointer capture and no pointer events here: the toggle is the browser's click (and the keys), nothing else.
        var markup = RenderWithProviders<SheetHandle>().Markup;

        Assert.DoesNotContain("pointer", markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stoppropagation", markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Handle_ARequestForTheHandle_TakesTheFocus_OncePerRequest()
    {
        var request = new FocusRequest(FocusTarget.Handle);
        var cut = RenderWithProviders<SheetHandle>(handle => handle.Add(p => p.Focus, request));

        cut.WaitForAssertion(() => Assert.Equal(1, FocusCalls()));

        // The page renders again (a tick of the minute, a change of the lists) with the request it has not yet dropped: nothing moves.
        cut.Render(handle => handle.Add(p => p.Summary, "5 in the Realm"));
        cut.Render(handle => handle.Add(p => p.Focus, request));
        Assert.Equal(1, FocusCalls());

        // A new request is a new ask, though it names the same target.
        cut.Render(handle => handle.Add(p => p.Focus, new FocusRequest(FocusTarget.Handle)));
        cut.WaitForAssertion(() => Assert.Equal(2, FocusCalls()));
    }

    [Theory]
    [InlineData(FocusTarget.None)]
    [InlineData(FocusTarget.SectionTab)]
    public void Handle_ARequestForSomethingElse_MovesNothing(FocusTarget target)
    {
        var cut = RenderWithProviders<SheetHandle>(handle => handle.Add(p => p.Focus, new FocusRequest(target)));

        Assert.Single(cut.FindAll("button"));
        AssertNothingFocused();
    }

    [Fact]
    public void Handle_WithNoRequest_NeverTakesTheFocus()
    {
        RenderWithProviders<SheetHandle>();

        AssertNothingFocused();
    }

    // ---- the three tabs --------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Segments_AreThreeTabs_DriversFirstAndSelected_WithARovingTabindex()
    {
        var cut = Segments(Section.Drivers);

        var tabs = cut.FindAll("[role='tab']");
        Assert.Equal(["tab-drivers", "tab-vehicles", "tab-places"], tabs.Select(tab => tab.GetAttribute("data-testid")));
        Assert.Equal(["Drivers", "Trackers", "Places"], tabs.Select(tab => tab.QuerySelector(".realm-segment-label")!.TextContent));
        Assert.Equal(["true", "false", "false"], tabs.Select(tab => tab.GetAttribute("aria-selected")));
        Assert.Equal(["0", "-1", "-1"], tabs.Select(tab => tab.GetAttribute("tabindex")));
        Assert.Equal("tablist", cut.Find("[data-testid='sheet-segments']").GetAttribute("role"));
        Assert.All(tabs, tab => Assert.Equal(SheetSegments.PanelId, tab.GetAttribute("aria-controls")));
    }

    [Theory]
    [InlineData(Section.Vehicles, "tab-vehicles")]
    [InlineData(Section.Places, "tab-places")]
    public void Segments_TheSelectedSection_IsTheOnlyOneInTheTabOrder(Section section, string selectedTestId)
    {
        var cut = Segments(section);

        var tabs = cut.FindAll("[role='tab']");
        var selected = Assert.Single(tabs, tab => tab.GetAttribute("aria-selected") == "true");
        Assert.Equal(selectedTestId, selected.GetAttribute("data-testid"));
        Assert.Equal("0", selected.GetAttribute("tabindex"));
        Assert.Single(tabs, tab => tab.GetAttribute("tabindex") == "0");
    }

    [Fact]
    public void Segments_HaveNoLiveRegion_TheSheetContentHoldsTheOne()
    {
        // The count of a section is announced by the sheet's one live region (SheetContent), which stays while the selection header takes the tabs' place.
        var cut = Segments(Section.Drivers);

        Assert.Empty(cut.FindAll("[aria-live]"));
        Assert.Empty(cut.FindAll("[data-testid='sheet-announce']"));
    }

    [Fact]
    public void Segments_ARequestForTheSectionTab_TakesTheFocus_OncePerRequest()
    {
        var request = new FocusRequest(FocusTarget.SectionTab);
        var cut = Segments(Section.Vehicles, focus: request);

        cut.WaitForAssertion(() => Assert.Equal(1, FocusCalls()));

        // The page renders again with the request it has not yet dropped: nothing moves. A new request is a new ask, though it names the same target.
        cut.Render(segments => segments.Add(p => p.Focus, request));
        Assert.Equal(1, FocusCalls());
        cut.Render(segments => segments.Add(p => p.Focus, new FocusRequest(FocusTarget.SectionTab)));
        cut.WaitForAssertion(() => Assert.Equal(2, FocusCalls()));
    }

    [Theory]
    [InlineData(FocusTarget.None)]
    [InlineData(FocusTarget.Handle)]
    public void Segments_ARequestForSomethingElse_MovesNothing(FocusTarget target)
    {
        var cut = Segments(Section.Drivers, focus: new FocusRequest(target));

        Assert.NotEmpty(cut.FindAll("[role='tab']"));
        AssertNothingFocused();
    }

    [Theory]
    [InlineData("tab-places", Section.Places)]
    [InlineData("tab-vehicles", Section.Vehicles)]
    [InlineData("tab-drivers", Section.Drivers)]
    public async Task Segments_ATap_ChoosesTheTab(string testId, Section expected)
    {
        var chosen = new List<Section>();
        var cut = Segments(Section.Drivers, onSection: chosen.Add);

        await cut.Find($"[data-testid='{testId}']").TriggerEventAsync("onclick", new MouseEventArgs());

        Assert.Equal([expected], chosen);
    }

    [Theory]
    [InlineData(Section.Drivers, "ArrowRight", Section.Vehicles)]
    [InlineData(Section.Vehicles, "ArrowRight", Section.Places)]
    [InlineData(Section.Places, "ArrowRight", Section.Drivers)]   // wraps
    [InlineData(Section.Drivers, "ArrowLeft", Section.Places)]    // wraps
    [InlineData(Section.Places, "ArrowLeft", Section.Vehicles)]
    [InlineData(Section.Vehicles, "Home", Section.Drivers)]
    [InlineData(Section.Drivers, "End", Section.Places)]
    public async Task Segments_TheArrowKeys_MoveToTheNeighbour_AndFocusFollows(Section from, string key, Section expected)
    {
        var chosen = new List<Section>();
        var cut = Segments(from, onSection: chosen.Add);

        await cut.Find($"[data-testid='tab-{from.ToString().ToLowerInvariant()}']").TriggerEventAsync("onkeydown", new KeyboardEventArgs { Key = key });

        Assert.Equal([expected], chosen);
        JSInterop.VerifyFocusAsyncInvoke();
    }

    [Fact]
    public async Task Segments_OtherKeys_ChooseNothing()
    {
        var chosen = new List<Section>();
        var cut = Segments(Section.Drivers, onSection: chosen.Add);

        await cut.Find("[data-testid='tab-drivers']").TriggerEventAsync("onkeydown", new KeyboardEventArgs { Key = "a" });
        await cut.Find("[data-testid='tab-drivers']").TriggerEventAsync("onkeydown", new KeyboardEventArgs { Key = "Enter" });

        Assert.Empty(chosen);
    }

    // ---- the list body, from the Demo cast -------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(Section.Drivers, "realm-tab-drivers")]
    [InlineData(Section.Vehicles, "realm-tab-vehicles")]
    [InlineData(Section.Places, "realm-tab-places")]
    public void Content_TheListIsATabpanel_LabelledByTheSelectedTab(Section section, string tabId)
    {
        var cut = Content(section);

        var panel = cut.Find($"#{SheetSegments.PanelId}");
        Assert.Equal("tabpanel", panel.GetAttribute("role"));
        Assert.Equal(tabId, panel.GetAttribute("aria-labelledby"));
        Assert.NotNull(cut.Find($"#{tabId}"));
    }

    [Fact]
    public void Content_TheSummaryIsInTheBodyOnlyInThePanel_WhereThereIsNoHandle()
    {
        Assert.Empty(Content(Section.Drivers).FindAll("[data-testid='sheet-summary']"));

        var panel = RenderWithProviders<SheetContent>(content => content
            .Add(p => p.Members, Demo.Members)
            .Add(p => p.Vehicles, Demo.Vehicles)
            .Add(p => p.Places, Demo.Places)
            .Add(p => p.Summary, "4 in the Realm · 1 driving")
            .Add(p => p.ShowSummary, true));

        Assert.Equal("4 in the Realm · 1 driving", panel.Find("[data-testid='sheet-summary']").TextContent);
    }

    [Fact]
    public void Content_NothingYet_IsAnEmptyList_NotAnError()
    {
        var cut = RenderWithProviders<SheetContent>();

        Assert.Empty(cut.FindAll("[data-testid='sheet-list'] li"));
        Assert.Equal(string.Empty, cut.Find("[data-testid='sheet-announce']").TextContent);
    }

    // ---- the right button stack ------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Stack_IsRecenterAboveLayers_AndTheReservedSlotRendersNothing()
    {
        var cut = RenderWithProviders<RightButtonStack>();

        var buttons = cut.FindAll(".realm-right-stack button");
        Assert.Equal(["btn-recenter", "btn-layers"], buttons.Select(button => button.GetAttribute("data-testid")));
        Assert.Empty(cut.FindAll("[data-testid='slot-add']"));
        Assert.Null(cut.Find(".realm-right-stack").GetAttribute("inert"));
        Assert.Equal("realm-right-stack", cut.Find(".realm-right-stack").ClassName);
    }

    [Theory]
    [InlineData(RecenterState.Default, "Zoom to me only")]
    [InlineData(RecenterState.Away, "Show me and nearby members")]
    public void Stack_TheRecenterNameFollowsTheCameraState(RecenterState state, string expected)
    {
        var cut = RenderWithProviders<RightButtonStack>(stack => stack.Add(p => p.Recenter, state));

        Assert.Equal(expected, cut.Find("[data-testid='btn-recenter']").GetAttribute("aria-label"));
    }

    [Fact]
    public void Stack_TheRecenterIcon_DiffersBetweenAwayAndTheDefaultView()
    {
        var atDefault = RenderWithProviders<RightButtonStack>(stack => stack.Add(p => p.Recenter, RecenterState.Default));
        var away = RenderWithProviders<RightButtonStack>(stack => stack.Add(p => p.Recenter, RecenterState.Away));

        Assert.NotEqual(
            atDefault.Find("[data-testid='btn-recenter'] svg").InnerHtml,
            away.Find("[data-testid='btn-recenter'] svg").InnerHtml);
    }

    [Theory]
    [InlineData("Night", "Map style, currently Night.")]
    [InlineData("Satellite", "Map style, currently Satellite.")]
    public void Stack_TheLayersNameSaysTheStyle(string style, string expected)
    {
        var cut = RenderWithProviders<RightButtonStack>(stack => stack.Add(p => p.MapStyleName, style));

        Assert.Equal(expected, cut.Find("[data-testid='btn-layers']").GetAttribute("aria-label"));
    }

    [Fact]
    public void Stack_WhileTall_IsHiddenAndInert_SoItLeavesTheFocusOrder()
    {
        var cut = RenderWithProviders<RightButtonStack>(stack => stack.Add(p => p.Hidden, true));

        var root = cut.Find(".realm-right-stack");
        Assert.Contains("realm-right-stack--hidden", root.ClassList);
        Assert.NotNull(root.GetAttribute("inert"));
    }

    [Fact]
    public void Stack_InThePanel_IsNeverHidden()
    {
        var cut = RenderWithProviders<RightButtonStack>(stack => stack.Add(p => p.Panel, true).Add(p => p.Hidden, true));

        var root = cut.Find(".realm-right-stack");
        Assert.Contains("realm-right-stack--panel", root.ClassList);
        Assert.DoesNotContain("realm-right-stack--hidden", root.ClassList);
        Assert.Null(root.GetAttribute("inert"));
    }

    [Fact]
    public async Task Stack_TheButtonsRaiseTheirCallbacks_AndDoNothingWithout()
    {
        var recenters = 0;
        var layers = 0;
        var cut = RenderWithProviders<RightButtonStack>(stack => stack
            .Add(p => p.OnRecenter, () =>
            {
                recenters++;
                return Task.CompletedTask;
            })
            .Add(p => p.OnLayers, () =>
            {
                layers++;
                return Task.CompletedTask;
            }));

        await cut.Find("[data-testid='btn-recenter']").TriggerEventAsync("onclick", new MouseEventArgs());
        await cut.Find("[data-testid='btn-layers']").TriggerEventAsync("onclick", new MouseEventArgs());
        await cut.Find("[data-testid='btn-layers']").TriggerEventAsync("onclick", new MouseEventArgs());

        Assert.Equal(1, recenters);
        Assert.Equal(2, layers);

        var bare = RenderWithProviders<RightButtonStack>();
        await bare.Find("[data-testid='btn-recenter']").TriggerEventAsync("onclick", new MouseEventArgs());
        await bare.Find("[data-testid='btn-layers']").TriggerEventAsync("onclick", new MouseEventArgs());
    }

    // ---- helpers ---------------------------------------------------------------------------------------------------------------------------

    // ElementReference.FocusAsync reaches the browser as this call; bUnit's own VerifyFocusAsyncInvoke counts it too, and raises when it was never made.
    private const string FocusCall = "Blazor._internal.domWrapper.focus";

    private int FocusCalls() => JSInterop.Invocations[FocusCall].Count();

    private void AssertNothingFocused() => Assert.ThrowsAny<Exception>(() => JSInterop.VerifyFocusAsyncInvoke());

    private IRenderedComponent<SheetSegments> Segments(Section section, Action<Section>? onSection = null, FocusRequest? focus = null) =>
        RenderWithProviders<SheetSegments>(segments =>
        {
            segments.Add(p => p.Section, section);
            if (focus is not null)
            {
                segments.Add(p => p.Focus, focus);
            }

            if (onSection is not null)
            {
                segments.Add(p => p.OnSection, (Section chosen) =>
                {
                    onSection(chosen);
                    return Task.CompletedTask;
                });
            }
        });

    private IRenderedComponent<SheetContent> Content(Section section) =>
        RenderWithProviders<SheetContent>(content => content
            .Add(p => p.Members, Demo.Members)
            .Add(p => p.Vehicles, Demo.Vehicles)
            .Add(p => p.Places, Demo.Places)
            .Add(p => p.Section, section));
}
