# CI summary: FAILURE

- result: failure
- branch: slice/S15b-states
- sha: f3ab03280ac35e19d85cc87acd4788fa273dafd0
- run: 104
- url: https://github.com/versile2/HA360/actions/runs/37137366248
- why: job dotnet: failure; 1 failed test(s); 1 failed E2E test(s)

## Jobs

| job | result |
|---|---|
| guards | success |
| dotnet | failure |
| js | success |
| docker-smoke | success |
| e2e | failure |

## Guards

```text
REPORT ac-coverage: 2 AC ids missing (AC-45, AC-48)
```

PASS: 11 of 12 guards.

## Compiler errors

None.

## Failed tests (1 of 2781)

2780 passed, 1 failed, 0 skipped (5 .trx files).

### Realm.Web.Tests.RealmShellTests.CreatingTheSession_HasNoOtherSideEffect

```text
Assert.Equal() Failure: Values differ
Expected: 0
Actual:   1
```

## JS

Node tests (node --test of tests/js): 462 passed, 0 failed.

Type check (tsc.log): no "error TS" line.

## Map styles

PASS: 5 (satellite, demo-offline, night, day, streets).

## E2E

146 passed, 1 failed, 2 flaky, 1 skipped (150 test runs in 4 projects).

Payload contract (node --test of tests/contract): 30 passed, 0 failed.

## Failed E2E tests (1)

### [phone] ac-f-states.spec.ts › [X-04] the circuit reconnect banner › [X-04] after the sockets drop the static banner "Reconnecting to the court…" shows and clears, and the selection is unchanged

at ac-f-states.spec.ts:179

```text
Error: the selection survived the reconnect

expect(locator).toBeVisible() failed

Locator: getByTestId('sheet-selection-header')
Expected: visible
Timeout: 8000ms
Error: element(s) not found

Call log:
  - the selection survived the reconnect getByTestId('sheet-selection-header') with timeout 8000ms
  - waiting for getByTestId('sheet-selection-header')
```

## App log (last 30 lines of e2e/app.log)

