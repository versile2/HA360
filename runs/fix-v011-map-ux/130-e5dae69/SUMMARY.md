# CI summary: SUCCESS

- result: success
- branch: fix/v011-map-ux
- sha: e5dae69f1ce8e82b2ca69ff422340d8d89dd6e6a
- run: 130
- url: https://github.com/versile2/ha-cartographer/actions/runs/37697622262

## Jobs

| job | result |
|---|---|
| guards | success |
| dotnet | success |
| js | success |
| docker-smoke | success |
| e2e | success |

## Guards

PASS: 12 of 12 guards.

## Compiler errors

None.

## Tests

2779 passed, 0 failed, 0 skipped (5 .trx files).

## JS

Node tests (node --test of tests/js): 468 passed, 0 failed.

Type check (tsc.log): no "error TS" line.

## Map styles

PASS: 5 (satellite, demo-offline, night, day, streets).

## E2E

186 passed, 0 failed, 0 flaky, 0 skipped (186 test runs in 4 projects).

Payload contract (node --test of tests/contract): 30 passed, 0 failed.

## Acceptance criteria

Since S15 (D50) the guards enforce all 50 ids (ac-coverage) and a failed test fails the run; this table itself never changes the verdict, and a flaky criterion is listed, not failed (04 section 1.6 blocks the merge at two flaky runs in three, by hand). A criterion is read from the test titles that carry `[AC-nn]` (a suffix such as `[AC-49a]` counts for AC-49) in the .trx files (dotnet), Playwright's `e2e/results.json` (e2e) and the node TAP of the js job (node); one with no such test is missing. When it has several, failed beats flaky beats partial beats passed. `partial` means a test of the criterion passed and another was skipped, fixme'd or expected to fail: part of the criterion is not verified, so it is never read as `passed`; when every test of it is skipped it is `skipped`.

50 criteria: 50 passed, 0 partial, 0 failed, 0 skipped, 0 flaky, 0 missing.

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
| AC-45 | passed | e2e 32 |
| AC-46 | passed | dotnet 2, e2e 5 (46a) |
| AC-47 | passed | dotnet 14, e2e 10 (47a, 47b) |
| AC-48 | passed | e2e 6 |
| AC-49 | passed | dotnet 26, e2e 4 (49a, 49b) |
| AC-50 | passed | dotnet 1, e2e 1 (50a) |

## Docker smoke

- image: realm:smoke
- image size (uncompressed): 253.3 MB, within the target (6.5: target at most 330 MB, warn over 360 MB, fail over 450 MB)
- app layer (published output): 23.4 MB, within the target (6.5: target at most 35 MB, warn over 45 MB)
- compressed size (estimate: gzip of docker save): 110.5 MB, within the target (6.5: target at most 120 MB, warn over 130 MB, fail over 200 MB)
- time to healthy: 0.84 s (item 1: target at most 3 s, warn over 3 s, fail over 10 s)

| item | check | result | detail |
|---|---|---|---|
| 1 | first /healthz 200 | PASS | 0.844 s |
| 2 | base href follows X-Ingress-Path | PASS | '/' without the header, '/api/hassio_ingress/TOKEN/' with it (a 43-character token), an invalid value ignored |
| 3 | content types | PASS | _framework/blazor.web.9hsif5t8mt.js (text/javascript) lib/maplibre-gl/maplibre-gl.mjs (text/javascript) lib/maplibre-gl/maplibre-gl-worker.mjs (text/javascript) js/realmMap.js (text/javascript) fonts/atkinson-hyperlegible-latin-700-normal.woff2 (font/woff2) |
| 4 | Set-Cookie on / (informational) | WARN | GET / sets a cookie: .AspNetCore.Antiforgery.fU4C0xp-sPg (D61: informational; the Blazor antiforgery cookie is expected) |
| 5 | healthcheck mode exits 0 | PASS | dotnet Realm.Web.dll healthcheck exited 0 after 0.137 s |
| 6 | diagnostics.json in Demo | PASS | GET /diagnostics.json answered 200 with "mode": "demo" and "zoneDataOk": true |
| 7 | no unhandled exception in the Demo log | PASS | none among 7 log lines after the start and 6 requests |
| 8 | image size against the 6.5 table | PASS | image 253.3 MB, app layer 23.3 MB, compressed estimate 110.5 MB (not enforced), within the warn lines of 6.5 (image 360.0 MB, app layer 45.0 MB) |
| 9 | image runs as root | PASS | id -u prints 0 |
| 10 | no forbidden path in the image | PASS | 5691 entries checked |
| 11 | Live start, Home Assistant unreachable | PASS | /healthz 200 after 0.854 s, realm.db 94208 bytes and dp-keys in /data, still running 3 s later, no crash among 10 log lines |
| 12 | licence notices in the image | PASS | LICENSE, THIRD-PARTY-NOTICES.md, LICENSES/Apache-2.0.txt and wwwroot/lib/maplibre-gl/LICENSE.txt are in /app; the fonts have an OFL-*.txt beside them |

