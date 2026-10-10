# CI summary: SUCCESS

- result: success
- branch: fix/demo-relocate
- sha: 0dc771acbdb04006a1b163ceb8a5aa3395802cbc
- run: 72
- url: https://github.com/versile2/HA360/actions/runs/37020719176

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
REPORT ac-coverage: 12 AC ids missing (AC-12, AC-15, AC-20 … AC-24, AC-30, AC-31, AC-45, AC-47, AC-48)
```

PASS: 11 of 12 guards.

## Compiler errors

None.

## Tests

2204 passed, 0 failed, 0 skipped (5 .trx files).

## E2E

103 passed, 0 failed, 0 flaky, 1 skipped (104 test runs in 4 projects).

Payload contract (node --test of tests/contract): 30 passed, 0 failed.

## Acceptance criteria

Report-only until S15 (D50): this table never changes the verdict. A criterion is read from the test titles that carry `[AC-nn]` (a suffix such as `[AC-49a]` counts for AC-49) in the .trx files (dotnet), Playwright's `e2e/results.json` (e2e) and the node TAP of the js job (node); one with no such test is missing. When it has several, failed beats flaky beats passed beats skipped.

50 criteria: 38 passed, 0 failed, 0 skipped, 0 flaky, 12 missing.

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
| AC-12 | missing | — |
| AC-13 | passed | e2e 1 (13a) |
| AC-14 | passed | node 2 |
| AC-15 | missing | — |
| AC-16 | passed | e2e 1, node 3 (16a) |
| AC-17 | passed | e2e 1 |
| AC-18 | passed | e2e 1 |
| AC-19 | passed | node 31 (19a, 19b, 19c, 19d) |
| AC-20 | missing | — |
| AC-21 | missing | — |
| AC-22 | missing | — |
| AC-23 | missing | — |
| AC-24 | missing | — |
| AC-25 | passed | dotnet 4, e2e 3 (25a) |
| AC-26 | passed | dotnet 2, e2e 2 |
| AC-27 | passed | dotnet 2, e2e 2 |
| AC-28 | passed | dotnet 2, e2e 4 (28a) |
| AC-29 | passed | dotnet 2, e2e 2 |
| AC-30 | missing | — |
| AC-31 | missing | — |
| AC-32 | passed | e2e 1 |
| AC-33 | passed | e2e 2 |
| AC-34 | passed | e2e 3 |
| AC-35 | passed | e2e 2 |
| AC-36 | passed | e2e 1 |
| AC-37 | passed | dotnet 3, e2e 3 |
| AC-38 | passed | dotnet 6, e2e 5 |
| AC-39 | passed | dotnet 2, e2e 5 |
| AC-40 | passed | dotnet 1, e2e 1 |
| AC-41 | passed | dotnet 2, e2e 3 |
| AC-42 | passed | e2e 2 |
| AC-43 | passed | e2e 4 |
| AC-44 | passed | dotnet 1 (44a) |
| AC-45 | missing | — |
| AC-46 | passed | dotnet 2, e2e 1 (46a) |
| AC-47 | missing | — |
| AC-48 | missing | — |
| AC-49 | passed | dotnet 23 (49a) |
| AC-50 | passed | dotnet 1, e2e 1 (50a) |

## Docker smoke

- image: realm:smoke
- image size (uncompressed): 285.5 MB, within the target (6.5: target at most 330 MB, warn over 360 MB, fail over 450 MB)
- app layer (published output): 55.5 MB, over the warn line (6.5: target at most 35 MB, warn over 45 MB)
- compressed size (estimate: gzip of docker save): 127.7 MB, above the target (6.5: target at most 120 MB, warn over 130 MB, fail over 200 MB)
- time to healthy: 0.75 s (item 1: target at most 3 s, warn over 3 s, fail over 10 s)

| item | check | result | detail |
|---|---|---|---|
| 1 | first /healthz 200 | PASS | 0.750 s |
| 2 | base href follows X-Ingress-Path | PASS | '/' without the header, '/api/hassio_ingress/TOKEN/' with it (a 43-character token), an invalid value ignored |
| 3 | content types | PASS | _framework/blazor.web.9hsif5t8mt.js (text/javascript) lib/maplibre-gl/maplibre-gl.mjs (text/javascript) lib/maplibre-gl/maplibre-gl-worker.mjs (text/javascript) js/realmMap.js (text/javascript) fonts/atkinson-hyperlegible-latin-700-normal.woff2 (font/woff2) |
| 4 | Set-Cookie on / (informational) | WARN | GET / sets a cookie: .AspNetCore.Antiforgery.fU4C0xp-sPg (D61: informational; the Blazor antiforgery cookie is expected) |
| 9 | image runs as root | PASS | id -u prints 0 |
| 10 | no forbidden path in the image | PASS | 5759 entries checked |
| 11 | Live start, Home Assistant unreachable | PASS | /healthz 200 after 0.718 s, realm.db 94208 bytes and dp-keys in /data, still running 3 s later, no crash among 10 log lines |
| 12 | licence notices in the image | PASS | LICENSE, THIRD-PARTY-NOTICES.md, LICENSES/Apache-2.0.txt and wwwroot/lib/maplibre-gl/LICENSE.txt are in /app; the fonts have an OFL-*.txt beside them |

Items: 7 PASS, 1 WARN, 0 FAIL, 0 SKIP. Sizes are recorded against the table of 03 section 6.5; no item enforces them before item 8 (S16a).

## Screenshots (25)

```text
shots/phone/SC01-location-peek.png  sha256:9def37bb4579
shots/phone/SC02-drivers-peek.png  sha256:9def37bb4579
shots/phone/SC03-drivers-tall.png  sha256:a3527a18390e
shots/phone/SC04-vehicles-list.png  sha256:671c61a44829
shots/phone/SC05-places-list.png  sha256:fb409405faac
shots/phone/SC10-driving.png  sha256:daa64b11fad9
shots/phone/SC11-popup-drives-miles.png  sha256:d67790601c11
shots/phone/SC11-popup-drives.png  sha256:c8143f3a18ab
shots/phone/SC12-popup-speeding.png  sha256:619d118477ed
shots/phone/SC13-driver-week.png  sha256:f6ba2533df45
shots/phone/SC14-variant-phone-unavailable.png  sha256:378296b758f5
shots/phone/SC17-variant-fresh-install-partial.png  sha256:723591ffbafb
shots/phone/SC17-variant-fresh-install.png  sha256:e60f947a5082
shots/unfolded/SC01-location-peek.png  sha256:c477db00464a
shots/unfolded/SC02-drivers-peek.png  sha256:c477db00464a
shots/unfolded/SC04-vehicles-list.png  sha256:0eb37c06d1cb
shots/unfolded/SC05-places-list.png  sha256:dbdfae756c3c
shots/unfolded/SC10-driving.png  sha256:3b776f15cd46
shots/unfolded/SC11-popup-drives-miles.png  sha256:ae943fa5af6b
shots/unfolded/SC11-popup-drives.png  sha256:bad8d3960846
shots/unfolded/SC12-popup-speeding.png  sha256:7b96d2115677
shots/unfolded/SC13-driver-week.png  sha256:9d3e2753d8a7
shots/unfolded/SC14-variant-phone-unavailable.png  sha256:1f28d73bdaca
shots/unfolded/SC17-variant-fresh-install-partial.png  sha256:ef18bee4f00c
shots/unfolded/SC17-variant-fresh-install.png  sha256:bb7098ab8def
```
