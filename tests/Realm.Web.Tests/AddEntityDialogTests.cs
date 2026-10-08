using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using Realm.Demo;
using Realm.Domain;
using Realm.Web.Components.Shell;
using Realm.Web.Sheet;
using Xunit;

namespace Realm.Web.Tests;

/// <summary>
/// The picker of "+ Add driver" and "+ Add tracker" (0.2.2, D119, D121), opened as the Location page opens it (IDialogService into a MudDialogProvider) over the Demo session, whose roster is four
/// people, the wagon, and the prince and the hatchback under Not tracked: every candidate is listed with its entity id, the ones already on the map are greyed out and disabled, Not tracked ones can be
/// chosen, a search filters, and a pick calls OnPicked and closes the dialog.
/// </summary>
public sealed class AddEntityDialogTests : ComponentTestBase
{
    private const string Hatchback = "device_tracker.hatchback";

    private IDialogReference? _reference;

    [Fact]
    public async Task TheDriverPicker_ListsEveryCandidate_NotTrackedFirst_WithTheirEntityIds()
    {
        await using var session = new DemoRealmSessionFactory().Create(null);

        var cut = await OpenAsync(session, AddKind.Driver);

        Assert.Equal("Add driver", cut.Find(".realm-popup__title").TextContent);
        var ids = cut.FindAll("[data-testid^='add-pick-']").Select(row => row.GetAttribute("data-testid")!["add-pick-".Length..]).ToArray();
        Assert.Equal(session.Roster.Entries.Count, ids.Length);
        Assert.Equal("person-prince", ids[0]);
        Assert.Contains("person.prince", cut.Find("[data-testid='add-pick-person-prince']").TextContent);
        Assert.Single(cut.FindAll("[data-testid='add-pick-person-prince'] .realm-roster__avatar"));
    }

    [Fact]
    public async Task EntriesAlreadyOnTheMap_AreGreyedOutAndDisabled_AndSayWhy()
    {
        await using var session = new DemoRealmSessionFactory().Create(null);

        var cut = await OpenAsync(session, AddKind.Driver);

        var king = cut.Find("[data-testid='add-pick-person-king']");
        Assert.True(king.HasAttribute("disabled"));
        Assert.Equal("true", king.GetAttribute("aria-disabled"));
        Assert.Contains("realm-picker__row--disabled", king.ClassList);
        Assert.Contains("Already on the map", king.TextContent);
        var wagon = cut.Find("[data-testid='add-pick-device-tracker-wagon']");
        Assert.True(wagon.HasAttribute("disabled"));

        var prince = cut.Find("[data-testid='add-pick-person-prince']");
        Assert.False(prince.HasAttribute("disabled"));
        Assert.DoesNotContain("Already on the map", prince.TextContent);
    }

    [Fact]
    public async Task TheSearchBox_FiltersByNameAndEntityId_AndSaysWhenNothingMatches()
    {
        await using var session = new DemoRealmSessionFactory().Create(null);
        var cut = await OpenAsync(session, AddKind.Tracker);
        Assert.Equal("Add tracker", cut.Find(".realm-popup__title").TextContent);

        cut.Find("[data-testid='add-search']").Input("hatch");

        Assert.Equal(["add-pick-device-tracker-hatchback"], cut.FindAll("[data-testid^='add-pick-']").Select(row => row.GetAttribute("data-testid")));

        cut.Find("[data-testid='add-search']").Input("person.king");
        Assert.Equal(["add-pick-person-king"], cut.FindAll("[data-testid^='add-pick-']").Select(row => row.GetAttribute("data-testid")));

        cut.Find("[data-testid='add-search']").Input("zzzz");
        Assert.Empty(cut.FindAll("[data-testid^='add-pick-']"));
        Assert.Equal("Nothing matches your search.", cut.Find("[data-testid='add-none']").TextContent);
    }

    [Fact]
    public async Task ChoosingANotTrackedEntry_CallsOnPicked_AndClosesTheDialog()
    {
        await using var session = new DemoRealmSessionFactory().Create(null);
        RosterEntry? picked = null;
        var cut = await OpenAsync(session, AddKind.Tracker, entry =>
        {
            picked = entry;
            return Task.CompletedTask;
        });

        await cut.Find("[data-testid='add-pick-device-tracker-hatchback']").TriggerEventAsync("onclick", new MouseEventArgs());

        Assert.Equal(Hatchback, picked?.EntityId);
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[data-testid='add-picker']")));
        Assert.True(_reference!.Result.IsCompleted);
    }

    [Fact]
    public async Task ASessionWithoutARoster_ShowsTheEmptyLine()
    {
        var cut = await OpenAsync(null, AddKind.Driver);

        Assert.Contains("Nothing found yet", cut.Find("[data-testid='add-none']").TextContent);
    }

    [Fact]
    public async Task ThePicker_IsOneOverlayLayer_ThatLeavesWithTheDialog()
    {
        await using var session = new DemoRealmSessionFactory().Create(null);
        var ui = Services.GetRequiredService<Realm.Web.State.RealmUiState>();

        var cut = await OpenAsync(session, AddKind.Driver);
        Assert.Contains(ui.Overlays, layer => layer.Id == "add-picker");

        await cut.InvokeAsync(() => _reference!.Close());

        cut.WaitForAssertion(() => Assert.DoesNotContain(ui.Overlays, layer => layer.Id == "add-picker"));
    }

    private async Task<IRenderedComponent<MudDialogProvider>> OpenAsync(IRealmSession? session, AddKind kind, Func<RosterEntry, Task>? onPicked = null)
    {
        var cut = RenderWithProviders<MudDialogProvider>(provider =>
        {
            if (session is not null)
            {
                provider.AddCascadingValue(session);
            }
        });
        var dialogs = Services.GetRequiredService<IDialogService>();
        var options = new DialogOptions { CloseOnEscapeKey = true, BackdropClick = true, CloseButton = false, BackgroundClass = "realm-popup-scrim" };
        var parameters = new DialogParameters<AddEntityDialog> { { dialog => dialog.Kind, kind } };
        if (onPicked is not null)
        {
            parameters.Add(dialog => dialog.OnPicked, onPicked);
        }

        await Renderer.Dispatcher.InvokeAsync(async () =>
        {
            _reference = await dialogs.ShowAsync<AddEntityDialog>(null, parameters, options);
        });
        cut.WaitForAssertion(() => cut.Find("[data-testid='add-picker']"));
        return cut;
    }
}
