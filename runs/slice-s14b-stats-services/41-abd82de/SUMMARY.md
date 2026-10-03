# CI summary: SUCCESS

- result: success
- branch: slice/S14b-stats-services
- sha: abd82de44bd21e9ee408d4c528d56e627891f566
- run: 41
- url: https://github.com/versile2/HA360/actions/runs/36977064540

## Jobs

| job | result |
|---|---|
| guards | success |
| dotnet | success |
| js | success |
| docker-smoke | success |

## Guards

```text
REPORT ac-coverage: 49 AC ids missing (AC-01 … AC-48, AC-50)
```

PASS: 11 of 12 guards.

## Compiler errors

None.

## Tests

1477 passed, 0 failed, 0 skipped (5 .trx files).

## Docker smoke

- image: realm:smoke
- image size (uncompressed): 284.9 MB, within the target (6.5: target at most 330 MB, warn over 360 MB, fail over 450 MB)
- app layer (published output): 55.0 MB, over the warn line (6.5: target at most 35 MB, warn over 45 MB)
- compressed size (estimate: gzip of docker save): 127.5 MB, above the target (6.5: target at most 120 MB, warn over 130 MB, fail over 200 MB)
- time to healthy: 0.64 s (item 1: target at most 3 s, warn over 3 s, fail over 10 s)

| item | check | result | detail |
|---|---|---|---|
| 1 | first /healthz 200 | PASS | 0.638 s |
| 2 | base href follows X-Ingress-Path | PASS | '/' without the header, '/api/hassio_ingress/TOKEN/' with it (a 43-character token), an invalid value ignored |
| 3 | content types | PASS | _framework/blazor.web.9hsif5t8mt.js (text/javascript) lib/maplibre-gl/maplibre-gl.mjs (text/javascript) lib/maplibre-gl/maplibre-gl-worker.mjs (text/javascript) js/realmMap.js (text/javascript) |
| 4 | Set-Cookie on / (informational) | WARN | GET / sets a cookie: .AspNetCore.Antiforgery.fU4C0xp-sPg (D61: informational; the Blazor antiforgery cookie is expected) |
| 9 | image runs as root | PASS | id -u prints 0 |
| 10 | no forbidden path in the image | PASS | 5727 entries checked |
| 11 | Live start, Home Assistant unreachable | PASS | /healthz 200 after 0.753 s, realm.db 94208 bytes and dp-keys in /data, still running 3 s later, no crash among 9 log lines |

Items: 6 PASS, 1 WARN, 0 FAIL, 0 SKIP. Sizes are recorded against the table of 03 section 6.5; no item enforces them before item 8 (S16a).
