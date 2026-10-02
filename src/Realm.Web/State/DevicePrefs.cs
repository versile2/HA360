using Microsoft.JSInterop;
using Realm.Web.Map;
using Realm.Web.Shell;

namespace Realm.Web.State;

/// <summary>
/// The device preferences of 01 section 7.9, one set per circuit (a scoped service): the map style, whether places are drawn, the default view radius and the
/// layout override. They live in the browser's <c>localStorage</c> under the four keys below, and only <c>realmShell.js</c> touches it (<c>prefs</c>, 03 section 4.8):
/// this class calls those functions after the first render, never before (prerendering has no browser), and a browser that refuses storage leaves the defaults in force.
/// A value that is not one of the allowed ones reads as the default. A change applies at once: the new value is current, <see cref="Changed"/> fires, and only then
/// is it stored.
/// </summary>
/// <remarks>
/// The Demo URL parameters <c>style</c> and <c>layout</c> are not preferences: a page that has one lets it win over the stored value and over the default,
/// and a choice made in the page replaces it for the rest of the circuit. The stored value is never read from, or written for, those parameters.
/// </remarks>
public sealed class DevicePrefs(IJSRuntime js) : IAsyncDisposable
{
    /// <summary>The map style id, one of <see cref="MapStyles"/>.</summary>
    public const string MapStyleKey = "realm.mapStyle";

    /// <summary><c>true</c> or <c>false</c>: whether the saved places are drawn as circles.</summary>
    public const string ShowZonesKey = "realm.showZones";

    /// <summary>One of the <see cref="ViewRadiusChoices"/> values: <c>10</c>, <c>25</c>, <c>40</c>, <c>100</c>, <c>250</c> (kilometres) or <c>all</c>.</summary>
    public const string ViewRadiusKmKey = "realm.viewRadiusKm";

    /// <summary><c>auto</c>, <c>sheet</c> or <c>panel</c> (the override of 01 section 3.1).</summary>
    public const string LayoutKey = "realm.layout";

    /// <summary>The radius value that includes everyone.</summary>
    public const string RadiusAll = "all";

    /// <summary>The layout override that follows the window width.</summary>
    public const string LayoutAuto = "auto";

    /// <summary>The layout override that always shows the bottom sheet.</summary>
    public const string LayoutSheet = "sheet";

    /// <summary>The layout override that always shows the side panel.</summary>
    public const string LayoutPanel = "panel";

    /// <summary>The four styles a person can choose, in the order of the Layers popover (01 section 4.12): never <c>demo-offline</c>, never Parchment.</summary>
    public static IReadOnlyList<string> MapStyles { get; } = [MapStyleIds.Night, MapStyleIds.Day, MapStyleIds.Streets, MapStyleIds.Satellite];

    /// <summary>The six radius choices of 01 section 7.9 (6, 16, 25, 62 and 155 mi, and everyone), stored in kilometres.</summary>
    public static IReadOnlyList<RadiusChoice> ViewRadiusChoices { get; } =
    [
        new("10", "6 mi", 10),
        new("25", "16 mi", 25),
        new("40", "25 mi", 40),
        new("100", "62 mi", 100),
        new("250", "155 mi", 250),
        new(RadiusAll, "Everyone", double.PositiveInfinity),
    ];

    /// <summary>The three layout overrides, as the Settings radio shows them.</summary>
    public static IReadOnlyList<LayoutChoice> LayoutChoices { get; } =
    [
        new(LayoutAuto, "Auto"),
        new(LayoutSheet, "Bottom sheet"),
        new(LayoutPanel, "Side panel"),
    ];

    private const string DefaultRadius = "40";   // 25 mi (40 km), 01 section 7.9

    private IJSObjectReference? _module;
    private Task? _loading;
    private bool _disposed;

    /// <summary>A radius choice: the stored value, the label in miles and the radius in kilometres (infinity for everyone).</summary>
    public sealed record RadiusChoice(string Value, string Label, double Km);

    /// <summary>A layout override choice: the stored value and its label.</summary>
    public sealed record LayoutChoice(string Value, string Label);

    /// <summary>The key of the preference that changed, raised after the value is current and before it is stored. Raised on the caller's context: a subscriber marshals with <c>InvokeAsync</c>.</summary>
    public event Action<string>? Changed;

    /// <summary>True once the stored values have been read.</summary>
    public bool Loaded { get; private set; }

    /// <summary>The map style: <see cref="MapStyleIds.Night"/> until another is chosen.</summary>
    public string MapStyle { get; private set; } = MapStyleIds.Night;

    /// <summary>The style before the last change of <see cref="MapStyle"/>, which a style that fails to load goes back to.</summary>
    public string PreviousMapStyle { get; private set; } = MapStyleIds.Night;

    /// <summary>Whether the places are drawn: on until switched off.</summary>
    public bool ShowZones { get; private set; } = true;

    /// <summary>The stored radius value (<c>40</c> until another is chosen).</summary>
    public string ViewRadius { get; private set; } = DefaultRadius;

    /// <summary>The layout override: <c>auto</c> until another is chosen.</summary>
    public string Layout { get; private set; } = LayoutAuto;

