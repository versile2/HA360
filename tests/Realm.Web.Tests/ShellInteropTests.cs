using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.JSInterop;
using Realm.Web.Layout;
using Realm.Web.Map;
using Realm.Web.Shell;
using Xunit;

namespace Realm.Web.Tests;

/// <summary>
/// <see cref="ShellInterop"/> and <see cref="ShellCallbacks"/> against a fake <see cref="IJSRuntime"/> (03 section 4.8): the module is the plain relative URL of D62,
/// each method calls its export, nothing is sent after the dispose, a dropped circuit is not an error, and the one callback is exactly the name
/// <c>realmShell.js</c> calls, with the shape the script sends. No browser.
/// </summary>
public sealed class ShellInteropTests
{
    private static readonly ViewportInfo Phone = new(412, 915, 2.625, new Padding(0, 0, 0, 0), ReducedMotion: false, PrefersContrast: false, "portrait");

    [Fact]
    public async Task CreateAsync_ImportsTheModuleByThePlainRelativePath()
    {
        var js = new FakeJs();

        await using var interop = await ShellInterop.CreateAsync(js, new ShellCallbacks(_ => Task.CompletedTask));

        var call = Assert.Single(js.Runtime);
        Assert.Equal("import", call.Identifier);
        Assert.Equal(["./js/realmShell.js"], call.Args);
        Assert.False(ShellInterop.ModulePath.StartsWith('/'));
        Assert.DoesNotContain("_content", ShellInterop.ModulePath);
    }

    [Fact]
    public async Task CreateAsync_WhenTheImportFails_Throws()
    {
        var js = new FakeJs { ImportError = new JSException("no such module") };

        await Assert.ThrowsAsync<JSException>(async () => await ShellInterop.CreateAsync(js, new ShellCallbacks(_ => Task.CompletedTask)));
    }

    [Fact]
    public async Task InitAsync_PassesTheCallbackReference_AndReturnsTheViewport()
    {
        var js = new FakeJs();
        await using var interop = await ShellInterop.CreateAsync(js, new ShellCallbacks(_ => Task.CompletedTask));

        var info = await interop.InitAsync();

        Assert.Equal(new ShellInfo(Phone), info);
        var call = Assert.Single(js.Module, call => call.Identifier == "init");
        Assert.IsType<DotNetObjectReference<ShellCallbacks>>(Assert.Single(call.Args));
    }

    [Theory]
    [InlineData(LayoutMode.Compact, "compact")]
    [InlineData(LayoutMode.Expanded, "expanded")]
    [InlineData(LayoutMode.Unknown, null)]
    public async Task SetLayoutAttrAsync_SendsTheWordOfTheMode_AndNullRemovesIt(LayoutMode mode, string? expected)
    {
        var js = new FakeJs();
        await using var interop = await ShellInterop.CreateAsync(js, new ShellCallbacks(_ => Task.CompletedTask));

        await interop.SetLayoutAttrAsync(mode);

        var call = Assert.Single(js.Module);
        Assert.Equal("setLayoutAttr", call.Identifier);
        Assert.Equal([expected], call.Args);
    }

    [Fact]
    public async Task ObserveSheetAsync_SendsTheSelector_AndNullStops()
    {
        var js = new FakeJs();
        await using var interop = await ShellInterop.CreateAsync(js, new ShellCallbacks(_ => Task.CompletedTask));

        await interop.ObserveSheetAsync(ShellInterop.SheetSelector);
        await interop.ObserveSheetAsync(null);

        Assert.Equal(["observeSheet", "observeSheet"], js.Module.Select(call => call.Identifier));
        Assert.Equal([ShellInterop.SheetSelector], js.Module[0].Args);
        Assert.Equal([(object?)null], js.Module[1].Args);
    }

    [Fact]
    public void TheSheetSelector_IsThePopoverFirst_ThenTheContractElement()
    {
        // 03 section 4.8 and [X-08]: a selector list returns the first match in document order, which is the popover (the whole sheet) when MudX rendered one.
        Assert.Equal("div[mudsheet], [data-testid=\"sheet\"]", ShellInterop.SheetSelector);
    }

    [Fact]
    public async Task DisposeAsync_StopsTheScript_ReleasesTheModuleAndTheReference_OnlyOnce()
    {
        var js = new FakeJs();
        var interop = await ShellInterop.CreateAsync(js, new ShellCallbacks(_ => Task.CompletedTask));
        await interop.InitAsync();
        var reference = (DotNetObjectReference<ShellCallbacks>)js.Module.Single(call => call.Identifier == "init").Args[0]!;
        Assert.NotNull(reference.Value);

        await interop.DisposeAsync();
        await interop.DisposeAsync();

        Assert.Equal(1, js.Module.Count(call => call.Identifier == "dispose"));
        Assert.Equal(1, js.ModuleDisposed);
        Assert.Throws<ObjectDisposedException>(() => reference.Value);
    }

