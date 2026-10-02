using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Realm.Domain;
using Realm.Web.Components.Shell;
using Realm.Web.Layout;
using Realm.Web.Map;
using Realm.Web.State;
using Xunit;

namespace Realm.Web.Tests;

public sealed class BottomNavTests : ComponentTestBase
{
    private static readonly EntityRef Jester = new(EntityKind.Member, "jester");

    [Fact]
    public void Renders_TwoRelativeLinks_NamedLocationAndDriving()
    {
        var cut = RenderWithProviders<BottomNav>();

        Assert.Equal("Main", cut.Find("nav").GetAttribute("aria-label"));
        var links = cut.FindAll("nav a");
        Assert.Equal(2, links.Count);
        Assert.Equal("./", links[0].GetAttribute("href"));
        Assert.Equal("driving", links[1].GetAttribute("href"));
        Assert.Equal("Location", LabelOf(cut, "nav-location"));
        Assert.Equal("Driving", LabelOf(cut, "nav-driving"));
    }

    [Fact]
    public void AtTheRoot_MarksOnlyLocationAsCurrent()
    {
        var cut = RenderWithProviders<BottomNav>();

        Assert.Equal("page", CurrentOf(cut, "nav-location"));
        Assert.Null(CurrentOf(cut, "nav-driving"));
    }

