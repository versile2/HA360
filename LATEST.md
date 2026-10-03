# CI summary: FAILURE

- result: failure
- branch: slice/S15a-a11y
- sha: bbef2eb55c6d78c56fcb1b2c135b0ccaed0560ae
- run: 103
- url: https://github.com/versile2/HA360/actions/runs/37137364465
- why: job dotnet: failure; 2 failed test(s); 10 failed E2E test(s)

## Jobs

| job | result |
|---|---|
| guards | success |
| dotnet | failure |
| js | success |
| docker-smoke | success |
| e2e | failure |

## Guards

PASS: 12 of 12 guards.

## Compiler errors

None.

## Failed tests (2 of 2779)

2777 passed, 2 failed, 0 skipped (5 .trx files).

### Realm.Web.Tests.MainLayoutCascadeTests.ContentInADialog_ReceivesTheShellsSessionAndOverrides

```text
System.InvalidOperationException : Missing <MudPopoverProvider />, please add it to your layout. See https://mudblazor.com/getting-started/installation#manual-install-add-components
```

### Realm.Web.Tests.MainLayoutCascadeTests.ContentInAPopover_ReceivesTheShellsSessionAndOverrides

```text
System.InvalidOperationException : Missing <MudPopoverProvider />, please add it to your layout. See https://mudblazor.com/getting-started/installation#manual-install-add-components
```

## JS

Node tests (node --test of tests/js): 462 passed, 0 failed.

Type check (tsc.log): no "error TS" line.

## Map styles

PASS: 5 (satellite, demo-offline, night, day, streets).

## E2E

168 passed, 10 failed, 0 flaky, 1 skipped (179 test runs in 4 projects).

Payload contract (node --test of tests/contract): 30 passed, 0 failed.

## Failed E2E tests (10)

### [phone] ac-g-a11y.spec.ts › [AC-45] targets, gaps, text size and reflow › [AC-45] Location at Peek: every interactive element is 48 x 48 and 8 px from its neighbours, no text under 12 px

Acceptance criteria: AC-45; at ac-g-a11y.spec.ts:241

```text
Error: Location at Peek: neighbours closer than 8 px

expect(received).toEqual(expected) // deep equality

- Expected  - 1
+ Received  + 5

- Array []
+ Array [
+   "sheet-handle and tab-drivers: 7.0 px",
+   "sheet-handle and tab-vehicles: 7.0 px",
+   "sheet-handle and tab-places: 7.0 px",
+ ]
```

### [phone] ac-g-a11y.spec.ts › [AC-45] targets, gaps, text size and reflow › [AC-45] Location at Peek with a member selected: every interactive element is 48 x 48 and 8 px from its neighbours, no text under 12 px

Acceptance criteria: AC-45; at ac-g-a11y.spec.ts:241

```text
Error: Location at Peek with a member selected: neighbours closer than 8 px

expect(received).toEqual(expected) // deep equality

- Expected  - 1
+ Received  + 3

- Array []
+ Array [
+   "sheet-handle and sheet-selection-clear: 7.0 px",
+ ]
```

### [phone] ac-g-a11y.spec.ts › [AC-45] targets, gaps, text size and reflow › [AC-45] Location at Peek with the vehicle selected: every interactive element is 48 x 48 and 8 px from its neighbours, no text under 12 px

Acceptance criteria: AC-45; at ac-g-a11y.spec.ts:241

```text
Error: Location at Peek with the vehicle selected: neighbours closer than 8 px

expect(received).toEqual(expected) // deep equality

- Expected  - 1
+ Received  + 3

- Array []
+ Array [
+   "sheet-handle and sheet-selection-clear: 7.0 px",
+ ]
```

### [phone] ac-g-a11y.spec.ts › [AC-45] targets, gaps, text size and reflow › [AC-45] Location at Peek with a place selected: every interactive element is 48 x 48 and 8 px from its neighbours, no text under 12 px

Acceptance criteria: AC-45; at ac-g-a11y.spec.ts:241

```text
Error: Location at Peek with a place selected: neighbours closer than 8 px

expect(received).toEqual(expected) // deep equality

- Expected  - 1
+ Received  + 3

- Array []
+ Array [
+   "sheet-handle and sheet-selection-clear: 7.0 px",
+ ]
```

### [phone] ac-g-a11y.spec.ts › [AC-45] targets, gaps, text size and reflow › [AC-45] the 80 % state, Drivers: every interactive element is 48 x 48 and 8 px from its neighbours, no text under 12 px

Acceptance criteria: AC-45; at ac-g-a11y.spec.ts:241

```text
Error: the 80 % state, Drivers: neighbours closer than 8 px

expect(received).toEqual(expected) // deep equality

- Expected  - 1
+ Received  + 5

- Array []
+ Array [
+   "sheet-handle and tab-drivers: 7.0 px",
+   "sheet-handle and tab-vehicles: 7.0 px",
+   "sheet-handle and tab-places: 7.0 px",
+ ]
```

### [phone] ac-g-a11y.spec.ts › [AC-45] targets, gaps, text size and reflow › [AC-45] the 80 % state, Vehicles: every interactive element is 48 x 48 and 8 px from its neighbours, no text under 12 px

Acceptance criteria: AC-45; at ac-g-a11y.spec.ts:241

```text
Error: the 80 % state, Vehicles: neighbours closer than 8 px

expect(received).toEqual(expected) // deep equality

- Expected  - 1
+ Received  + 5

- Array []
+ Array [
+   "sheet-handle and tab-drivers: 7.0 px",
+   "sheet-handle and tab-vehicles: 7.0 px",
+   "sheet-handle and tab-places: 7.0 px",
+ ]
```

### [phone] ac-g-a11y.spec.ts › [AC-45] targets, gaps, text size and reflow › [AC-45] the 80 % state, Places: every interactive element is 48 x 48 and 8 px from its neighbours, no text under 12 px

Acceptance criteria: AC-45; at ac-g-a11y.spec.ts:241

```text
Error: the 80 % state, Places: neighbours closer than 8 px

expect(received).toEqual(expected) // deep equality

- Expected  - 1
+ Received  + 5

- Array []
+ Array [
+   "sheet-handle and tab-drivers: 7.0 px",
+   "sheet-handle and tab-vehicles: 7.0 px",
+   "sheet-handle and tab-places: 7.0 px",
+ ]
```