    [Fact]
    public async Task AfterTheDispose_NothingIsSentToTheScript()
    {
        var js = new FakeJs();
        var interop = await ShellInterop.CreateAsync(js, new ShellCallbacks(_ => Task.CompletedTask));
        await interop.DisposeAsync();
        var calls = js.Module.Count;

        Assert.Null(await interop.InitAsync());
        await interop.SetLayoutAttrAsync(LayoutMode.Compact);
        await interop.ObserveSheetAsync(ShellInterop.SheetSelector);

        Assert.Equal(calls, js.Module.Count);
    }

    [Fact]
    public async Task ADroppedCircuit_IsNotAnError_AndTheReferenceIsStillReleased()
    {
        var js = new FakeJs { Disconnected = true };
        var interop = await ShellInterop.CreateAsync(js, new ShellCallbacks(_ => Task.CompletedTask));

        Assert.Null(await interop.InitAsync());
        await interop.SetLayoutAttrAsync(LayoutMode.Expanded);
        await interop.ObserveSheetAsync(null);
        await interop.DisposeAsync();

        var reference = (DotNetObjectReference<ShellCallbacks>)js.Module.Single(call => call.Identifier == "init").Args[0]!;
        Assert.Throws<ObjectDisposedException>(() => reference.Value);
    }

    // ---- the callback ----------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task OnViewport_ForwardsTheViewportToTheHandler()
    {
        ViewportInfo? received = null;
        var callbacks = new ShellCallbacks(viewport =>
        {
            received = viewport;
            return Task.CompletedTask;
        });

        await callbacks.OnViewport(Phone);

        Assert.Same(Phone, received);
    }

    [Fact]
    public void OnViewport_IsTheOnlyJsInvokableName_AndTheScriptCallsNothingElse()
    {
        var invokable = typeof(ShellCallbacks)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(method => (Method: method, Attribute: method.GetCustomAttribute<JSInvokableAttribute>()))
            .ToList();
        var script = File.ReadAllText(Path.Combine(PayloadContractTests.FindRepositoryRoot(), "src", "Realm.Web", "wwwroot", "js", "realmShell.js"));
        var called = Regex.Matches(script, "invokeMethodAsync\\('([A-Za-z]+)'").Select(match => match.Groups[1].Value).ToHashSet();

        Assert.All(invokable, entry =>
        {
            Assert.NotNull(entry.Attribute);
            Assert.Null(entry.Attribute.Identifier);   // the C# method name is the name the script uses
        });
        Assert.Equal(["OnViewport"], invokable.Select(entry => entry.Method.Name));
        Assert.Equal(["OnViewport"], called.ToArray());
    }

    [Fact]
    public void TheViewportTheScriptSends_DeserializesIntoViewportInfo()
    {
        // The shape of readViewport() in realmShell.js, as the JS interop serializes it (camelCase) and deserializes it (case-insensitive).
        const string json = """
            {"width":884,"height":916,"dpr":2.625,"safe":{"top":24,"right":0,"bottom":16,"left":0},"reducedMotion":true,"prefersContrast":false,"orientation":"landscape"}
            """;

        var viewport = JsonSerializer.Deserialize<ViewportInfo>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal(new ViewportInfo(884, 916, 2.625, new Padding(24, 0, 16, 0), ReducedMotion: true, PrefersContrast: false, "landscape"), viewport);
    }

    // ---- fakes -----------------------------------------------------------------------------------------------------------------------------

    private sealed record Call(string Identifier, object?[] Args);

    private sealed class FakeJs : IJSRuntime
    {
        public List<Call> Runtime { get; } = [];

        public List<Call> Module { get; } = [];

        public int ModuleDisposed { get; private set; }

        public Exception? ImportError { get; init; }

        public bool Disconnected { get; init; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            Runtime.Add(new Call(identifier, args ?? []));
            if (ImportError is not null)
            {
                throw ImportError;
            }

            return new ValueTask<TValue>((TValue)(object)new FakeModule(this));
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            InvokeAsync<TValue>(identifier, args);

        public void Disposed() => ModuleDisposed++;
    }

    private sealed class FakeModule(FakeJs js) : IJSObjectReference
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            js.Module.Add(new Call(identifier, args ?? []));
            if (js.Disconnected)
            {
                throw new JSDisconnectedException("The circuit is gone.");
            }

            object? result = identifier == "init" ? new ShellInfo(Phone) : null;
            return new ValueTask<TValue>((TValue)result!);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            InvokeAsync<TValue>(identifier, args);

        public ValueTask DisposeAsync()
        {
            js.Disposed();
            return ValueTask.CompletedTask;
        }
    }
}