```text
2026-10-03T16:40:26.110Z warn: Microsoft.AspNetCore.Components.Server.Circuits.RemoteRenderer[100] Unhandled exception rendering component: JavaScript interop calls cannot be issued at this time. This is because the circuit has disconnected and is being disposed. Microsoft.JSInterop.JSDisconnectedException: JavaScript interop calls cannot be issued at this time. This is because the circuit has disconnected and is being disposed.    at Microsoft.AspNetCore.Components.Server.Circuits.RemoteJSRuntime.BeginInvokeJS(JSInvocationInfo& invocationInfo)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, CancellationToken cancellationToken, Object[] args)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, Object[] args)    at Microsoft.JSInterop.JSRuntimeExtensions.InvokeVoidAsync(IJSRuntime jsRuntime, String identifier, Object[] args)    at Microsoft.JSInterop.Implementation.JSObjectReference.DisposeAsync()    at MudX.MudXProvider.DisposeAsync()    at Microsoft.AspNetCore.Components.RenderTree.Renderer.<>c__DisplayClass105_0.<<Dispose>g__HandleAsyncExceptions|0>d.MoveNext()
2026-10-03T16:40:26.110Z fail: Microsoft.AspNetCore.Components.Server.Circuits.CircuitHost[111] Unhandled exception in circuit 'yyP9LXS8hZsX4Ses0rzU6shipn3qhd-Qv-ImCrfYLEw'. Microsoft.JSInterop.JSDisconnectedException: JavaScript interop calls cannot be issued at this time. This is because the circuit has disconnected and is being disposed.    at Microsoft.AspNetCore.Components.Server.Circuits.RemoteJSRuntime.BeginInvokeJS(JSInvocationInfo& invocationInfo)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, CancellationToken cancellationToken, Object[] args)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, Object[] args)    at Microsoft.JSInterop.JSRuntimeExtensions.InvokeVoidAsync(IJSRuntime jsRuntime, String identifier, Object[] args)    at Microsoft.JSInterop.Implementation.JSObjectReference.DisposeAsync()    at MudX.MudXProvider.DisposeAsync()    at Microsoft.AspNetCore.Components.RenderTree.Renderer.<>c__DisplayClass105_0.<<Dispose>g__HandleAsyncExceptions|0>d.MoveNext()
2026-10-03T16:40:26.112Z warn: Microsoft.AspNetCore.Components.Server.Circuits.RemoteRenderer[100] Unhandled exception rendering component: JavaScript interop calls cannot be issued at this time. This is because the circuit has disconnected and is being disposed. Microsoft.JSInterop.JSDisconnectedException: JavaScript interop calls cannot be issued at this time. This is because the circuit has disconnected and is being disposed.    at Microsoft.AspNetCore.Components.Server.Circuits.RemoteJSRuntime.BeginInvokeJS(JSInvocationInfo& invocationInfo)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, CancellationToken cancellationToken, Object[] args)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, Object[] args)    at Microsoft.JSInterop.JSRuntimeExtensions.InvokeVoidAsync(IJSRuntime jsRuntime, String identifier, Object[] args)    at Microsoft.JSInterop.Implementation.JSObjectReference.DisposeAsync()    at MudX.MudXProvider.DisposeAsync()    at Microsoft.AspNetCore.Components.RenderTree.Renderer.<>c__DisplayClass105_0.<<Dispose>g__HandleAsyncExceptions|0>d.MoveNext()
2026-10-03T16:40:26.112Z fail: Microsoft.AspNetCore.Components.Server.Circuits.CircuitHost[111] Unhandled exception in circuit '-oFAA8niW3UmwQoiyS9Nazq5OJdsvxAm8aZCD2az-9g'. Microsoft.JSInterop.JSDisconnectedException: JavaScript interop calls cannot be issued at this time. This is because the circuit has disconnected and is being disposed.    at Microsoft.AspNetCore.Components.Server.Circuits.RemoteJSRuntime.BeginInvokeJS(JSInvocationInfo& invocationInfo)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, CancellationToken cancellationToken, Object[] args)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, Object[] args)    at Microsoft.JSInterop.JSRuntimeExtensions.InvokeVoidAsync(IJSRuntime jsRuntime, String identifier, Object[] args)    at Microsoft.JSInterop.Implementation.JSObjectReference.DisposeAsync()    at MudX.MudXProvider.DisposeAsync()    at Microsoft.AspNetCore.Components.RenderTree.Renderer.<>c__DisplayClass105_0.<<Dispose>g__HandleAsyncExceptions|0>d.MoveNext()
2026-10-03T16:40:28.249Z warn: Microsoft.AspNetCore.Components.Server.Circuits.RemoteRenderer[100] Unhandled exception rendering component: JavaScript interop calls cannot be issued at this time. This is because the circuit has disconnected and is being disposed. Microsoft.JSInterop.JSDisconnectedException: JavaScript interop calls cannot be issued at this time. This is because the circuit has disconnected and is being disposed.    at Microsoft.AspNetCore.Components.Server.Circuits.RemoteJSRuntime.BeginInvokeJS(JSInvocationInfo& invocationInfo)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, CancellationToken cancellationToken, Object[] args)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, Object[] args)    at Microsoft.JSInterop.JSRuntimeExtensions.InvokeVoidAsync(IJSRuntime jsRuntime, String identifier, Object[] args)    at Microsoft.JSInterop.Implementation.JSObjectReference.DisposeAsync()    at MudX.MudXProvider.DisposeAsync()    at Microsoft.AspNetCore.Components.RenderTree.Renderer.<>c__DisplayClass105_0.<<Dispose>g__HandleAsyncExceptions|0>d.MoveNext()
2026-10-03T16:40:28.251Z warn: Microsoft.AspNetCore.Components.Server.Circuits.RemoteRenderer[100] Unhandled exception rendering component: JavaScript interop calls cannot be issued at this time. This is because the circuit has disconnected and is being disposed. Microsoft.JSInterop.JSDisconnectedException: JavaScript interop calls cannot be issued at this time. This is because the circuit has disconnected and is being disposed.    at Microsoft.AspNetCore.Components.Server.Circuits.RemoteJSRuntime.BeginInvokeJS(JSInvocationInfo& invocationInfo)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, CancellationToken cancellationToken, Object[] args)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, Object[] args)    at Microsoft.JSInterop.JSRuntimeExtensions.InvokeVoidAsync(IJSRuntime jsRuntime, String identifier, Object[] args)    at Microsoft.JSInterop.Implementation.JSObjectReference.DisposeAsync()    at MudX.MudXProvider.DisposeAsync()    at Microsoft.AspNetCore.Components.RenderTree.Renderer.<>c__DisplayClass105_0.<<Dispose>g__HandleAsyncExceptions|0>d.MoveNext()
2026-10-03T16:40:28.252Z fail: Microsoft.AspNetCore.Components.Server.Circuits.CircuitHost[111] Unhandled exception in circuit 'Jr9x64RPuEQ79Z4CvuXSpLyL8A84AkfsyY-L-dBk8wM'. Microsoft.JSInterop.JSDisconnectedException: JavaScript interop calls cannot be issued at this time. This is because the circuit has disconnected and is being disposed.    at Microsoft.AspNetCore.Components.Server.Circuits.RemoteJSRuntime.BeginInvokeJS(JSInvocationInfo& invocationInfo)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, CancellationToken cancellationToken, Object[] args)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, Object[] args)    at Microsoft.JSInterop.JSRuntimeExtensions.InvokeVoidAsync(IJSRuntime jsRuntime, String identifier, Object[] args)    at Microsoft.JSInterop.Implementation.JSObjectReference.DisposeAsync()    at MudX.MudXProvider.DisposeAsync()    at Microsoft.AspNetCore.Components.RenderTree.Renderer.<>c__DisplayClass105_0.<<Dispose>g__HandleAsyncExceptions|0>d.MoveNext()
2026-10-03T16:40:28.252Z fail: Microsoft.AspNetCore.Components.Server.Circuits.CircuitHost[111] Unhandled exception in circuit 'dE0jwq01HHfwKPvQ3OyvA-P5OEeYrOZ_HRij6_YuAg4'. Microsoft.JSInterop.JSDisconnectedException: JavaScript interop calls cannot be issued at this time. This is because the circuit has disconnected and is being disposed.    at Microsoft.AspNetCore.Components.Server.Circuits.RemoteJSRuntime.BeginInvokeJS(JSInvocationInfo& invocationInfo)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, CancellationToken cancellationToken, Object[] args)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, Object[] args)    at Microsoft.JSInterop.JSRuntimeExtensions.InvokeVoidAsync(IJSRuntime jsRuntime, String identifier, Object[] args)    at Microsoft.JSInterop.Implementation.JSObjectReference.DisposeAsync()    at MudX.MudXProvider.DisposeAsync()    at Microsoft.AspNetCore.Components.RenderTree.Renderer.<>c__DisplayClass105_0.<<Dispose>g__HandleAsyncExceptions|0>d.MoveNext()
2026-10-03T16:40:29.434Z warn: Microsoft.AspNetCore.Components.Server.Circuits.RemoteRenderer[100] Unhandled exception rendering component: JavaScript interop calls cannot be issued at this time. This is because the circuit has disconnected and is being disposed. Microsoft.JSInterop.JSDisconnectedException: JavaScript interop calls cannot be issued at this time. This is because the circuit has disconnected and is being disposed.    at Microsoft.AspNetCore.Components.Server.Circuits.RemoteJSRuntime.BeginInvokeJS(JSInvocationInfo& invocationInfo)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, CancellationToken cancellationToken, Object[] args)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, Object[] args)    at Microsoft.JSInterop.JSRuntimeExtensions.InvokeVoidAsync(IJSRuntime jsRuntime, String identifier, Object[] args)    at Microsoft.JSInterop.Implementation.JSObjectReference.DisposeAsync()    at MudX.MudXProvider.DisposeAsync()    at Microsoft.AspNetCore.Components.RenderTree.Renderer.<>c__DisplayClass105_0.<<Dispose>g__HandleAsyncExceptions|0>d.MoveNext()
2026-10-03T16:40:29.434Z fail: Microsoft.AspNetCore.Components.Server.Circuits.CircuitHost[111] Unhandled exception in circuit 'zUyu9ObVLxkM4dmmSuc_dlc0H8TnYdmfcD6oOUYAECE'. Microsoft.JSInterop.JSDisconnectedException: JavaScript interop calls cannot be issued at this time. This is because the circuit has disconnected and is being disposed.    at Microsoft.AspNetCore.Components.Server.Circuits.RemoteJSRuntime.BeginInvokeJS(JSInvocationInfo& invocationInfo)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, CancellationToken cancellationToken, Object[] args)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, Object[] args)    at Microsoft.JSInterop.JSRuntimeExtensions.InvokeVoidAsync(IJSRuntime jsRuntime, String identifier, Object[] args)    at Microsoft.JSInterop.Implementation.JSObjectReference.DisposeAsync()    at MudX.MudXProvider.DisposeAsync()    at Microsoft.AspNetCore.Components.RenderTree.Renderer.<>c__DisplayClass105_0.<<Dispose>g__HandleAsyncExceptions|0>d.MoveNext()
2026-10-03T16:40:29.437Z warn: Microsoft.AspNetCore.Components.Server.Circuits.RemoteRenderer[100] Unhandled exception rendering component: JavaScript interop calls cannot be issued at this time. This is because the circuit has disconnected and is being disposed. Microsoft.JSInterop.JSDisconnectedException: JavaScript interop calls cannot be issued at this time. This is because the circuit has disconnected and is being disposed.    at Microsoft.AspNetCore.Components.Server.Circuits.RemoteJSRuntime.BeginInvokeJS(JSInvocationInfo& invocationInfo)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, CancellationToken cancellationToken, Object[] args)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, Object[] args)    at Microsoft.JSInterop.JSRuntimeExtensions.InvokeVoidAsync(IJSRuntime jsRuntime, String identifier, Object[] args)    at Microsoft.JSInterop.Implementation.JSObjectReference.DisposeAsync()    at MudX.MudXProvider.DisposeAsync()    at Microsoft.AspNetCore.Components.RenderTree.Renderer.<>c__DisplayClass105_0.<<Dispose>g__HandleAsyncExceptions|0>d.MoveNext()
2026-10-03T16:40:29.437Z fail: Microsoft.AspNetCore.Components.Server.Circuits.CircuitHost[111] Unhandled exception in circuit 'Sr51X9osotOtc9PR0g8OesvRwJzYPyJQun9dYgUL9HU'. Microsoft.JSInterop.JSDisconnectedException: JavaScript interop calls cannot be issued at this time. This is because the circuit has disconnected and is being disposed.    at Microsoft.AspNetCore.Components.Server.Circuits.RemoteJSRuntime.BeginInvokeJS(JSInvocationInfo& invocationInfo)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, CancellationToken cancellationToken, Object[] args)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, Object[] args)    at Microsoft.JSInterop.JSRuntimeExtensions.InvokeVoidAsync(IJSRuntime jsRuntime, String identifier, Object[] args)    at Microsoft.JSInterop.Implementation.JSObjectReference.DisposeAsync()    at MudX.MudXProvider.DisposeAsync()    at Microsoft.AspNetCore.Components.RenderTree.Renderer.<>c__DisplayClass105_0.<<Dispose>g__HandleAsyncExceptions|0>d.MoveNext()
2026-10-03T16:40:31.171Z warn: Microsoft.AspNetCore.Components.Server.Circuits.RemoteRenderer[100] Unhandled exception rendering component: JavaScript interop calls cannot be issued at this time. This is because the circuit has disconnected and is being disposed. Microsoft.JSInterop.JSDisconnectedException: JavaScript interop calls cannot be issued at this time. This is because the circuit has disconnected and is being disposed.    at Microsoft.AspNetCore.Components.Server.Circuits.RemoteJSRuntime.BeginInvokeJS(JSInvocationInfo& invocationInfo)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, CancellationToken cancellationToken, Object[] args)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, Object[] args)    at Microsoft.JSInterop.JSRuntimeExtensions.InvokeVoidAsync(IJSRuntime jsRuntime, String identifier, Object[] args)    at Microsoft.JSInterop.Implementation.JSObjectReference.DisposeAsync()    at MudX.MudXProvider.DisposeAsync()    at Microsoft.AspNetCore.Components.RenderTree.Renderer.<>c__DisplayClass105_0.<<Dispose>g__HandleAsyncExceptions|0>d.MoveNext()
2026-10-03T16:40:31.171Z fail: Microsoft.AspNetCore.Components.Server.Circuits.CircuitHost[111] Unhandled exception in circuit 'd1j2rTTrZTWYbtZYy2FqItFJwjw-JvrlldTKp5QHR-4'. Microsoft.JSInterop.JSDisconnectedException: JavaScript interop calls cannot be issued at this time. This is because the circuit has disconnected and is being disposed.    at Microsoft.AspNetCore.Components.Server.Circuits.RemoteJSRuntime.BeginInvokeJS(JSInvocationInfo& invocationInfo)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, CancellationToken cancellationToken, Object[] args)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, Object[] args)    at Microsoft.JSInterop.JSRuntimeExtensions.InvokeVoidAsync(IJSRuntime jsRuntime, String identifier, Object[] args)    at Microsoft.JSInterop.Implementation.JSObjectReference.DisposeAsync()    at MudX.MudXProvider.DisposeAsync()    at Microsoft.AspNetCore.Components.RenderTree.Renderer.<>c__DisplayClass105_0.<<Dispose>g__HandleAsyncExceptions|0>d.MoveNext()
2026-10-03T16:40:31.171Z warn: Microsoft.AspNetCore.Components.Server.Circuits.RemoteRenderer[100] Unhandled exception rendering component: JavaScript interop calls cannot be issued at this time. This is because the circuit has disconnected and is being disposed. Microsoft.JSInterop.JSDisconnectedException: JavaScript interop calls cannot be issued at this time. This is because the circuit has disconnected and is being disposed.    at Microsoft.AspNetCore.Components.Server.Circuits.RemoteJSRuntime.BeginInvokeJS(JSInvocationInfo& invocationInfo)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, CancellationToken cancellationToken, Object[] args)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, Object[] args)    at Microsoft.JSInterop.JSRuntimeExtensions.InvokeVoidAsync(IJSRuntime jsRuntime, String identifier, Object[] args)    at Microsoft.JSInterop.Implementation.JSObjectReference.DisposeAsync()    at MudX.MudXProvider.DisposeAsync()    at Microsoft.AspNetCore.Components.RenderTree.Renderer.<>c__DisplayClass105_0.<<Dispose>g__HandleAsyncExceptions|0>d.MoveNext()
2026-10-03T16:40:31.172Z fail: Microsoft.AspNetCore.Components.Server.Circuits.CircuitHost[111] Unhandled exception in circuit 'efVMlDboHdJxN2Iu1mrfy4yGRMe4VGYzgxHdsq_KJro'. Microsoft.JSInterop.JSDisconnectedException: JavaScript interop calls cannot be issued at this time. This is because the circuit has disconnected and is being disposed.    at Microsoft.AspNetCore.Components.Server.Circuits.RemoteJSRuntime.BeginInvokeJS(JSInvocationInfo& invocationInfo)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, CancellationToken cancellationToken, Object[] args)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, Object[] args)    at Microsoft.JSInterop.JSRuntimeExtensions.InvokeVoidAsync(IJSRuntime jsRuntime, String identifier, Object[] args)    at Microsoft.JSInterop.Implementation.JSObjectReference.DisposeAsync()    at MudX.MudXProvider.DisposeAsync()    at Microsoft.AspNetCore.Components.RenderTree.Renderer.<>c__DisplayClass105_0.<<Dispose>g__HandleAsyncExceptions|0>d.MoveNext()
2026-10-03T16:40:32.566Z warn: Microsoft.AspNetCore.Components.Server.Circuits.RemoteRenderer[100] Unhandled exception rendering component: JavaScript interop calls cannot be issued at this time. This is because the circuit has disconnected and is being disposed. Microsoft.JSInterop.JSDisconnectedException: JavaScript interop calls cannot be issued at this time. This is because the circuit has disconnected and is being disposed.    at Microsoft.AspNetCore.Components.Server.Circuits.RemoteJSRuntime.BeginInvokeJS(JSInvocationInfo& invocationInfo)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, CancellationToken cancellationToken, Object[] args)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, Object[] args)    at Microsoft.JSInterop.JSRuntimeExtensions.InvokeVoidAsync(IJSRuntime jsRuntime, String identifier, Object[] args)    at Microsoft.JSInterop.Implementation.JSObjectReference.DisposeAsync()    at MudX.MudXProvider.DisposeAsync()    at Microsoft.AspNetCore.Components.RenderTree.Renderer.<>c__DisplayClass105_0.<<Dispose>g__HandleAsyncExceptions|0>d.MoveNext()
2026-10-03T16:40:32.567Z fail: Microsoft.AspNetCore.Components.Server.Circuits.CircuitHost[111] Unhandled exception in circuit 'oGxLycSSGgljbK3TbkQo-9XJsiMr4SIphjdHomZ2wNk'. Microsoft.JSInterop.JSDisconnectedException: JavaScript interop calls cannot be issued at this time. This is because the circuit has disconnected and is being disposed.    at Microsoft.AspNetCore.Components.Server.Circuits.RemoteJSRuntime.BeginInvokeJS(JSInvocationInfo& invocationInfo)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, CancellationToken cancellationToken, Object[] args)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, Object[] args)    at Microsoft.JSInterop.JSRuntimeExtensions.InvokeVoidAsync(IJSRuntime jsRuntime, String identifier, Object[] args)    at Microsoft.JSInterop.Implementation.JSObjectReference.DisposeAsync()    at MudX.MudXProvider.DisposeAsync()    at Microsoft.AspNetCore.Components.RenderTree.Renderer.<>c__DisplayClass105_0.<<Dispose>g__HandleAsyncExceptions|0>d.MoveNext()
2026-10-03T16:40:33.027Z warn: Microsoft.AspNetCore.Components.Server.Circuits.RemoteRenderer[100] Unhandled exception rendering component: JavaScript interop calls cannot be issued at this time. This is because the circuit has disconnected and is being disposed. Microsoft.JSInterop.JSDisconnectedException: JavaScript interop calls cannot be issued at this time. This is because the circuit has disconnected and is being disposed.    at Microsoft.AspNetCore.Components.Server.Circuits.RemoteJSRuntime.BeginInvokeJS(JSInvocationInfo& invocationInfo)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, CancellationToken cancellationToken, Object[] args)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, Object[] args)    at Microsoft.JSInterop.JSRuntimeExtensions.InvokeVoidAsync(IJSRuntime jsRuntime, String identifier, Object[] args)    at Microsoft.JSInterop.Implementation.JSObjectReference.DisposeAsync()    at MudX.MudXProvider.DisposeAsync()    at Microsoft.AspNetCore.Components.RenderTree.Renderer.<>c__DisplayClass105_0.<<Dispose>g__HandleAsyncExceptions|0>d.MoveNext()
2026-10-03T16:40:33.028Z fail: Microsoft.AspNetCore.Components.Server.Circuits.CircuitHost[111] Unhandled exception in circuit 'tuS0SgG1hpXUkPX8ATUmBQSLd8rL9WbDeMq8wqCUqkc'. Microsoft.JSInterop.JSDisconnectedException: JavaScript interop calls cannot be issued at this time. This is because the circuit has disconnected and is being disposed.    at Microsoft.AspNetCore.Components.Server.Circuits.RemoteJSRuntime.BeginInvokeJS(JSInvocationInfo& invocationInfo)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, CancellationToken cancellationToken, Object[] args)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, Object[] args)    at Microsoft.JSInterop.JSRuntimeExtensions.InvokeVoidAsync(IJSRuntime jsRuntime, String identifier, Object[] args)    at Microsoft.JSInterop.Implementation.JSObjectReference.DisposeAsync()    at MudX.MudXProvider.DisposeAsync()    at Microsoft.AspNetCore.Components.RenderTree.Renderer.<>c__DisplayClass105_0.<<Dispose>g__HandleAsyncExceptions|0>d.MoveNext()
2026-10-03T16:40:33.028Z warn: Microsoft.AspNetCore.Components.Server.Circuits.RemoteRenderer[100] Unhandled exception rendering component: JavaScript interop calls cannot be issued at this time. This is because the circuit has disconnected and is being disposed. Microsoft.JSInterop.JSDisconnectedException: JavaScript interop calls cannot be issued at this time. This is because the circuit has disconnected and is being disposed.    at Microsoft.AspNetCore.Components.Server.Circuits.RemoteJSRuntime.BeginInvokeJS(JSInvocationInfo& invocationInfo)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, CancellationToken cancellationToken, Object[] args)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, Object[] args)    at Microsoft.JSInterop.JSRuntimeExtensions.InvokeVoidAsync(IJSRuntime jsRuntime, String identifier, Object[] args)    at Microsoft.JSInterop.Implementation.JSObjectReference.DisposeAsync()    at MudX.MudXProvider.DisposeAsync()    at Microsoft.AspNetCore.Components.RenderTree.Renderer.<>c__DisplayClass105_0.<<Dispose>g__HandleAsyncExceptions|0>d.MoveNext()
2026-10-03T16:40:33.028Z fail: Microsoft.AspNetCore.Components.Server.Circuits.CircuitHost[111] Unhandled exception in circuit 'pg_nZpQMyPDHJHle1ipvO777gtuBZVNsE7GynuLEoLs'. Microsoft.JSInterop.JSDisconnectedException: JavaScript interop calls cannot be issued at this time. This is because the circuit has disconnected and is being disposed.    at Microsoft.AspNetCore.Components.Server.Circuits.RemoteJSRuntime.BeginInvokeJS(JSInvocationInfo& invocationInfo)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, CancellationToken cancellationToken, Object[] args)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, Object[] args)    at Microsoft.JSInterop.JSRuntimeExtensions.InvokeVoidAsync(IJSRuntime jsRuntime, String identifier, Object[] args)    at Microsoft.JSInterop.Implementation.JSObjectReference.DisposeAsync()    at MudX.MudXProvider.DisposeAsync()    at Microsoft.AspNetCore.Components.RenderTree.Renderer.<>c__DisplayClass105_0.<<Dispose>g__HandleAsyncExceptions|0>d.MoveNext()
2026-10-03T16:40:34.838Z warn: Microsoft.AspNetCore.Components.Server.Circuits.RemoteRenderer[100] Unhandled exception rendering component: JavaScript interop calls cannot be issued at this time. This is because the circuit has disconnected and is being disposed. Microsoft.JSInterop.JSDisconnectedException: JavaScript interop calls cannot be issued at this time. This is because the circuit has disconnected and is being disposed.    at Microsoft.AspNetCore.Components.Server.Circuits.RemoteJSRuntime.BeginInvokeJS(JSInvocationInfo& invocationInfo)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, CancellationToken cancellationToken, Object[] args)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, Object[] args)    at Microsoft.JSInterop.JSRuntimeExtensions.InvokeVoidAsync(IJSRuntime jsRuntime, String identifier, Object[] args)    at Microsoft.JSInterop.Implementation.JSObjectReference.DisposeAsync()    at MudX.MudXProvider.DisposeAsync()    at Microsoft.AspNetCore.Components.RenderTree.Renderer.<>c__DisplayClass105_0.<<Dispose>g__HandleAsyncExceptions|0>d.MoveNext()
2026-10-03T16:40:34.838Z fail: Microsoft.AspNetCore.Components.Server.Circuits.CircuitHost[111] Unhandled exception in circuit 'yP8qdyNRAKZ4IUAxZGH2lhKE_N3z4xIGwFnjwLay_rc'. Microsoft.JSInterop.JSDisconnectedException: JavaScript interop calls cannot be issued at this time. This is because the circuit has disconnected and is being disposed.    at Microsoft.AspNetCore.Components.Server.Circuits.RemoteJSRuntime.BeginInvokeJS(JSInvocationInfo& invocationInfo)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, CancellationToken cancellationToken, Object[] args)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, Object[] args)    at Microsoft.JSInterop.JSRuntimeExtensions.InvokeVoidAsync(IJSRuntime jsRuntime, String identifier, Object[] args)    at Microsoft.JSInterop.Implementation.JSObjectReference.DisposeAsync()    at MudX.MudXProvider.DisposeAsync()    at Microsoft.AspNetCore.Components.RenderTree.Renderer.<>c__DisplayClass105_0.<<Dispose>g__HandleAsyncExceptions|0>d.MoveNext()
2026-10-03T16:40:34.841Z warn: Microsoft.AspNetCore.Components.Server.Circuits.RemoteRenderer[100] Unhandled exception rendering component: JavaScript interop calls cannot be issued at this time. This is because the circuit has disconnected and is being disposed. Microsoft.JSInterop.JSDisconnectedException: JavaScript interop calls cannot be issued at this time. This is because the circuit has disconnected and is being disposed.    at Microsoft.AspNetCore.Components.Server.Circuits.RemoteJSRuntime.BeginInvokeJS(JSInvocationInfo& invocationInfo)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, CancellationToken cancellationToken, Object[] args)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, Object[] args)    at Microsoft.JSInterop.JSRuntimeExtensions.InvokeVoidAsync(IJSRuntime jsRuntime, String identifier, Object[] args)    at Microsoft.JSInterop.Implementation.JSObjectReference.DisposeAsync()    at MudX.MudXProvider.DisposeAsync()    at Microsoft.AspNetCore.Components.RenderTree.Renderer.<>c__DisplayClass105_0.<<Dispose>g__HandleAsyncExceptions|0>d.MoveNext()
2026-10-03T16:40:34.841Z fail: Microsoft.AspNetCore.Components.Server.Circuits.CircuitHost[111] Unhandled exception in circuit 'dLrL7EgIBg477-SKdo25D_9pdyqNgVfasNRKcFmgUD8'. Microsoft.JSInterop.JSDisconnectedException: JavaScript interop calls cannot be issued at this time. This is because the circuit has disconnected and is being disposed.    at Microsoft.AspNetCore.Components.Server.Circuits.RemoteJSRuntime.BeginInvokeJS(JSInvocationInfo& invocationInfo)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, CancellationToken cancellationToken, Object[] args)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, Object[] args)    at Microsoft.JSInterop.JSRuntimeExtensions.InvokeVoidAsync(IJSRuntime jsRuntime, String identifier, Object[] args)    at Microsoft.JSInterop.Implementation.JSObjectReference.DisposeAsync()    at MudX.MudXProvider.DisposeAsync()    at Microsoft.AspNetCore.Components.RenderTree.Renderer.<>c__DisplayClass105_0.<<Dispose>g__HandleAsyncExceptions|0>d.MoveNext()
2026-10-03T16:40:38.612Z warn: Microsoft.AspNetCore.Components.Server.Circuits.RemoteRenderer[100] Unhandled exception rendering component: JavaScript interop calls cannot be issued at this time. This is because the circuit has disconnected and is being disposed. Microsoft.JSInterop.JSDisconnectedException: JavaScript interop calls cannot be issued at this time. This is because the circuit has disconnected and is being disposed.    at Microsoft.AspNetCore.Components.Server.Circuits.RemoteJSRuntime.BeginInvokeJS(JSInvocationInfo& invocationInfo)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, CancellationToken cancellationToken, Object[] args)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, Object[] args)    at Microsoft.JSInterop.JSRuntimeExtensions.InvokeVoidAsync(IJSRuntime jsRuntime, String identifier, Object[] args)    at Microsoft.JSInterop.Implementation.JSObjectReference.DisposeAsync()    at MudX.MudXProvider.DisposeAsync()    at Microsoft.AspNetCore.Components.RenderTree.Renderer.<>c__DisplayClass105_0.<<Dispose>g__HandleAsyncExceptions|0>d.MoveNext()
2026-10-03T16:40:38.612Z fail: Microsoft.AspNetCore.Components.Server.Circuits.CircuitHost[111] Unhandled exception in circuit 'jh0nBYKmu3jvERdijWtrac2fD488ui8lTiUycCa9oMc'. Microsoft.JSInterop.JSDisconnectedException: JavaScript interop calls cannot be issued at this time. This is because the circuit has disconnected and is being disposed.    at Microsoft.AspNetCore.Components.Server.Circuits.RemoteJSRuntime.BeginInvokeJS(JSInvocationInfo& invocationInfo)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, CancellationToken cancellationToken, Object[] args)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, Object[] args)    at Microsoft.JSInterop.JSRuntimeExtensions.InvokeVoidAsync(IJSRuntime jsRuntime, String identifier, Object[] args)    at Microsoft.JSInterop.Implementation.JSObjectReference.DisposeAsync()    at MudX.MudXProvider.DisposeAsync()    at Microsoft.AspNetCore.Components.RenderTree.Renderer.<>c__DisplayClass105_0.<<Dispose>g__HandleAsyncExceptions|0>d.MoveNext()
2026-10-03T16:40:38.612Z warn: Microsoft.AspNetCore.Components.Server.Circuits.RemoteRenderer[100] Unhandled exception rendering component: JavaScript interop calls cannot be issued at this time. This is because the circuit has disconnected and is being disposed. Microsoft.JSInterop.JSDisconnectedException: JavaScript interop calls cannot be issued at this time. This is because the circuit has disconnected and is being disposed.    at Microsoft.AspNetCore.Components.Server.Circuits.RemoteJSRuntime.BeginInvokeJS(JSInvocationInfo& invocationInfo)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, CancellationToken cancellationToken, Object[] args)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, Object[] args)    at Microsoft.JSInterop.JSRuntimeExtensions.InvokeVoidAsync(IJSRuntime jsRuntime, String identifier, Object[] args)    at Microsoft.JSInterop.Implementation.JSObjectReference.DisposeAsync()    at MudX.MudXProvider.DisposeAsync()    at Microsoft.AspNetCore.Components.RenderTree.Renderer.<>c__DisplayClass105_0.<<Dispose>g__HandleAsyncExceptions|0>d.MoveNext()
2026-10-03T16:40:38.614Z fail: Microsoft.AspNetCore.Components.Server.Circuits.CircuitHost[111] Unhandled exception in circuit '2oJBOe-Kv8xTSwooSbXg-55vCKsEHeu4TVJighjQsKI'. Microsoft.JSInterop.JSDisconnectedException: JavaScript interop calls cannot be issued at this time. This is because the circuit has disconnected and is being disposed.    at Microsoft.AspNetCore.Components.Server.Circuits.RemoteJSRuntime.BeginInvokeJS(JSInvocationInfo& invocationInfo)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, CancellationToken cancellationToken, Object[] args)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, Object[] args)    at Microsoft.JSInterop.JSRuntimeExtensions.InvokeVoidAsync(IJSRuntime jsRuntime, String identifier, Object[] args)    at Microsoft.JSInterop.Implementation.JSObjectReference.DisposeAsync()    at MudX.MudXProvider.DisposeAsync()    at Microsoft.AspNetCore.Components.RenderTree.Renderer.<>c__DisplayClass105_0.<<Dispose>g__HandleAsyncExceptions|0>d.MoveNext()
```