Items: 11 PASS, 1 WARN, 0 FAIL, 0 SKIP. Sizes are recorded against the table of 03 section 6.5; no item enforces them before item 8 (S16a).

## Screenshots (38)

```text
shots/phone/SC01-location-peek.png  sha256:cb63b19e0601
shots/phone/SC02-drivers-peek.png  sha256:cb63b19e0601
shots/phone/SC03-drivers-tall.png  sha256:79823ca8ab7f
shots/phone/SC04-vehicles-list.png  sha256:5af10a719621
shots/phone/SC05-places-list.png  sha256:20f198f71da5
shots/phone/SC06-member-detail.png  sha256:e8190d3d6050
shots/phone/SC07-peek-selection.png  sha256:423ef01b3eec
shots/phone/SC08-style-popover.png  sha256:c436d0a59cc2
shots/phone/SC09-settings-about.png  sha256:72ef52bcb04d
shots/phone/SC09-settings.png  sha256:d69f617d2e9f
shots/phone/SC10-driving.png  sha256:d2bade742fdc
shots/phone/SC11-popup-drives-miles.png  sha256:9994eeb421a6
shots/phone/SC11-popup-drives.png  sha256:fbca3eb87c2c
shots/phone/SC12-popup-speeding.png  sha256:495ea8f07bfc
shots/phone/SC13-driver-week.png  sha256:18efc0e9eb15
shots/phone/SC14-variant-phone-unavailable.png  sha256:cf689be586b6
shots/phone/SC15-variant-poor-accuracy.png  sha256:b48733f766f2
shots/phone/SC16-variant-ha-down.png  sha256:6ce119cc4d7a
shots/phone/SC17-variant-fresh-install-partial.png  sha256:b9b9f5cc4b95
shots/phone/SC17-variant-fresh-install.png  sha256:fde71d560054
shots/unfolded/SC01-location-peek.png  sha256:23f20e00aa31
shots/unfolded/SC02-drivers-peek.png  sha256:23f20e00aa31
shots/unfolded/SC04-vehicles-list.png  sha256:f8565e7a8f80
shots/unfolded/SC05-places-list.png  sha256:c5ff6b7ed1a8
shots/unfolded/SC06-member-detail.png  sha256:9ed074f9ae11
shots/unfolded/SC08-style-popover.png  sha256:54da7607a9cf
shots/unfolded/SC09-settings-about.png  sha256:a8f355516a91
shots/unfolded/SC09-settings.png  sha256:4f881a5fd123
shots/unfolded/SC10-driving.png  sha256:3cd1dfa0abcc
shots/unfolded/SC11-popup-drives-miles.png  sha256:1f263e1ac4d9
shots/unfolded/SC11-popup-drives.png  sha256:fdadaf6771e4
shots/unfolded/SC12-popup-speeding.png  sha256:957df269efa8
shots/unfolded/SC13-driver-week.png  sha256:5304e80ea047
shots/unfolded/SC14-variant-phone-unavailable.png  sha256:b9aff7dad91d
shots/unfolded/SC15-variant-poor-accuracy.png  sha256:63c8913936b2
shots/unfolded/SC16-variant-ha-down.png  sha256:48cd1c0e6525
shots/unfolded/SC17-variant-fresh-install-partial.png  sha256:6aeb1e3f370f
shots/unfolded/SC17-variant-fresh-install.png  sha256:6c6669ec98ae
```