    /// <summary>The default view radius in kilometres; <see cref="double.PositiveInfinity"/> for everyone.</summary>
    public double ViewRadiusKm => RadiusOf(ViewRadius).Km;

    /// <summary>The radius choice for a stored value; the default for anything else.</summary>
    public static RadiusChoice RadiusOf(string? value) =>
        ViewRadiusChoices.FirstOrDefault(choice => choice.Value == value) ?? ViewRadiusChoices.First(choice => choice.Value == DefaultRadius);

    /// <summary>
    /// Reads the stored values once. Call it after the first render; a call that cannot reach the browser (no circuit yet, or the circuit is gone)
    /// leaves the defaults in force and lets the next call try again.
    /// </summary>
    public async Task EnsureLoadedAsync()
    {
        if (Loaded)
        {
            return;
        }

        _loading ??= LoadAsync();
        await _loading;
        if (!Loaded)
        {
            _loading = null;   // the browser could not be reached: the next call asks again
        }
    }

    /// <summary>Applies a style immediately and stores it. An id that is not one of <see cref="MapStyles"/> is ignored.</summary>
    public async Task SetMapStyleAsync(string styleId)
    {
        if (!MapStyles.Contains(styleId) || styleId == MapStyle)
        {
            return;
        }

        PreviousMapStyle = MapStyle;
        MapStyle = styleId;
        Changed?.Invoke(MapStyleKey);
        await StoreAsync(MapStyleKey, styleId);
    }

    /// <summary>Applies the Show places switch immediately and stores it.</summary>
    public async Task SetShowZonesAsync(bool show)
    {
        if (show == ShowZones)
        {
            return;
        }

        ShowZones = show;
        Changed?.Invoke(ShowZonesKey);
        await StoreAsync(ShowZonesKey, show ? "true" : "false");
    }

    /// <summary>Applies a radius immediately and stores it. A value that is not one of <see cref="ViewRadiusChoices"/> is ignored.</summary>
    public async Task SetViewRadiusAsync(string value)
    {
        if (ViewRadiusChoices.All(choice => choice.Value != value) || value == ViewRadius)
        {
            return;
        }

        ViewRadius = value;
        Changed?.Invoke(ViewRadiusKmKey);
        await StoreAsync(ViewRadiusKmKey, value);
    }

    /// <summary>Applies a layout override immediately and stores it. A value that is not one of <see cref="LayoutChoices"/> is ignored.</summary>
    public async Task SetLayoutAsync(string value)
    {
        if (LayoutChoices.All(choice => choice.Value != value) || value == Layout)
        {
            return;
        }

        Layout = value;
        Changed?.Invoke(LayoutKey);
        await StoreAsync(LayoutKey, value);
    }

    /// <summary>Releases the module reference; safe when the circuit is already gone.</summary>
    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        if (_module is null)
        {
            return;
        }

        try
        {
            await _module.DisposeAsync();
        }
        catch (JSDisconnectedException)
        {
            // The circuit is gone, and the reference with it.
        }
    }

    private async Task LoadAsync()
    {
        try
        {
            var all = await CallAsync<Dictionary<string, string>>("prefs.getAll");
            Apply(all);
            Loaded = true;
        }
        catch (Exception exception) when (exception is JSException or JSDisconnectedException or InvalidOperationException or OperationCanceledException)
        {
            // No browser to ask (prerender, or the circuit dropped): the defaults stay, Loaded stays false.
        }
    }

    private void Apply(Dictionary<string, string>? stored)
    {
        if (stored is null)
        {
            return;
        }

        if (stored.TryGetValue(MapStyleKey, out var style) && MapStyles.Contains(style))
        {
            MapStyle = style;
        }

        if (stored.TryGetValue(ShowZonesKey, out var zones) && bool.TryParse(zones, out var show))
        {
            ShowZones = show;
        }

        if (stored.TryGetValue(ViewRadiusKmKey, out var radius) && ViewRadiusChoices.Any(choice => choice.Value == radius))
        {
            ViewRadius = radius;
        }

        if (stored.TryGetValue(LayoutKey, out var layout) && LayoutChoices.Any(choice => choice.Value == layout))
        {
            Layout = layout;
        }
    }

    // A value that cannot be stored (a private window, blocked site data) stays current for this circuit and is simply not remembered.
    private async Task StoreAsync(string key, string value)
    {
        try
        {
            await CallAsync<bool>("prefs.set", key, value);
        }
        catch (Exception exception) when (exception is JSException or JSDisconnectedException or InvalidOperationException or OperationCanceledException)
        {
            // Nothing to store into.
        }
    }

    private async Task<T> CallAsync<T>(string identifier, params object?[] args)
    {
        // The same module URL as ShellInterop's: the browser holds one instance of it, so the functions need no init.
        _module ??= await js.InvokeAsync<IJSObjectReference>("import", ShellInterop.ModulePath);
        if (_disposed)
        {
            throw new OperationCanceledException();
        }

        return await _module.InvokeAsync<T>(identifier, args);
    }
}