## Flaky tests (2, passed on retry)

A flaky test is investigated at once; two flaky runs in three consecutive runs block the merge (04 section 1.6).

```text
[phone] [GAL] screenshot gallery › [GAL] SC04-vehicles-list
[phone] [GAL] screenshot gallery › [GAL] SC05-places-list
```

## Acceptance criteria

Report-only until S15 (D50): this table never changes the verdict. A criterion is read from the test titles that carry `[AC-nn]` (a suffix such as `[AC-49a]` counts for AC-49) in the .trx files (dotnet), Playwright's `e2e/results.json` (e2e) and the node TAP of the js job (node); one with no such test is missing. When it has several, failed beats flaky beats partial beats passed. `partial` means a test of the criterion passed and another was skipped, fixme'd or expected to fail: part of the criterion is not verified, so it is never read as `passed`; when every test of it is skipped it is `skipped`.

50 criteria: 48 passed, 0 partial, 0 failed, 0 skipped, 0 flaky, 2 missing.

| AC | status | found in |
|---|---|---|
| AC-01 | passed | e2e 1 |
| AC-02 | passed | e2e 1 |
| AC-03 | passed | e2e 1 |
| AC-04 | passed | e2e 1 |
| AC-05 | passed | dotnet 1, e2e 2 |
| AC-06 | passed | e2e 3 |
| AC-07 | passed | e2e 2 |
| AC-08 | passed | e2e 1 |
| AC-09 | passed | e2e 1 |
| AC-10 | passed | dotnet 4, e2e 2 |
| AC-11 | passed | dotnet 11, e2e 4 |
| AC-12 | passed | e2e 1 |
| AC-13 | passed | e2e 4, node 6 (13a, 13b) |
| AC-14 | passed | e2e 1, node 9 |
| AC-15 | passed | e2e 3, node 1 |
| AC-16 | passed | e2e 3, node 13 (16a, 16b) |
| AC-17 | passed | dotnet 3, e2e 2, node 4 (17b) |
| AC-18 | passed | e2e 1 |
| AC-19 | passed | node 35 (19a, 19b, 19c, 19d) |
| AC-20 | passed | dotnet 2, e2e 1, node 2 |
| AC-21 | passed | e2e 2 |
| AC-22 | passed | dotnet 8, e2e 2 (22a, 22b, 22c, 22d, 22e, 22f) |
| AC-23 | passed | dotnet 8, e2e 2 (23a, 23b, 23c) |
| AC-24 | passed | dotnet 1, e2e 5 |
| AC-25 | passed | dotnet 4, e2e 3 (25a) |
| AC-26 | passed | dotnet 2, e2e 2 |
| AC-27 | passed | dotnet 2, e2e 2 |
| AC-28 | passed | dotnet 5, e2e 5 (28a, 28b, 28c, 28d) |
| AC-29 | passed | dotnet 2, e2e 2 |
| AC-30 | passed | dotnet 9, e2e 2 (30b, 30c, 30d, 30e, 30f, 30g, 30h) |
| AC-31 | passed | dotnet 4, e2e 1 (31a, 31b, 31c, 31d) |
| AC-32 | passed | e2e 1 |
| AC-33 | passed | e2e 2 |
| AC-34 | passed | e2e 3 |
| AC-35 | passed | e2e 2 |
| AC-36 | passed | e2e 1 |
| AC-37 | passed | dotnet 3, e2e 3 |
| AC-38 | passed | dotnet 9, e2e 5 |
| AC-39 | passed | dotnet 2, e2e 5 |
| AC-40 | passed | dotnet 1, e2e 1 |
| AC-41 | passed | dotnet 2, e2e 3 |
| AC-42 | passed | e2e 2 |
| AC-43 | passed | e2e 4 |
| AC-44 | passed | dotnet 1 (44a) |
| AC-45 | missing | — |
| AC-46 | passed | dotnet 2, e2e 1 (46a) |
| AC-47 | passed | dotnet 14, e2e 8 (47a) |
| AC-48 | missing | — |
| AC-49 | passed | dotnet 26, e2e 4 (49a, 49b) |
| AC-50 | passed | dotnet 1, e2e 1 (50a) |