### [phone] ac-g-a11y.spec.ts › [AC-45] targets, gaps, text size and reflow › [AC-45] person detail: every interactive element is 48 x 48 and 8 px from its neighbours, no text under 12 px

Acceptance criteria: AC-45; at ac-g-a11y.spec.ts:241

```text
Error: person detail: neighbours closer than 8 px

expect(received).toEqual(expected) // deep equality

- Expected  - 1
+ Received  + 5

- Array []
+ Array [
+   "sheet-handle and tab-drivers: 7.0 px",
+   "sheet-handle and tab-vehicles: 7.0 px",
+   "sheet-handle and tab-places: 7.0 px",
+ ]
```

### [phone] ac-g-a11y.spec.ts › [AC-45] targets, gaps, text size and reflow › [AC-45] the 80 % state, Drivers: no horizontal scroll at 320 px and 200 % text

Acceptance criteria: AC-45; at realmMap.n9bt52izui.js:1204

```text
Error: page.evaluate: Error: settled() timed out (style ready true, pending false, work true, loaded true)
    at check (http://127.0.0.1:8123/api/hassio_ingress/AbCdEfGhIjKlMnOpQrStUvWxYz0123456789_-AbCdE/js/realmMap.n9bt52izui.js:1204:27)
```

### [phone] ac-g-a11y.spec.ts › [AC-46] accessible names of 01 section 10.3 › [AC-46] the pin, the bubble and the landmarks of Location carry their exact names and roles

Acceptance criteria: AC-46; at ac-g-a11y.spec.ts:291

```text
Error: member pin

expect(locator).toHaveAccessibleName(expected) failed

Locator:  getByTestId('pin-member-jester')
Expected: "Cass, The Royal Jester. At The Jester's Hall since 9:06 pm. Battery 12 percent, low. 1.0 mile away."
Received: "Cass, The Royal Jester. At The Jester's Hall. Battery 12 percent, low."
Timeout:  8000ms

Call log:
  - member pin getByTestId('pin-member-jester') with timeout 8000ms
  - waiting for getByTestId('pin-member-jester')
    18 × locator resolved to <button type="button" role="button" tabindex="-1" data-testid="pin-member-jester" title="Cass · The Royal Jester" aria-label="Cass, The Royal Jester. At The Jester's Hall. Battery 12 percent, low." class="realm-pin realm-ui realm-pin--member maplibregl-marker maplibregl-marker-anchor-bottom">…</button>
       - unexpected value "Cass, The Royal Jester. At The Jester's Hall. Battery 12 percent, low."
```

## App log (last 30 lines of e2e/app.log)

