# CI summary: SUCCESS

- result: success
- branch: main
- sha: f3e8ee6123860bd1e361a966c416204587cb653f
- run: 102
- url: https://github.com/versile2/HA360/actions/runs/37069687846

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
REPORT ac-coverage: 2 AC ids missing (AC-45, AC-48)
```

PASS: 11 of 12 guards.

## Compiler errors

None.

## Tests

2778 passed, 0 failed, 0 skipped (5 .trx files).

## JS

Node tests (node --test of tests/js): 462 passed, 0 failed.

Type check (tsc.log): no "error TS" line.

## Map styles

PASS: 5 (satellite, demo-offline, night, day, streets).

## E2E

145 passed, 0 failed, 0 flaky, 1 skipped (146 test runs in 4 projects).

Payload contract (node --test of tests/contract): 30 passed, 0 failed.

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
| AC-49 | passed | dotnet 23 (49a) |
| AC-50 | passed | dotnet 1, e2e 1 (50a) |

## Docker smoke

- image: realm:smoke
- image size (uncompressed): 253.3 MB, within the target (6.5: target at most 330 MB, warn over 360 MB, fail over 450 MB)
- app layer (published output): 23.4 MB, within the target (6.5: target at most 35 MB, warn over 45 MB)
- compressed size (estimate: gzip of docker save): 110.5 MB, within the target (6.5: target at most 120 MB, warn over 130 MB, fail over 200 MB)
- time to healthy: 0.64 s (item 1: target at most 3 s, warn over 3 s, fail over 10 s)

| item | check | result | detail |
|---|---|---|---|
| 1 | first /healthz 200 | PASS | 0.643 s |
| 2 | base href follows X-Ingress-Path | PASS | '/' without the header, '/api/hassio_ingress/TOKEN/' with it (a 43-character token), an invalid value ignored |
| 3 | content types | PASS | _framework/blazor.web.9hsif5t8mt.js (text/javascript) lib/maplibre-gl/maplibre-gl.mjs (text/javascript) lib/maplibre-gl/maplibre-gl-worker.mjs (text/javascript) js/realmMap.js (text/javascript) fonts/atkinson-hyperlegible-latin-700-normal.woff2 (font/woff2) |
| 4 | Set-Cookie on / (informational) | WARN | GET / sets a cookie: .AspNetCore.Antiforgery.fU4C0xp-sPg (D61: informational; the Blazor antiforgery cookie is expected) |
| 5 | healthcheck mode exits 0 | PASS | dotnet Realm.Web.dll healthcheck exited 0 after 0.120 s |
| 6 | diagnostics.json in Demo | PASS | GET /diagnostics.json answered 200 with "mode": "demo" and "zoneDataOk": true |
| 7 | no unhandled exception in the Demo log | PASS | none among 7 log lines after the start and 6 requests |
| 8 | image size against the 6.5 table | PASS | image 253.2 MB, app layer 23.3 MB, compressed estimate 110.4 MB (not enforced), within the warn lines of 6.5 (image 360.0 MB, app layer 45.0 MB) |
| 9 | image runs as root | PASS | id -u prints 0 |
| 10 | no forbidden path in the image | PASS | 5691 entries checked |
| 11 | Live start, Home Assistant unreachable | PASS | /healthz 200 after 0.506 s, realm.db 94208 bytes and dp-keys in /data, still running 3 s later, no crash among 9 log lines |
| 12 | licence notices in the image | PASS | LICENSE, THIRD-PARTY-NOTICES.md, LICENSES/Apache-2.0.txt and wwwroot/lib/maplibre-gl/LICENSE.txt are in /app; the fonts have an OFL-*.txt beside them |

Items: 11 PASS, 1 WARN, 0 FAIL, 0 SKIP. Sizes are recorded against the table of 03 section 6.5; no item enforces them before item 8 (S16a).

## Screenshots (36)

```text
shots/phone/SC01-location-peek.png  sha256:f6dd8a4cd84a
shots/phone/SC02-drivers-peek.png  sha256:f6dd8a4cd84a
shots/phone/SC03-drivers-tall.png  sha256:b1f3e05f2e65
shots/phone/SC04-vehicles-list.png  sha256:ffb0600821eb
shots/phone/SC05-places-list.png  sha256:bf5397a05b1b
shots/phone/SC06-member-detail.png  sha256:e85d68ac52aa
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
shots/unfolded/SC17-variant-fresh-install-partial.png  sha256:ef18bee4f00c
shots/unfolded/SC17-variant-fresh-install.png  sha256:bb7098ab8def
```