## Docker smoke

- image: realm:smoke
- image size (uncompressed): 253.3 MB, within the target (6.5: target at most 330 MB, warn over 360 MB, fail over 450 MB)
- app layer (published output): 23.4 MB, within the target (6.5: target at most 35 MB, warn over 45 MB)
- compressed size (estimate: gzip of docker save): 110.5 MB, within the target (6.5: target at most 120 MB, warn over 130 MB, fail over 200 MB)
- time to healthy: 0.75 s (item 1: target at most 3 s, warn over 3 s, fail over 10 s)

| item | check | result | detail |
|---|---|---|---|
| 1 | first /healthz 200 | PASS | 0.747 s |
| 2 | base href follows X-Ingress-Path | PASS | '/' without the header, '/api/hassio_ingress/TOKEN/' with it (a 43-character token), an invalid value ignored |
| 3 | content types | PASS | _framework/blazor.web.9hsif5t8mt.js (text/javascript) lib/maplibre-gl/maplibre-gl.mjs (text/javascript) lib/maplibre-gl/maplibre-gl-worker.mjs (text/javascript) js/realmMap.js (text/javascript) fonts/atkinson-hyperlegible-latin-700-normal.woff2 (font/woff2) |
| 4 | Set-Cookie on / (informational) | WARN | GET / sets a cookie: .AspNetCore.Antiforgery.fU4C0xp-sPg (D61: informational; the Blazor antiforgery cookie is expected) |
| 5 | healthcheck mode exits 0 | PASS | dotnet Realm.Web.dll healthcheck exited 0 after 0.148 s |
| 6 | diagnostics.json in Demo | PASS | GET /diagnostics.json answered 200 with "mode": "demo" and "zoneDataOk": true |
| 7 | no unhandled exception in the Demo log | PASS | none among 7 log lines after the start and 6 requests |
| 8 | image size against the 6.5 table | PASS | image 253.3 MB, app layer 23.3 MB, compressed estimate 110.5 MB (not enforced), within the warn lines of 6.5 (image 360.0 MB, app layer 45.0 MB) |
| 9 | image runs as root | PASS | id -u prints 0 |
| 10 | no forbidden path in the image | PASS | 5691 entries checked |
| 11 | Live start, Home Assistant unreachable | PASS | /healthz 200 after 0.955 s, realm.db 94208 bytes and dp-keys in /data, still running 3 s later, no crash among 10 log lines |
| 12 | licence notices in the image | PASS | LICENSE, THIRD-PARTY-NOTICES.md, LICENSES/Apache-2.0.txt and wwwroot/lib/maplibre-gl/LICENSE.txt are in /app; the fonts have an OFL-*.txt beside them |

