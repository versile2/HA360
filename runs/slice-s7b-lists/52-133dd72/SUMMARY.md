# CI summary: SUCCESS

- result: success
- branch: slice/S7b-lists
- sha: 133dd72e54f2559b2c7f4f633520168fa6dc30a5
- run: 52
- url: https://github.com/versile2/HA360/actions/runs/36992366805

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
REPORT ac-coverage: 25 AC ids missing (AC-12, AC-15, AC-20 … AC-24, AC-30 … AC-45, AC-47, AC-48)
```

PASS: 11 of 12 guards.

## Compiler errors

None.

## Tests

1796 passed, 0 failed, 0 skipped (5 .trx files).

## E2E

57 passed, 0 failed, 0 flaky, 0 skipped (57 test runs in 4 projects).

Payload contract (node --test of tests/contract): 30 passed, 0 failed.

## Acceptance criteria

Report-only until S15 (D50): this table never changes the verdict. A criterion is read from the test titles that carry `[AC-nn]` (a suffix such as `[AC-49a]` counts for AC-49) in the .trx files (dotnet), Playwright's `e2e/results.json` (e2e) and the node TAP of the js job (node); one with no such test is missing. When it has several, failed beats flaky beats passed beats skipped.

50 criteria: 25 passed, 0 failed, 0 skipped, 0 flaky, 25 missing.

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
| AC-46 | passed | dotnet 2, e2e 1 (46a) |
| AC-47 | missing | — |
| AC-48 | missing | — |
| AC-49 | passed | dotnet 23 (49a) |
| AC-50 | passed | dotnet 1, e2e 1 (50a) |

## Docker smoke

- image: realm:smoke
- image size (uncompressed): 285.1 MB, within the target (6.5: target at most 330 MB, warn over 360 MB, fail over 450 MB)
- app layer (published output): 55.2 MB, over the warn line (6.5: target at most 35 MB, warn over 45 MB)
- compressed size (estimate: gzip of docker save): 127.6 MB, above the target (6.5: target at most 120 MB, warn over 130 MB, fail over 200 MB)
- time to healthy: 0.56 s (item 1: target at most 3 s, warn over 3 s, fail over 10 s)

| item | check | result | detail |
|---|---|---|---|
| 1 | first /healthz 200 | PASS | 0.557 s |
| 2 | base href follows X-Ingress-Path | PASS | '/' without the header, '/api/hassio_ingress/TOKEN/' with it (a 43-character token), an invalid value ignored |
| 3 | content types | PASS | _framework/blazor.web.9hsif5t8mt.js (text/javascript) lib/maplibre-gl/maplibre-gl.mjs (text/javascript) lib/maplibre-gl/maplibre-gl-worker.mjs (text/javascript) js/realmMap.js (text/javascript) |
| 4 | Set-Cookie on / (informational) | WARN | GET / sets a cookie: .AspNetCore.Antiforgery.fU4C0xp-sPg (D61: informational; the Blazor antiforgery cookie is expected) |
| 9 | image runs as root | PASS | id -u prints 0 |
| 10 | no forbidden path in the image | PASS | 5739 entries checked |
| 11 | Live start, Home Assistant unreachable | PASS | /healthz 200 after 0.637 s, realm.db 94208 bytes and dp-keys in /data, still running 3 s later, no crash among 9 log lines |

Items: 6 PASS, 1 WARN, 0 FAIL, 0 SKIP. Sizes are recorded against the table of 03 section 6.5; no item enforces them before item 8 (S16a).

## Screenshots (9)

```text
shots/phone/SC01-location-peek.png  sha256:7e5bc4297add
shots/phone/SC02-drivers-peek.png  sha256:7e5bc4297add
shots/phone/SC03-drivers-tall.png  sha256:8cc35ab8fed2
shots/phone/SC04-vehicles-list.png  sha256:9ce7b912fbbc
shots/phone/SC05-places-list.png  sha256:049d9b295839
shots/unfolded/SC01-location-peek.png  sha256:31e084bd611e
shots/unfolded/SC02-drivers-peek.png  sha256:31e084bd611e
shots/unfolded/SC04-vehicles-list.png  sha256:5fba6bed4883
shots/unfolded/SC05-places-list.png  sha256:740afee22a99
```
