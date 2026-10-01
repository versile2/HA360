using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Realm.Web.Components.Shell;
using Xunit;

namespace Realm.Web.Tests;

public sealed class BottomNavTests : ComponentTestBase
{
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

    private void GoTo(string uri) => Services.GetRequiredService<NavigationManager>().NavigateTo(uri);

    private static string? CurrentOf(IRenderedComponent<BottomNav> cut, string testId) =>
        cut.Find($"[data-testid='{testId}']").GetAttribute("aria-current");

    private static string LabelOf(IRenderedComponent<BottomNav> cut, string testId) =>
        cut.Find($"[data-testid='{testId}'] .realm-nav-label").TextContent;
}
