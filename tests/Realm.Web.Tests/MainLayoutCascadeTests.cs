using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using Realm.Demo;
using Realm.Domain;
using Realm.Web.Components.Layout;
using Realm.Web.Components.Shell;
using Realm.Web.Hosting;
using Xunit;

namespace Realm.Web.Tests;

/// <summary>
/// The session and the Demo overrides that <c>RealmShell</c> cascades reach what MudBlazor renders for a popover and for a dialog (D67, CR2-001).
/// </summary>
/// <remarks>
/// <para>
/// <c>MudPopover</c> renders an empty <c>div</c>; <c>MudPopoverProvider</c> renders the popover's content, and <c>MudDialogProvider</c> the
/// dialog's, inside each provider's own <c>MudRender</c>. Blazor resolves a cascading parameter by walking up from the component that renders
/// the fragment, so content in either place sees the provider and what is above it, never a sibling of the provider. With the providers beside
/// <c>RealmShell</c> (the layout before D67) a probe in a popover or a dialog reaches <c>MainLayout</c> without meeting the shell, and both tests
/// below fail on <c>Assert.NotNull</c>. They pass only while the providers are inside the shell.
/// </para>
/// <para>
/// The real layout brings its own providers, so these tests call <c>Render</c> and not <c>RenderWithProviders</c>, which would add a second
/// popover provider. The shell is rendered as in Demo mode with the session from the Demo factory, and the page URL carries two overrides, so the
/// values compared are the ones the shell parsed.
/// </para>
/// </remarks>
public sealed class MainLayoutCascadeTests : ComponentTestBase
{
    private const string PageUrl = "?style=day&week=2";

    // The page body: a probe the way every page sits (inside the shell, outside any provider), and a probe inside an open MudPopover, the way
    // MudXSheet puts the sheet's content.
    private static readonly RenderFragment PageBody = builder =>
    {
        builder.OpenComponent<InlineProbe>(0);
        builder.CloseComponent();

        builder.OpenComponent<MudPopover>(1);
        builder.AddComponentParameter(2, nameof(MudPopover.Open), true);
        builder.AddComponentParameter(3, nameof(MudPopover.ChildContent), (RenderFragment)(popover =>
        {
            popover.OpenComponent<PopoverProbe>(0);
            popover.CloseComponent();
        }));
        builder.CloseComponent();
    };

    [Fact]
    public void ContentInAPopover_ReceivesTheShellsSessionAndOverrides()
    {
        var cut = RenderLayout();

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(cut.FindComponent<PopoverProbe>());
        });

        AssertShellCascade(cut.FindComponent<InlineProbe>().Instance, cut.FindComponent<PopoverProbe>().Instance);
    }

    [Fact]
    public async Task ContentInADialog_ReceivesTheShellsSessionAndOverrides()
    {
        var cut = RenderLayout();
        var dialogs = Services.GetRequiredService<IDialogService>();

        await Renderer.Dispatcher.InvokeAsync(async () =>
        {
            await dialogs.ShowAsync<DialogProbe>();
        });

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(cut.FindComponent<DialogProbe>());
        });

        AssertShellCascade(cut.FindComponent<InlineProbe>().Instance, cut.FindComponent<DialogProbe>().Instance);
    }

    // Registers what RealmShell injects, points the page at the overrides and renders the real layout around the page body.
    private IRenderedComponent<MainLayout> RenderLayout()
    {
        Services.AddSingleton(new RuntimeOptions(RealmMode.Demo, DetailedErrors: false));
        Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        Services.AddRealmDemo();

        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo(PageUrl);
        Assert.EndsWith(PageUrl, navigation.Uri, StringComparison.Ordinal);   // the overrides below must come from the URL, not from a missed navigation

        Action<ComponentParameterCollectionBuilder<MainLayout>> parameters = layout => layout.Add(l => l.Body, PageBody);
        return Render<MainLayout>(parameters);
    }

    // The inline probe is the baseline: it sits where the shell always cascaded to. The probe under a provider must see the same two objects.
    private static void AssertShellCascade(ProbeBase inline, ProbeBase underProvider)
    {
        Assert.NotNull(inline.Session);
        Assert.NotNull(inline.Overrides);
        Assert.Equal(new DemoUiOverrides(Style: "day", Week: 2), inline.Overrides);

        Assert.NotNull(underProvider.Session);
        Assert.NotNull(underProvider.Overrides);
        Assert.Same(inline.Session, underProvider.Session);
        Assert.Same(inline.Overrides, underProvider.Overrides);
    }

    private abstract class ProbeBase : ComponentBase
    {
        [CascadingParameter]
        public IRealmSession? Session { get; set; }

        [CascadingParameter]
        public DemoUiOverrides? Overrides { get; set; }
    }

    private sealed class InlineProbe : ProbeBase
    {
    }

    private sealed class PopoverProbe : ProbeBase
    {
    }

    private sealed class DialogProbe : ProbeBase
    {
    }
}
