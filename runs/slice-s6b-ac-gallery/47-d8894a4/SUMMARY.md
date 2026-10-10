# CI summary: SUCCESS

- result: success
- branch: slice/S6b-ac-gallery
- sha: d8894a4ea7166b75cca3d3dd2b81e476aa512b88
- run: 47
- url: https://github.com/versile2/HA360/actions/runs/36985981097

## Jobs

| job | result |
|---|---|
| guards | success |
| dotnet | success |
| js | success |
| docker-smoke | success |
| e2e | success |

## Guards

```text
REPORT ac-coverage: 43 AC ids missing (AC-02, AC-04 … AC-12, AC-14, AC-15, AC-19 … AC-48, AC-50)
```

PASS: 11 of 12 guards.

## Compiler errors

None.

## Tests

1477 passed, 0 failed, 0 skipped (5 .trx files).

## E2E

14 passed, 0 failed, 0 flaky, 0 skipped (14 test runs in 2 projects).

Payload contract (node --test of tests/contract): 30 passed, 0 failed.

## Acceptance criteria

Report-only until S15 (D50): this table never changes the verdict. A criterion is read from the test titles that carry `[AC-nn]` (a suffix such as `[AC-49a]` counts for AC-49) in the .trx files (dotnet), Playwright's `e2e/results.json` (e2e) and the node TAP of the js job (node); one with no such test is missing. When it has several, failed beats flaky beats passed beats skipped.

50 criteria: 7 passed, 0 failed, 0 skipped, 0 flaky, 43 missing.

| AC | status | found in |
|---|---|---|
| AC-01 | passed | e2e 1 |
| AC-02 | missing | — |
| AC-03 | passed | e2e 1 |
| AC-04 | missing | — |
| AC-05 | missing | — |
| AC-06 | missing | — |
| AC-07 | missing | — |
| AC-08 | missing | — |
| AC-09 | missing | — |
| AC-10 | missing | — |
| AC-11 | missing | — |
| AC-12 | missing | — |
| AC-13 | passed | e2e 1 (13a) |
| AC-14 | missing | — |
| AC-15 | missing | — |
| AC-16 | passed | e2e 1 (16a) |
| AC-17 | passed | e2e 1 |
| AC-18 | passed | e2e 1 |
| AC-19 | missing | — |
| AC-20 | missing | — |
| AC-21 | missing | — |
| AC-22 | missing | — |
| AC-23 | missing | — |
| AC-24 | missing | — |
| AC-25 | missing | — |
| AC-26 | missing | — |
| AC-27 | missing | — |
| AC-28 | missing | — |
| AC-29 | missing | — |
| AC-30 | missing | — |
| AC-31 | missing | — |
| AC-32 | missing | — |
| AC-33 | missing | — |
| AC-34 | missing | — |
| AC-35 | missing | — |
| AC-36 | missing | — |
| AC-37 | missing | — |
| AC-38 | missing | — |
| AC-39 | missing | — |
| AC-40 | missing | — |
| AC-41 | missing | — |
| AC-42 | missing | — |
| AC-43 | missing | — |
| AC-44 | missing | — |
| AC-45 | missing | — |
| AC-46 | missing | — |
| AC-47 | missing | — |
| AC-48 | missing | — |
| AC-49 | passed | dotnet 23 (49a) |
| AC-50 | missing | — |

## Docker smoke

- image: realm:smoke
- image size (uncompressed): 284.9 MB, within the target (6.5: target at most 330 MB, warn over 360 MB, fail over 450 MB)
- app layer (published output): 55.0 MB, over the warn line (6.5: target at most 35 MB, warn over 45 MB)
- compressed size (estimate: gzip of docker save): 127.5 MB, above the target (6.5: target at most 120 MB, warn over 130 MB, fail over 200 MB)
- time to healthy: 0.59 s (item 1: target at most 3 s, warn over 3 s, fail over 10 s)

| item | check | result | detail |
|---|---|---|---|
| 1 | first /healthz 200 | PASS | 0.589 s |
| 2 | base href follows X-Ingress-Path | PASS | '/' without the header, '/api/hassio_ingress/TOKEN/' with it (a 43-character token), an invalid value ignored |
| 3 | content types | PASS | _framework/blazor.web.9hsif5t8mt.js (text/javascript) lib/maplibre-gl/maplibre-gl.mjs (text/javascript) lib/maplibre-gl/maplibre-gl-worker.mjs (text/javascript) js/realmMap.js (text/javascript) |
| 4 | Set-Cookie on / (informational) | WARN | GET / sets a cookie: .AspNetCore.Antiforgery.fU4C0xp-sPg (D61: informational; the Blazor antiforgery cookie is expected) |
| 9 | image runs as root | PASS | id -u prints 0 |
| 10 | no forbidden path in the image | PASS | 5727 entries checked |
| 11 | Live start, Home Assistant unreachable | PASS | /healthz 200 after 0.695 s, realm.db 94208 bytes and dp-keys in /data, still running 3 s later, no crash among 10 log lines |

Items: 6 PASS, 1 WARN, 0 FAIL, 0 SKIP. Sizes are recorded against the table of 03 section 6.5; no item enforces them before item 8 (S16a).

## Screenshots (2)

```text
shots/phone/SC01-location-peek.png  sha256:7add6a55dbf5
shots/unfolded/SC01-location-peek.png  sha256:e3cc685bdf53
```