Items: 11 PASS, 1 WARN, 0 FAIL, 0 SKIP. Sizes are recorded against the table of 03 section 6.5; no item enforces them before item 8 (S16a).

## Screenshots (39)

```text
shots/phone/SC01-location-peek.png  sha256:f6dd8a4cd84a
shots/phone/SC02-drivers-peek.png  sha256:f6dd8a4cd84a
shots/phone/SC03-drivers-tall.png  sha256:b1f3e05f2e65
shots/phone/SC04-vehicles-list.png  sha256:ffb0600821eb
shots/phone/SC05-places-list.png  sha256:bf5397a05b1b
shots/phone/SC06-member-detail.png  sha256:d60fc156a2ba
shots/phone/SC07-peek-selection.png  sha256:cd73ff9a86f3
shots/phone/SC08-style-popover.png  sha256:35718fdc8ef7
shots/phone/SC09-settings-about.png  sha256:d3716c4e3a6a
shots/phone/SC09-settings.png  sha256:d69f617d2e9f
shots/phone/SC10-driving.png  sha256:daa64b11fad9
shots/phone/SC11-popup-drives-miles.png  sha256:d67790601c11
shots/phone/SC11-popup-drives.png  sha256:c8143f3a18ab
shots/phone/SC12-popup-speeding.png  sha256:619d118477ed
shots/phone/SC13-driver-week.png  sha256:f6ba2533df45
shots/phone/SC14-variant-phone-unavailable.png  sha256:378296b758f5
shots/phone/SC15-variant-poor-accuracy.png  sha256:0c4d65de560a
shots/phone/SC16-variant-ha-down.png  sha256:e76641c02a5d
shots/phone/SC17-variant-fresh-install-partial.png  sha256:723591ffbafb
shots/phone/SC17-variant-fresh-install.png  sha256:e60f947a5082
shots/unfolded/SC01-location-peek.png  sha256:c2fbd05a869b
shots/unfolded/SC02-drivers-peek.png  sha256:c2fbd05a869b
shots/unfolded/SC04-vehicles-list.png  sha256:5cd4c1451a1c
shots/unfolded/SC05-places-list.png  sha256:d3d5150bcc83
shots/unfolded/SC06-member-detail.png  sha256:5633aede68e8
shots/unfolded/SC08-style-popover.png  sha256:6c32fffad0d3
shots/unfolded/SC09-settings-about.png  sha256:3cf962427f6d
shots/unfolded/SC09-settings.png  sha256:67e815b1d574
shots/unfolded/SC10-driving.png  sha256:3b776f15cd46
shots/unfolded/SC11-popup-drives-miles.png  sha256:ae943fa5af6b
shots/unfolded/SC11-popup-drives.png  sha256:bad8d3960846
shots/unfolded/SC12-popup-speeding.png  sha256:7b96d2115677
shots/unfolded/SC13-driver-week.png  sha256:9d3e2753d8a7
shots/unfolded/SC14-variant-phone-unavailable.png  sha256:1f28d73bdaca
shots/unfolded/SC15-variant-poor-accuracy.png  sha256:79d0a8f874f5
shots/unfolded/SC16-variant-ha-down.png  sha256:644a925bdf65
shots/unfolded/SC17-variant-fresh-install-partial.png  sha256:ef18bee4f00c
shots/unfolded/SC17-variant-fresh-install.png  sha256:bb7098ab8def
failures/x-04.phone.png  sha256:f6dd8a4cd84a
```
