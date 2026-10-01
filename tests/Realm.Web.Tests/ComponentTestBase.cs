using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using MudX;

namespace Realm.Web.Tests;

/// <summary>
/// The canonical bUnit setup of 03 section 8.3. Every component test class derives from it and renders the component under test with
/// <see cref="RenderWithProviders{TComponent}"/>: MudBlazor's services are registered, JS interop is loose, and
/// <c>MudPopoverProvider</c> and <c>MudXProvider</c> are on the page before the component, as <c>MainLayout</c> orders them (03 section 3.2).
/// A test that needs a service of its own adds it in its constructor, before the first render.
/// </summary>
public abstract class ComponentTestBase : BunitContext
{
    private bool _providersRendered;

    protected ComponentTestBase()
    {
        // Loose mode answers every JS call with a default, so a component never needs its own setup to render.
        JSInterop.Mode = JSRuntimeMode.Loose;

        // MudXProvider throws unless the initialize function of its module returns true, and loose mode would return false.
        JSInterop.SetupModule("import", IsMudXProviderModule).Setup<bool>("initialize", _ => true);

        Services.AddMudServices();
    }

    /// <summary>Renders the two providers once, then the component under test.</summary>
    protected IRenderedComponent<TComponent> RenderWithProviders<TComponent>(Action<ComponentParameterCollectionBuilder<TComponent>>? parameters = null)
        where TComponent : IComponent
    {
        if (!_providersRendered)
        {
            _providersRendered = true;
            Render<MudPopoverProvider>();
            Render<MudXProvider>();
        }

        return Render<TComponent>(parameters);
    }

    private static bool IsMudXProviderModule(JSRuntimeInvocation invocation) =>
        invocation.Arguments.Count > 0
        && invocation.Arguments[0] is string path
        && path.EndsWith("/mudxProvider.js", StringComparison.Ordinal);
}