    [Theory]
    [InlineData("driving")]
    [InlineData("driving?week=2")]
    [InlineData("driving/king")]
    [InlineData("driving/king?week=1")]
    public void OnADrivingRoute_MarksOnlyDrivingAsCurrent(string uri)
    {
        var cut = RenderWithProviders<BottomNav>();

        GoTo(uri);

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("page", CurrentOf(cut, "nav-driving"));
            Assert.Null(CurrentOf(cut, "nav-location"));
        });
    }

    [Fact]
    public void NavigatingBackToTheRoot_MovesTheMarkBackToLocation()
    {
        var cut = RenderWithProviders<BottomNav>();
        GoTo("driving");
        cut.WaitForAssertion(() => Assert.Equal("page", CurrentOf(cut, "nav-driving")));

        GoTo("./");

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("page", CurrentOf(cut, "nav-location"));
            Assert.Null(CurrentOf(cut, "nav-driving"));
        });
    }

    [Theory]
    [InlineData("not-found")]
    [InlineData("drivingx")]
    public void OnAnyOtherPath_MarksNeitherLinkAsCurrent(string uri)
    {
        var cut = RenderWithProviders<BottomNav>();

        GoTo(uri);

        cut.WaitForAssertion(() =>
        {
            Assert.Null(CurrentOf(cut, "nav-location"));
            Assert.Null(CurrentOf(cut, "nav-driving"));
        });
    }

    // ---- the re-tap rows (01 section 2.2, 03 section 3.7) ------------------------------------------------------------------------------------

    [Fact]
    public async Task ReTappingLocation_OnLocation_ClearsTheSelection_AndCollapsesTheSheet_WithoutNavigating()
    {
        var ui = CompactUi();
        ui.Apply(new SheetEvent.PinTap(Jester));
        ui.Apply(new SheetEvent.HandleSet(SheetSize.Tall));
        var navigation = Navigation();
        var cut = RenderWithProviders<BottomNav>();

        await TapAsync(cut, "nav-location");

        Assert.Null(ui.Selection);
        Assert.Equal(SheetSize.Peek, ui.SheetSize);
        Assert.Equal(0, ui.Depth);
        Assert.Empty(navigation.History);
    }

    [Fact]
    public async Task ReTappingLocation_TakesTheHistoryBackToTheBaseEntry_ByTheStateChangeAlone()
    {
        var ui = CompactUi();
        ui.Apply(new SheetEvent.PinTap(Jester));
        ui.Apply(new SheetEvent.HandleSet(SheetSize.Tall));
        var port = new FakeHistoryPort(tokens: true);
        await Services.GetRequiredService<HistorySync>().AttachAsync(port, HistorySurface.Location);
        Assert.Equal(2, port.Entries);
        var cut = RenderWithProviders<BottomNav>();

        await TapAsync(cut, "nav-location");

        Assert.Equal(0, port.Entries);
    }

    [Fact]
    public async Task ReTappingDriving_OnAnotherWeek_SelectsThisWeek_ScrollsToTheTop_AndReplacesTheEntryWithoutTheWeek()
    {
        var ui = CompactUi();
        ui.SelectedWeek = 2;
        var navigation = Navigation();
        GoTo("driving?week=2");
        var cut = RenderWithProviders<BottomNav>();

        await TapAsync(cut, "nav-driving");

        Assert.Equal(0, ui.SelectedWeek);
        Assert.Equal("http://localhost/driving", navigation.Uri);
        var scroll = Assert.Single(JSInterop.Invocations["scrollTo"]);
        Assert.Collection(scroll.Arguments, top => Assert.Equal(0, top), left => Assert.Equal(0, left));
        Assert.Contains(navigation.History, entry => entry.Uri == "http://localhost/driving" && entry.Options.ReplaceHistoryEntry);
    }

    [Fact]
    public async Task ReTappingDriving_OnThisWeek_ScrollsToTheTop_AndLeavesTheAddressAlone()
    {
        var navigation = Navigation();
        GoTo("driving");
        var cut = RenderWithProviders<BottomNav>();

        await TapAsync(cut, "nav-driving");

        Assert.Single(JSInterop.Invocations["scrollTo"]);
        Assert.Equal(0, CompactUi().SelectedWeek);
        Assert.False(Assert.Single(navigation.History).Options.ReplaceHistoryEntry);
    }

    [Fact]
    public async Task ChangingTab_FromDriving_TakesTheSentinelBack_BeforeItReplacesTheEntry()
    {
        var navigation = Navigation();
        GoTo("driving");
        var port = new FakeHistoryPort(tokens: true);
        await Services.GetRequiredService<HistorySync>().AttachAsync(port, HistorySurface.Driving);
        Assert.Equal(1, port.Entries);
        var entriesWhenTheAddressChanged = -1;
        navigation.LocationChanged += (_, _) => entriesWhenTheAddressChanged = port.Entries;
        var cut = RenderWithProviders<BottomNav>();

        await TapAsync(cut, "nav-location");

        Assert.Equal("http://localhost/", navigation.Uri);
        Assert.Equal(0, port.Entries);
        Assert.Equal(0, entriesWhenTheAddressChanged);   // the entries were gone before the address changed
        Assert.Contains(navigation.History, entry => entry.Options.ReplaceHistoryEntry);
    }

    [Fact]
    public async Task ChangingTab_FromLocationWithADetailOpen_ReleasesTheEntries_KeepsTheSelection_AndReplaces()
    {
        var ui = CompactUi();
        ui.Apply(new SheetEvent.PinTap(Jester));
        ui.Apply(new SheetEvent.HandleSet(SheetSize.Tall));
        var navigation = Navigation();
        var port = new FakeHistoryPort(tokens: true);
        await Services.GetRequiredService<HistorySync>().AttachAsync(port, HistorySurface.Location);
        var cut = RenderWithProviders<BottomNav>();

        await TapAsync(cut, "nav-driving");

        Assert.Equal("http://localhost/driving", navigation.Uri);
        Assert.Equal(0, port.Entries);
        Assert.Equal(Jester, ui.Selection);
        Assert.Equal(SheetSize.Tall, ui.SheetSize);
        Assert.Contains(navigation.History, entry => entry.Uri == "driving" && entry.Options.ReplaceHistoryEntry);
    }

    private static Task TapAsync(IRenderedComponent<BottomNav> cut, string testId) =>
        cut.Find($"[data-testid='{testId}']").TriggerEventAsync("onclick", new MouseEventArgs());

    private RealmUiState CompactUi()
    {
        var ui = Services.GetRequiredService<RealmUiState>();
        ui.Layout = new LayoutSnapshot(LayoutMode.Compact, false, 412, 915, new Padding(0, 0, 0, 0));
        return ui;
    }

    private BunitNavigationManager Navigation() => (BunitNavigationManager)Services.GetRequiredService<NavigationManager>();

    private void GoTo(string uri) => Services.GetRequiredService<NavigationManager>().NavigateTo(uri);

    private static string? CurrentOf(IRenderedComponent<BottomNav> cut, string testId) =>
        cut.Find($"[data-testid='{testId}']").GetAttribute("aria-current");

    private static string LabelOf(IRenderedComponent<BottomNav> cut, string testId) =>
        cut.Find($"[data-testid='{testId}'] .realm-nav-label").TextContent;
}