```text
2026-10-03T16:41:22.334Z fail: Microsoft.AspNetCore.Components.Server.Circuits.CircuitHost[111] Unhandled exception in circuit 'Fb1joCmKfN0GRZfGVGPKzmt_BrxQylb7EoTaGkmgTSQ'. Microsoft.JSInterop.JSDisconnectedException: JavaScript interop calls cannot be issued at this time. This is because the circuit has disconnected and is being disposed.    at Microsoft.AspNetCore.Components.Server.Circuits.RemoteJSRuntime.BeginInvokeJS(JSInvocationInfo& invocationInfo)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, CancellationToken cancellationToken, Object[] args)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, Object[] args)    at Microsoft.JSInterop.JSRuntimeExtensions.InvokeVoidAsync(IJSRuntime jsRuntime, String identifier, Object[] args)    at Microsoft.JSInterop.Implementation.JSObjectReference.DisposeAsync()    at MudX.MudXProvider.DisposeAsync()    at Microsoft.AspNetCore.Components.RenderTree.Renderer.<>c__DisplayClass105_0.<<Dispose>g__HandleAsyncExceptions|0>d.MoveNext()
2026-10-03T16:41:26.009Z warn: Microsoft.AspNetCore.Components.Server.Circuits.RemoteRenderer[100] Unhandled exception rendering component: JavaScript interop calls cannot be issued at this time. This is because the circuit has disconnected and is being disposed. Microsoft.JSInterop.JSDisconnectedException: JavaScript interop calls cannot be issued at this time. This is because the circuit has disconnected and is being disposed.    at Microsoft.AspNetCore.Components.Server.Circuits.RemoteJSRuntime.BeginInvokeJS(JSInvocationInfo& invocationInfo)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, CancellationToken cancellationToken, Object[] args)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, Object[] args)    at Microsoft.JSInterop.JSRuntimeExtensions.InvokeVoidAsync(IJSRuntime jsRuntime, String identifier, Object[] args)    at Microsoft.JSInterop.Implementation.JSObjectReference.DisposeAsync()    at MudX.MudXProvider.DisposeAsync()    at Microsoft.AspNetCore.Components.RenderTree.Renderer.<>c__DisplayClass105_0.<<Dispose>g__HandleAsyncExceptions|0>d.MoveNext()
2026-10-03T16:41:26.009Z fail: Microsoft.AspNetCore.Components.Server.Circuits.CircuitHost[111] Unhandled exception in circuit 'BgjTK2un5zssCKHbuI7upYXrrC7t8zfRi4rSkADh8nE'. Microsoft.JSInterop.JSDisconnectedException: JavaScript interop calls cannot be issued at this time. This is because the circuit has disconnected and is being disposed.    at Microsoft.AspNetCore.Components.Server.Circuits.RemoteJSRuntime.BeginInvokeJS(JSInvocationInfo& invocationInfo)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, CancellationToken cancellationToken, Object[] args)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, Object[] args)    at Microsoft.JSInterop.JSRuntimeExtensions.InvokeVoidAsync(IJSRuntime jsRuntime, String identifier, Object[] args)    at Microsoft.JSInterop.Implementation.JSObjectReference.DisposeAsync()    at MudX.MudXProvider.DisposeAsync()    at Microsoft.AspNetCore.Components.RenderTree.Renderer.<>c__DisplayClass105_0.<<Dispose>g__HandleAsyncExceptions|0>d.MoveNext()
2026-10-03T16:41:26.011Z warn: Microsoft.AspNetCore.Components.Server.Circuits.RemoteRenderer[100] Unhandled exception rendering component: JavaScript interop calls cannot be issued at this time. This is because the circuit has disconnected and is being disposed. Microsoft.JSInterop.JSDisconnectedException: JavaScript interop calls cannot be issued at this time. This is because the circuit has disconnected and is being disposed.    at Microsoft.AspNetCore.Components.Server.Circuits.RemoteJSRuntime.BeginInvokeJS(JSInvocationInfo& invocationInfo)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, CancellationToken cancellationToken, Object[] args)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, Object[] args)    at Microsoft.JSInterop.JSRuntimeExtensions.InvokeVoidAsync(IJSRuntime jsRuntime, String identifier, Object[] args)    at Microsoft.JSInterop.Implementation.JSObjectReference.DisposeAsync()    at MudX.MudXProvider.DisposeAsync()    at Microsoft.AspNetCore.Components.RenderTree.Renderer.<>c__DisplayClass105_0.<<Dispose>g__HandleAsyncExceptions|0>d.MoveNext()
2026-10-03T16:41:26.011Z fail: Microsoft.AspNetCore.Components.Server.Circuits.CircuitHost[111] Unhandled exception in circuit 'dz9-HBSpP4c67ZmYjLZ4TZ1G_Ljq97woXIDRkLxMhu0'. Microsoft.JSInterop.JSDisconnectedException: JavaScript interop calls cannot be issued at this time. This is because the circuit has disconnected and is being disposed.    at Microsoft.AspNetCore.Components.Server.Circuits.RemoteJSRuntime.BeginInvokeJS(JSInvocationInfo& invocationInfo)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, CancellationToken cancellationToken, Object[] args)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, Object[] args)    at Microsoft.JSInterop.JSRuntimeExtensions.InvokeVoidAsync(IJSRuntime jsRuntime, String identifier, Object[] args)    at Microsoft.JSInterop.Implementation.JSObjectReference.DisposeAsync()    at MudX.MudXProvider.DisposeAsync()    at Microsoft.AspNetCore.Components.RenderTree.Renderer.<>c__DisplayClass105_0.<<Dispose>g__HandleAsyncExceptions|0>d.MoveNext()
2026-10-03T16:41:28.585Z warn: Realm.Web.Pages.LocationPage[0] The Location page could not attach the browser history. System.Threading.Tasks.TaskCanceledException: A task was canceled.    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, Object[] args)    at Realm.Web.State.HistoryInterop.AttachAsync(HistorySync sync, IJSRuntime js, HistorySurface surface, Func`1 onLeave) in /_/src/Realm.Web/State/HistoryInterop.cs:line 43    at Realm.Web.State.HistoryInterop.AttachAsync(HistorySync sync, IJSRuntime js, HistorySurface surface, Func`1 onLeave) in /_/src/Realm.Web/State/HistoryInterop.cs:line 55    at Realm.Web.Pages.LocationPage.AttachHistoryAsync() in /_/src/Realm.Web/Pages/LocationPage.razor:line 312
2026-10-03T16:41:36.388Z warn: Microsoft.AspNetCore.Components.Server.Circuits.RemoteRenderer[100] Unhandled exception rendering component: JavaScript interop calls cannot be issued at this time. This is because the circuit has disconnected and is being disposed. Microsoft.JSInterop.JSDisconnectedException: JavaScript interop calls cannot be issued at this time. This is because the circuit has disconnected and is being disposed.    at Microsoft.AspNetCore.Components.Server.Circuits.RemoteJSRuntime.BeginInvokeJS(JSInvocationInfo& invocationInfo)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, CancellationToken cancellationToken, Object[] args)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, Object[] args)    at Microsoft.JSInterop.JSRuntimeExtensions.InvokeVoidAsync(IJSRuntime jsRuntime, String identifier, Object[] args)    at Microsoft.JSInterop.Implementation.JSObjectReference.DisposeAsync()    at MudX.MudXProvider.DisposeAsync()    at Microsoft.AspNetCore.Components.RenderTree.Renderer.<>c__DisplayClass105_0.<<Dispose>g__HandleAsyncExceptions|0>d.MoveNext()
2026-10-03T16:41:36.388Z fail: Microsoft.AspNetCore.Components.Server.Circuits.CircuitHost[111] Unhandled exception in circuit 'QN57V1Fb7VGKCSpFm5bJ_BV_dF4tDiMh8R5sACiQZSs'. Microsoft.JSInterop.JSDisconnectedException: JavaScript interop calls cannot be issued at this time. This is because the circuit has disconnected and is being disposed.    at Microsoft.AspNetCore.Components.Server.Circuits.RemoteJSRuntime.BeginInvokeJS(JSInvocationInfo& invocationInfo)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, CancellationToken cancellationToken, Object[] args)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, Object[] args)    at Microsoft.JSInterop.JSRuntimeExtensions.InvokeVoidAsync(IJSRuntime jsRuntime, String identifier, Object[] args)    at Microsoft.JSInterop.Implementation.JSObjectReference.DisposeAsync()    at MudX.MudXProvider.DisposeAsync()    at Microsoft.AspNetCore.Components.RenderTree.Renderer.<>c__DisplayClass105_0.<<Dispose>g__HandleAsyncExceptions|0>d.MoveNext()
2026-10-03T16:41:36.390Z warn: Microsoft.AspNetCore.Components.Server.Circuits.RemoteRenderer[100] Unhandled exception rendering component: JavaScript interop calls cannot be issued at this time. This is because the circuit has disconnected and is being disposed. Microsoft.JSInterop.JSDisconnectedException: JavaScript interop calls cannot be issued at this time. This is because the circuit has disconnected and is being disposed.    at Microsoft.AspNetCore.Components.Server.Circuits.RemoteJSRuntime.BeginInvokeJS(JSInvocationInfo& invocationInfo)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, CancellationToken cancellationToken, Object[] args)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, Object[] args)    at Microsoft.JSInterop.JSRuntimeExtensions.InvokeVoidAsync(IJSRuntime jsRuntime, String identifier, Object[] args)    at Microsoft.JSInterop.Implementation.JSObjectReference.DisposeAsync()    at MudX.MudXProvider.DisposeAsync()    at Microsoft.AspNetCore.Components.RenderTree.Renderer.<>c__DisplayClass105_0.<<Dispose>g__HandleAsyncExceptions|0>d.MoveNext()
2026-10-03T16:41:36.390Z fail: Microsoft.AspNetCore.Components.Server.Circuits.CircuitHost[111] Unhandled exception in circuit 'p0fbicRgpebv1ETusZNE1rHE7MQiGvPDRJ_Sv7S48uw'. Microsoft.JSInterop.JSDisconnectedException: JavaScript interop calls cannot be issued at this time. This is because the circuit has disconnected and is being disposed.    at Microsoft.AspNetCore.Components.Server.Circuits.RemoteJSRuntime.BeginInvokeJS(JSInvocationInfo& invocationInfo)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, CancellationToken cancellationToken, Object[] args)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, Object[] args)    at Microsoft.JSInterop.JSRuntimeExtensions.InvokeVoidAsync(IJSRuntime jsRuntime, String identifier, Object[] args)    at Microsoft.JSInterop.Implementation.JSObjectReference.DisposeAsync()    at MudX.MudXProvider.DisposeAsync()    at Microsoft.AspNetCore.Components.RenderTree.Renderer.<>c__DisplayClass105_0.<<Dispose>g__HandleAsyncExceptions|0>d.MoveNext()
2026-10-03T16:41:38.664Z warn: Microsoft.AspNetCore.Components.Server.Circuits.RemoteRenderer[100] Unhandled exception rendering component: JavaScript interop calls cannot be issued at this time. This is because the circuit has disconnected and is being disposed. Microsoft.JSInterop.JSDisconnectedException: JavaScript interop calls cannot be issued at this time. This is because the circuit has disconnected and is being disposed.    at Microsoft.AspNetCore.Components.Server.Circuits.RemoteJSRuntime.BeginInvokeJS(JSInvocationInfo& invocationInfo)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, CancellationToken cancellationToken, Object[] args)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, Object[] args)    at Microsoft.JSInterop.JSRuntimeExtensions.InvokeVoidAsync(IJSRuntime jsRuntime, String identifier, Object[] args)    at Microsoft.JSInterop.Implementation.JSObjectReference.DisposeAsync()    at MudX.MudXProvider.DisposeAsync()    at Microsoft.AspNetCore.Components.RenderTree.Renderer.<>c__DisplayClass105_0.<<Dispose>g__HandleAsyncExceptions|0>d.MoveNext()
2026-10-03T16:41:38.665Z warn: Microsoft.AspNetCore.Components.Server.Circuits.RemoteRenderer[100] Unhandled exception rendering component: JavaScript interop calls cannot be issued at this time. This is because the circuit has disconnected and is being disposed. Microsoft.JSInterop.JSDisconnectedException: JavaScript interop calls cannot be issued at this time. This is because the circuit has disconnected and is being disposed.    at Microsoft.AspNetCore.Components.Server.Circuits.RemoteJSRuntime.BeginInvokeJS(JSInvocationInfo& invocationInfo)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, CancellationToken cancellationToken, Object[] args)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, Object[] args)    at Microsoft.JSInterop.JSRuntimeExtensions.InvokeVoidAsync(IJSRuntime jsRuntime, String identifier, Object[] args)    at Microsoft.JSInterop.Implementation.JSObjectReference.DisposeAsync()    at MudX.MudXProvider.DisposeAsync()    at Microsoft.AspNetCore.Components.RenderTree.Renderer.<>c__DisplayClass105_0.<<Dispose>g__HandleAsyncExceptions|0>d.MoveNext()
2026-10-03T16:41:38.666Z fail: Microsoft.AspNetCore.Components.Server.Circuits.CircuitHost[111] Unhandled exception in circuit '6-X9ealP9cAj35RJep083oQ3wU_5unWG5tAubSgMlf0'. Microsoft.JSInterop.JSDisconnectedException: JavaScript interop calls cannot be issued at this time. This is because the circuit has disconnected and is being disposed.    at Microsoft.AspNetCore.Components.Server.Circuits.RemoteJSRuntime.BeginInvokeJS(JSInvocationInfo& invocationInfo)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, CancellationToken cancellationToken, Object[] args)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, Object[] args)    at Microsoft.JSInterop.JSRuntimeExtensions.InvokeVoidAsync(IJSRuntime jsRuntime, String identifier, Object[] args)    at Microsoft.JSInterop.Implementation.JSObjectReference.DisposeAsync()    at MudX.MudXProvider.DisposeAsync()    at Microsoft.AspNetCore.Components.RenderTree.Renderer.<>c__DisplayClass105_0.<<Dispose>g__HandleAsyncExceptions|0>d.MoveNext()
2026-10-03T16:41:38.666Z fail: Microsoft.AspNetCore.Components.Server.Circuits.CircuitHost[111] Unhandled exception in circuit 'aVrfb3lOp-Bb8toLTUhndARMyMG2WWNAzBCR2vNsMa8'. Microsoft.JSInterop.JSDisconnectedException: JavaScript interop calls cannot be issued at this time. This is because the circuit has disconnected and is being disposed.    at Microsoft.AspNetCore.Components.Server.Circuits.RemoteJSRuntime.BeginInvokeJS(JSInvocationInfo& invocationInfo)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, CancellationToken cancellationToken, Object[] args)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, Object[] args)    at Microsoft.JSInterop.JSRuntimeExtensions.InvokeVoidAsync(IJSRuntime jsRuntime, String identifier, Object[] args)    at Microsoft.JSInterop.Implementation.JSObjectReference.DisposeAsync()    at MudX.MudXProvider.DisposeAsync()    at Microsoft.AspNetCore.Components.RenderTree.Renderer.<>c__DisplayClass105_0.<<Dispose>g__HandleAsyncExceptions|0>d.MoveNext()
2026-10-03T16:41:49.404Z warn: Microsoft.AspNetCore.Components.Server.Circuits.RemoteRenderer[100] Unhandled exception rendering component: JavaScript interop calls cannot be issued at this time. This is because the circuit has disconnected and is being disposed. Microsoft.JSInterop.JSDisconnectedException: JavaScript interop calls cannot be issued at this time. This is because the circuit has disconnected and is being disposed.    at Microsoft.AspNetCore.Components.Server.Circuits.RemoteJSRuntime.BeginInvokeJS(JSInvocationInfo& invocationInfo)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, CancellationToken cancellationToken, Object[] args)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, Object[] args)    at Microsoft.JSInterop.JSRuntimeExtensions.InvokeVoidAsync(IJSRuntime jsRuntime, String identifier, Object[] args)    at Microsoft.JSInterop.Implementation.JSObjectReference.DisposeAsync()    at MudX.MudXProvider.DisposeAsync()    at Microsoft.AspNetCore.Components.RenderTree.Renderer.<>c__DisplayClass105_0.<<Dispose>g__HandleAsyncExceptions|0>d.MoveNext()
2026-10-03T16:41:49.404Z warn: Microsoft.AspNetCore.Components.Server.Circuits.RemoteRenderer[100] Unhandled exception rendering component: JavaScript interop calls cannot be issued at this time. This is because the circuit has disconnected and is being disposed. Microsoft.JSInterop.JSDisconnectedException: JavaScript interop calls cannot be issued at this time. This is because the circuit has disconnected and is being disposed.    at Microsoft.AspNetCore.Components.Server.Circuits.RemoteJSRuntime.BeginInvokeJS(JSInvocationInfo& invocationInfo)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, CancellationToken cancellationToken, Object[] args)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, Object[] args)    at Microsoft.JSInterop.JSRuntimeExtensions.InvokeVoidAsync(IJSRuntime jsRuntime, String identifier, Object[] args)    at Microsoft.JSInterop.Implementation.JSObjectReference.DisposeAsync()    at MudX.MudXProvider.DisposeAsync()    at Microsoft.AspNetCore.Components.RenderTree.Renderer.<>c__DisplayClass105_0.<<Dispose>g__HandleAsyncExceptions|0>d.MoveNext()
2026-10-03T16:41:49.404Z fail: Microsoft.AspNetCore.Components.Server.Circuits.CircuitHost[111] Unhandled exception in circuit '0BwtE7nHbnN4DBPmIHUrgS5greUDQnNY5xv7sLQi4gA'. Microsoft.JSInterop.JSDisconnectedException: JavaScript interop calls cannot be issued at this time. This is because the circuit has disconnected and is being disposed.    at Microsoft.AspNetCore.Components.Server.Circuits.RemoteJSRuntime.BeginInvokeJS(JSInvocationInfo& invocationInfo)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, CancellationToken cancellationToken, Object[] args)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, Object[] args)    at Microsoft.JSInterop.JSRuntimeExtensions.InvokeVoidAsync(IJSRuntime jsRuntime, String identifier, Object[] args)    at Microsoft.JSInterop.Implementation.JSObjectReference.DisposeAsync()    at MudX.MudXProvider.DisposeAsync()    at Microsoft.AspNetCore.Components.RenderTree.Renderer.<>c__DisplayClass105_0.<<Dispose>g__HandleAsyncExceptions|0>d.MoveNext()
2026-10-03T16:41:49.404Z fail: Microsoft.AspNetCore.Components.Server.Circuits.CircuitHost[111] Unhandled exception in circuit 'ZHKG3i6o1G6E9N-J8hrd4YuZYpRWA4flk2uBlCDRvlY'. Microsoft.JSInterop.JSDisconnectedException: JavaScript interop calls cannot be issued at this time. This is because the circuit has disconnected and is being disposed.    at Microsoft.AspNetCore.Components.Server.Circuits.RemoteJSRuntime.BeginInvokeJS(JSInvocationInfo& invocationInfo)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, CancellationToken cancellationToken, Object[] args)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, Object[] args)    at Microsoft.JSInterop.JSRuntimeExtensions.InvokeVoidAsync(IJSRuntime jsRuntime, String identifier, Object[] args)    at Microsoft.JSInterop.Implementation.JSObjectReference.DisposeAsync()    at MudX.MudXProvider.DisposeAsync()    at Microsoft.AspNetCore.Components.RenderTree.Renderer.<>c__DisplayClass105_0.<<Dispose>g__HandleAsyncExceptions|0>d.MoveNext()
2026-10-03T16:42:00.832Z warn: Microsoft.AspNetCore.Components.Server.Circuits.RemoteRenderer[100] Unhandled exception rendering component: JavaScript interop calls cannot be issued at this time. This is because the circuit has disconnected and is being disposed. Microsoft.JSInterop.JSDisconnectedException: JavaScript interop calls cannot be issued at this time. This is because the circuit has disconnected and is being disposed.    at Microsoft.AspNetCore.Components.Server.Circuits.RemoteJSRuntime.BeginInvokeJS(JSInvocationInfo& invocationInfo)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, CancellationToken cancellationToken, Object[] args)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, Object[] args)    at Microsoft.JSInterop.JSRuntimeExtensions.InvokeVoidAsync(IJSRuntime jsRuntime, String identifier, Object[] args)    at Microsoft.JSInterop.Implementation.JSObjectReference.DisposeAsync()    at MudX.MudXProvider.DisposeAsync()    at Microsoft.AspNetCore.Components.RenderTree.Renderer.<>c__DisplayClass105_0.<<Dispose>g__HandleAsyncExceptions|0>d.MoveNext()
2026-10-03T16:42:00.832Z fail: Microsoft.AspNetCore.Components.Server.Circuits.CircuitHost[111] Unhandled exception in circuit 'BGGwARXtj2-cjDHmOrTEMZjyBCKAGB0UkCzXaOKAC_Y'. Microsoft.JSInterop.JSDisconnectedException: JavaScript interop calls cannot be issued at this time. This is because the circuit has disconnected and is being disposed.    at Microsoft.AspNetCore.Components.Server.Circuits.RemoteJSRuntime.BeginInvokeJS(JSInvocationInfo& invocationInfo)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, CancellationToken cancellationToken, Object[] args)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, Object[] args)    at Microsoft.JSInterop.JSRuntimeExtensions.InvokeVoidAsync(IJSRuntime jsRuntime, String identifier, Object[] args)    at Microsoft.JSInterop.Implementation.JSObjectReference.DisposeAsync()    at MudX.MudXProvider.DisposeAsync()    at Microsoft.AspNetCore.Components.RenderTree.Renderer.<>c__DisplayClass105_0.<<Dispose>g__HandleAsyncExceptions|0>d.MoveNext()
2026-10-03T16:42:00.834Z warn: Microsoft.AspNetCore.Components.Server.Circuits.RemoteRenderer[100] Unhandled exception rendering component: JavaScript interop calls cannot be issued at this time. This is because the circuit has disconnected and is being disposed. Microsoft.JSInterop.JSDisconnectedException: JavaScript interop calls cannot be issued at this time. This is because the circuit has disconnected and is being disposed.    at Microsoft.AspNetCore.Components.Server.Circuits.RemoteJSRuntime.BeginInvokeJS(JSInvocationInfo& invocationInfo)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, CancellationToken cancellationToken, Object[] args)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, Object[] args)    at Microsoft.JSInterop.JSRuntimeExtensions.InvokeVoidAsync(IJSRuntime jsRuntime, String identifier, Object[] args)    at Microsoft.JSInterop.Implementation.JSObjectReference.DisposeAsync()    at MudX.MudXProvider.DisposeAsync()    at Microsoft.AspNetCore.Components.RenderTree.Renderer.<>c__DisplayClass105_0.<<Dispose>g__HandleAsyncExceptions|0>d.MoveNext()
2026-10-03T16:42:00.834Z fail: Microsoft.AspNetCore.Components.Server.Circuits.CircuitHost[111] Unhandled exception in circuit 'otNUfmoFep5JabkgVExcDYtbcwSXb0e7ydzBTfN5prk'. Microsoft.JSInterop.JSDisconnectedException: JavaScript interop calls cannot be issued at this time. This is because the circuit has disconnected and is being disposed.    at Microsoft.AspNetCore.Components.Server.Circuits.RemoteJSRuntime.BeginInvokeJS(JSInvocationInfo& invocationInfo)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, CancellationToken cancellationToken, Object[] args)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, Object[] args)    at Microsoft.JSInterop.JSRuntimeExtensions.InvokeVoidAsync(IJSRuntime jsRuntime, String identifier, Object[] args)    at Microsoft.JSInterop.Implementation.JSObjectReference.DisposeAsync()    at MudX.MudXProvider.DisposeAsync()    at Microsoft.AspNetCore.Components.RenderTree.Renderer.<>c__DisplayClass105_0.<<Dispose>g__HandleAsyncExceptions|0>d.MoveNext()
2026-10-03T16:42:04.286Z warn: Microsoft.AspNetCore.Components.Server.Circuits.RemoteRenderer[100] Unhandled exception rendering component: JavaScript interop calls cannot be issued at this time. This is because the circuit has disconnected and is being disposed. Microsoft.JSInterop.JSDisconnectedException: JavaScript interop calls cannot be issued at this time. This is because the circuit has disconnected and is being disposed.    at Microsoft.AspNetCore.Components.Server.Circuits.RemoteJSRuntime.BeginInvokeJS(JSInvocationInfo& invocationInfo)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, CancellationToken cancellationToken, Object[] args)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, Object[] args)    at Microsoft.JSInterop.JSRuntimeExtensions.InvokeVoidAsync(IJSRuntime jsRuntime, String identifier, Object[] args)    at Microsoft.JSInterop.Implementation.JSObjectReference.DisposeAsync()    at MudX.MudXProvider.DisposeAsync()    at Microsoft.AspNetCore.Components.RenderTree.Renderer.<>c__DisplayClass105_0.<<Dispose>g__HandleAsyncExceptions|0>d.MoveNext()
2026-10-03T16:42:04.286Z warn: Microsoft.AspNetCore.Components.Server.Circuits.RemoteRenderer[100] Unhandled exception rendering component: JavaScript interop calls cannot be issued at this time. This is because the circuit has disconnected and is being disposed. Microsoft.JSInterop.JSDisconnectedException: JavaScript interop calls cannot be issued at this time. This is because the circuit has disconnected and is being disposed.    at Microsoft.AspNetCore.Components.Server.Circuits.RemoteJSRuntime.BeginInvokeJS(JSInvocationInfo& invocationInfo)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, CancellationToken cancellationToken, Object[] args)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, Object[] args)    at Microsoft.JSInterop.JSRuntimeExtensions.InvokeVoidAsync(IJSRuntime jsRuntime, String identifier, Object[] args)    at Microsoft.JSInterop.Implementation.JSObjectReference.DisposeAsync()    at MudX.MudXProvider.DisposeAsync()    at Microsoft.AspNetCore.Components.RenderTree.Renderer.<>c__DisplayClass105_0.<<Dispose>g__HandleAsyncExceptions|0>d.MoveNext()
2026-10-03T16:42:04.286Z fail: Microsoft.AspNetCore.Components.Server.Circuits.CircuitHost[111] Unhandled exception in circuit '9pKRoF0UW_Dwf_c400YSe95JEwPd9YIRkUNvDAdFAsQ'. Microsoft.JSInterop.JSDisconnectedException: JavaScript interop calls cannot be issued at this time. This is because the circuit has disconnected and is being disposed.    at Microsoft.AspNetCore.Components.Server.Circuits.RemoteJSRuntime.BeginInvokeJS(JSInvocationInfo& invocationInfo)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, CancellationToken cancellationToken, Object[] args)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, Object[] args)    at Microsoft.JSInterop.JSRuntimeExtensions.InvokeVoidAsync(IJSRuntime jsRuntime, String identifier, Object[] args)    at Microsoft.JSInterop.Implementation.JSObjectReference.DisposeAsync()    at MudX.MudXProvider.DisposeAsync()    at Microsoft.AspNetCore.Components.RenderTree.Renderer.<>c__DisplayClass105_0.<<Dispose>g__HandleAsyncExceptions|0>d.MoveNext()
2026-10-03T16:42:04.286Z fail: Microsoft.AspNetCore.Components.Server.Circuits.CircuitHost[111] Unhandled exception in circuit 'fVsXQsr2emzdsCN6P6qF-7ZWjWEbu9Jqc25sEZnl1uE'. Microsoft.JSInterop.JSDisconnectedException: JavaScript interop calls cannot be issued at this time. This is because the circuit has disconnected and is being disposed.    at Microsoft.AspNetCore.Components.Server.Circuits.RemoteJSRuntime.BeginInvokeJS(JSInvocationInfo& invocationInfo)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, CancellationToken cancellationToken, Object[] args)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, Object[] args)    at Microsoft.JSInterop.JSRuntimeExtensions.InvokeVoidAsync(IJSRuntime jsRuntime, String identifier, Object[] args)    at Microsoft.JSInterop.Implementation.JSObjectReference.DisposeAsync()    at MudX.MudXProvider.DisposeAsync()    at Microsoft.AspNetCore.Components.RenderTree.Renderer.<>c__DisplayClass105_0.<<Dispose>g__HandleAsyncExceptions|0>d.MoveNext()
2026-10-03T16:42:06.735Z warn: Microsoft.AspNetCore.Components.Server.Circuits.RemoteRenderer[100] Unhandled exception rendering component: JavaScript interop calls cannot be issued at this time. This is because the circuit has disconnected and is being disposed. Microsoft.JSInterop.JSDisconnectedException: JavaScript interop calls cannot be issued at this time. This is because the circuit has disconnected and is being disposed.    at Microsoft.AspNetCore.Components.Server.Circuits.RemoteJSRuntime.BeginInvokeJS(JSInvocationInfo& invocationInfo)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, CancellationToken cancellationToken, Object[] args)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, Object[] args)    at Microsoft.JSInterop.JSRuntimeExtensions.InvokeVoidAsync(IJSRuntime jsRuntime, String identifier, Object[] args)    at Microsoft.JSInterop.Implementation.JSObjectReference.DisposeAsync()    at MudX.MudXProvider.DisposeAsync()    at Microsoft.AspNetCore.Components.RenderTree.Renderer.<>c__DisplayClass105_0.<<Dispose>g__HandleAsyncExceptions|0>d.MoveNext()
2026-10-03T16:42:06.735Z fail: Microsoft.AspNetCore.Components.Server.Circuits.CircuitHost[111] Unhandled exception in circuit '9vs5pQ6_atbj2acozeFlRldoKY-Stc9HMM64QzBIGa8'. Microsoft.JSInterop.JSDisconnectedException: JavaScript interop calls cannot be issued at this time. This is because the circuit has disconnected and is being disposed.    at Microsoft.AspNetCore.Components.Server.Circuits.RemoteJSRuntime.BeginInvokeJS(JSInvocationInfo& invocationInfo)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, CancellationToken cancellationToken, Object[] args)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, Object[] args)    at Microsoft.JSInterop.JSRuntimeExtensions.InvokeVoidAsync(IJSRuntime jsRuntime, String identifier, Object[] args)    at Microsoft.JSInterop.Implementation.JSObjectReference.DisposeAsync()    at MudX.MudXProvider.DisposeAsync()    at Microsoft.AspNetCore.Components.RenderTree.Renderer.<>c__DisplayClass105_0.<<Dispose>g__HandleAsyncExceptions|0>d.MoveNext()
2026-10-03T16:42:06.737Z warn: Microsoft.AspNetCore.Components.Server.Circuits.RemoteRenderer[100] Unhandled exception rendering component: JavaScript interop calls cannot be issued at this time. This is because the circuit has disconnected and is being disposed. Microsoft.JSInterop.JSDisconnectedException: JavaScript interop calls cannot be issued at this time. This is because the circuit has disconnected and is being disposed.    at Microsoft.AspNetCore.Components.Server.Circuits.RemoteJSRuntime.BeginInvokeJS(JSInvocationInfo& invocationInfo)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, CancellationToken cancellationToken, Object[] args)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, Object[] args)    at Microsoft.JSInterop.JSRuntimeExtensions.InvokeVoidAsync(IJSRuntime jsRuntime, String identifier, Object[] args)    at Microsoft.JSInterop.Implementation.JSObjectReference.DisposeAsync()    at MudX.MudXProvider.DisposeAsync()    at Microsoft.AspNetCore.Components.RenderTree.Renderer.<>c__DisplayClass105_0.<<Dispose>g__HandleAsyncExceptions|0>d.MoveNext()
2026-10-03T16:42:06.738Z fail: Microsoft.AspNetCore.Components.Server.Circuits.CircuitHost[111] Unhandled exception in circuit 'MALTVAZw3ArILO_UV0mepFUrR5eG58Nx6LkJ_8PBWio'. Microsoft.JSInterop.JSDisconnectedException: JavaScript interop calls cannot be issued at this time. This is because the circuit has disconnected and is being disposed.    at Microsoft.AspNetCore.Components.Server.Circuits.RemoteJSRuntime.BeginInvokeJS(JSInvocationInfo& invocationInfo)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, CancellationToken cancellationToken, Object[] args)    at Microsoft.JSInterop.JSRuntime.InvokeAsync[TValue](Int64 targetInstanceId, String identifier, JSCallType callType, Object[] args)    at Microsoft.JSInterop.JSRuntimeExtensions.InvokeVoidAsync(IJSRuntime jsRuntime, String identifier, Object[] args)    at Microsoft.JSInterop.Implementation.JSObjectReference.DisposeAsync()    at MudX.MudXProvider.DisposeAsync()    at Microsoft.AspNetCore.Components.RenderTree.Renderer.<>c__DisplayClass105_0.<<Dispose>g__HandleAsyncExceptions|0>d.MoveNext()
```

## Acceptance criteria

Report-only until S15 (D50): this table never changes the verdict. A criterion is read from the test titles that carry `[AC-nn]` (a suffix such as `[AC-49a]` counts for AC-49) in the .trx files (dotnet), Playwright's `e2e/results.json` (e2e) and the node TAP of the js job (node); one with no such test is missing. When it has several, failed beats flaky beats partial beats passed. `partial` means a test of the criterion passed and another was skipped, fixme'd or expected to fail: part of the criterion is not verified, so it is never read as `passed`; when every test of it is skipped it is `skipped`.

50 criteria: 48 passed, 0 partial, 2 failed, 0 skipped, 0 flaky, 0 missing.

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
| AC-44 | passed | dotnet 2, e2e 22 (44a, 44b) |
| AC-45 | failed | e2e 32 |
| AC-46 | failed | dotnet 2, e2e 5 (46a) |
| AC-47 | passed | dotnet 14, e2e 10 (47a, 47b) |
| AC-48 | passed | e2e 6 |
| AC-49 | passed | dotnet 23 (49a) |
| AC-50 | passed | dotnet 1, e2e 1 (50a) |

## Docker smoke

- image: realm:smoke
- image size (uncompressed): 253.3 MB, within the target (6.5: target at most 330 MB, warn over 360 MB, fail over 450 MB)
- app layer (published output): 23.4 MB, within the target (6.5: target at most 35 MB, warn over 45 MB)
- compressed size (estimate: gzip of docker save): 110.5 MB, within the target (6.5: target at most 120 MB, warn over 130 MB, fail over 200 MB)
- time to healthy: 0.77 s (item 1: target at most 3 s, warn over 3 s, fail over 10 s)

| item | check | result | detail |
|---|---|---|---|
| 1 | first /healthz 200 | PASS | 0.765 s |
| 2 | base href follows X-Ingress-Path | PASS | '/' without the header, '/api/hassio_ingress/TOKEN/' with it (a 43-character token), an invalid value ignored |
| 3 | content types | PASS | _framework/blazor.web.9hsif5t8mt.js (text/javascript) lib/maplibre-gl/maplibre-gl.mjs (text/javascript) lib/maplibre-gl/maplibre-gl-worker.mjs (text/javascript) js/realmMap.js (text/javascript) fonts/atkinson-hyperlegible-latin-700-normal.woff2 (font/woff2) |
| 4 | Set-Cookie on / (informational) | WARN | GET / sets a cookie: .AspNetCore.Antiforgery.fU4C0xp-sPg (D61: informational; the Blazor antiforgery cookie is expected) |
| 5 | healthcheck mode exits 0 | PASS | dotnet Realm.Web.dll healthcheck exited 0 after 0.172 s |
| 6 | diagnostics.json in Demo | PASS | GET /diagnostics.json answered 200 with "mode": "demo" and "zoneDataOk": true |
| 7 | no unhandled exception in the Demo log | PASS | none among 7 log lines after the start and 6 requests |
| 8 | image size against the 6.5 table | PASS | image 253.3 MB, app layer 23.3 MB, compressed estimate 110.5 MB (not enforced), within the warn lines of 6.5 (image 360.0 MB, app layer 45.0 MB) |
| 9 | image runs as root | PASS | id -u prints 0 |
| 10 | no forbidden path in the image | PASS | 5691 entries checked |
| 11 | Live start, Home Assistant unreachable | PASS | /healthz 200 after 0.750 s, realm.db 94208 bytes and dp-keys in /data, still running 3 s later, no crash among 10 log lines |
| 12 | licence notices in the image | PASS | LICENSE, THIRD-PARTY-NOTICES.md, LICENSES/Apache-2.0.txt and wwwroot/lib/maplibre-gl/LICENSE.txt are in /app; the fonts have an OFL-*.txt beside them |

Items: 11 PASS, 1 WARN, 0 FAIL, 0 SKIP. Sizes are recorded against the table of 03 section 6.5; no item enforces them before item 8 (S16a).

## Screenshots (46)

```text
shots/phone/SC01-location-peek.png  sha256:b44412a42c79
shots/phone/SC02-drivers-peek.png  sha256:b44412a42c79
shots/phone/SC03-drivers-tall.png  sha256:a789211518ea
shots/phone/SC04-vehicles-list.png  sha256:efdb3682a10a
shots/phone/SC05-places-list.png  sha256:ae17b75a0af0
shots/phone/SC06-member-detail.png  sha256:866c5fabe141
shots/phone/SC07-peek-selection.png  sha256:b9fc04d9d53d
shots/phone/SC08-style-popover.png  sha256:9102748d9362
shots/phone/SC09-settings-about.png  sha256:d3716c4e3a6a
shots/phone/SC09-settings.png  sha256:d69f617d2e9f
shots/phone/SC10-driving.png  sha256:d2bade742fdc
shots/phone/SC11-popup-drives-miles.png  sha256:9994eeb421a6
shots/phone/SC11-popup-drives.png  sha256:fbca3eb87c2c
shots/phone/SC12-popup-speeding.png  sha256:495ea8f07bfc
shots/phone/SC13-driver-week.png  sha256:18efc0e9eb15
shots/phone/SC14-variant-phone-unavailable.png  sha256:cf689be586b6
shots/phone/SC15-variant-poor-accuracy.png  sha256:999050d7b113
shots/phone/SC17-variant-fresh-install-partial.png  sha256:b9b9f5cc4b95
shots/phone/SC17-variant-fresh-install.png  sha256:f8b8b59059c1
shots/unfolded/SC01-location-peek.png  sha256:23f20e00aa31
shots/unfolded/SC02-drivers-peek.png  sha256:23f20e00aa31
shots/unfolded/SC04-vehicles-list.png  sha256:f8565e7a8f80
shots/unfolded/SC05-places-list.png  sha256:c5ff6b7ed1a8
shots/unfolded/SC06-member-detail.png  sha256:9ed074f9ae11
shots/unfolded/SC08-style-popover.png  sha256:a822011aec30
shots/unfolded/SC09-settings-about.png  sha256:53edaa2d5597
shots/unfolded/SC09-settings.png  sha256:f7a6859747ac
shots/unfolded/SC10-driving.png  sha256:3cd1dfa0abcc
shots/unfolded/SC11-popup-drives-miles.png  sha256:1f263e1ac4d9
shots/unfolded/SC11-popup-drives.png  sha256:fdadaf6771e4
shots/unfolded/SC12-popup-speeding.png  sha256:957df269efa8
shots/unfolded/SC13-driver-week.png  sha256:5304e80ea047
shots/unfolded/SC14-variant-phone-unavailable.png  sha256:b9aff7dad91d
shots/unfolded/SC15-variant-poor-accuracy.png  sha256:63c8913936b2
shots/unfolded/SC17-variant-fresh-install-partial.png  sha256:6aeb1e3f370f
shots/unfolded/SC17-variant-fresh-install.png  sha256:6c6669ec98ae
failures/ac-45.phone.png  sha256:b44412a42c79
failures/ac-45.phone-2.png  sha256:b9fc04d9d53d
failures/ac-45.phone-3.png  sha256:25f634bb0009
failures/ac-45.phone-4.png  sha256:39d245535d7f
failures/ac-45.phone-5.png  sha256:a789211518ea
failures/ac-45.phone-6.png  sha256:efdb3682a10a
failures/ac-45.phone-7.png  sha256:ae17b75a0af0
failures/ac-45.phone-8.png  sha256:4819bff22882
failures/ac-45.phone-9.png  sha256:1b4c97f020e3
failures/ac-46.phone.png  sha256:b44412a42c79
```
