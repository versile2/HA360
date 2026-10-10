using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Realm.Demo;
using Realm.Domain;
using Realm.Web.Components.Sheet;
using Realm.Web.Sheet;
using Xunit;

namespace Realm.Web.Tests;

/// <summary>
/// The Add row, the last row of each sheet list (0.2.2, D119): "+ Add driver", "+ Add tracker" or "+ Add place", only when the page allows it (the add-on option allow_add; the Demo always), after the
/// list and outside it (so the list holds only its rows), with its own test id, one text node and a button's accessibility.
/// </summary>
public sealed class AddRowTests : ComponentTestBase
{
    private static readonly RealmSnapshot Demo = FullCast.Snapshot();

    [Theory]
    [InlineData(Section.Drivers, "add-driver", "+ Add driver")]
    [InlineData(Section.Vehicles, "add-tracker", "+ Add tracker")]
    [InlineData(Section.Places, "add-place", "+ Add place")]
    public void EachSection_EndsWithItsAddRow_WhenAddingIsAllowed(Section section, string testId, string label)
    {
        var cut = Content(section, allowAdd: true);

        var row = cut.Find($"[data-testid='{testId}']");
        Assert.Equal("BUTTON", row.TagName);
        Assert.Equal(label, row.TextContent);
        Assert.Single(row.ChildNodes);
        Assert.Single(cut.FindAll(".realm-add-row"));
        Assert.False(row.GetAttribute("data-testid")!.StartsWith("row-", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(Section.Drivers)]
    [InlineData(Section.Vehicles)]
    [InlineData(Section.Places)]
    public void NoSection_HasAnAddRow_WhenAddingIsNotAllowed(Section section)
    {
        var cut = Content(section, allowAdd: false);

        Assert.Empty(cut.FindAll(".realm-add-row"));
        Assert.Empty(cut.FindAll("[data-testid^='add-']"));
    }

    [Fact]
    public void TheDefault_IsNoAddRow_SoABareListIsOnlyItsRows()
    {
        var cut = RenderWithProviders<SheetContent>(content => content.Add(p => p.Members, Demo.Members).Add(p => p.Places, Demo.Places));

        Assert.Empty(cut.FindAll(".realm-add-row"));
    }

    [Fact]
    public void TheAddRow_IsAfterTheListAndNotInIt_SoTheRowCountsStay()
    {
        var cut = Content(Section.Drivers, allowAdd: true);

        Assert.Equal(Demo.Members.Count, cut.FindAll("[data-testid='sheet-list'] > li").Count);
        Assert.Empty(cut.FindAll("[data-testid='sheet-list'] [data-testid='add-driver']"));
        var wrap = cut.Find(".realm-sheet-list-wrap");
        Assert.Equal("add-driver", wrap.LastElementChild!.QuerySelector("button")!.GetAttribute("data-testid"));
    }

    [Theory]
    [InlineData(Section.Drivers, AddKind.Driver)]
    [InlineData(Section.Vehicles, AddKind.Tracker)]
    [InlineData(Section.Places, AddKind.Place)]
    public async Task ATap_RaisesOnAdd_WithTheKindOfTheSection(Section section, AddKind expected)
    {
        AddKind? raised = null;
        var cut = Content(section, allowAdd: true, onAdd: kind => raised = kind);

        await cut.Find(".realm-add-row").TriggerEventAsync("onclick", new MouseEventArgs());

        Assert.Equal(expected, raised);
    }

    [Fact]
    public void TheWords_NeverUseTheOldAddAWording()
    {
        foreach (var kind in Enum.GetValues<AddKind>())
        {
            Assert.StartsWith("+ Add ", AddKinds.Label(kind), StringComparison.Ordinal);
            Assert.DoesNotMatch("Add a (person|vehicle|place)", AddKinds.Label(kind));
        }

        Assert.Equal("Add driver", AddKinds.Title(AddKind.Driver));
        Assert.Equal("Add tracker", AddKinds.Title(AddKind.Tracker));
        Assert.Equal(RosterGroup.People, AddKinds.Target(AddKind.Driver));
        Assert.Equal(RosterGroup.Vehicles, AddKinds.Target(AddKind.Tracker));
    }

    private IRenderedComponent<SheetContent> Content(Section section, bool allowAdd, Action<AddKind>? onAdd = null) =>
        RenderWithProviders<SheetContent>(content =>
        {
            content
                .Add(p => p.Members, Demo.Members)
                .Add(p => p.Vehicles, Demo.Vehicles)
                .Add(p => p.Places, Demo.Places)
                .Add(p => p.Section, section)
                .Add(p => p.AllowAdd, allowAdd);
            if (onAdd is not null)
            {
                content.Add(p => p.OnAdd, (AddKind kind) => onAdd(kind));
            }
        });
}
