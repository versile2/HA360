# CI summary: SUCCESS

- result: success
- branch: slice/S12-sqlite-writer
- sha: ba32f8ce77d003336a02537532e3f0f0396af260
- run: 30
- url: https://github.com/versile2/HA360/actions/runs/36928834732

## Jobs

| job | result |
|---|---|
| guards | success |
| dotnet | success |
| docker-smoke | success |

## Guards

```text
REPORT ac-coverage: 50 AC ids missing (AC-01 … AC-50)
```

PASS: 11 of 12 guards.

## Compiler errors

None.

## Tests

1006 passed, 0 failed, 0 skipped (4 .trx files).

## Docker smoke

- image: realm:smoke
- image size (uncompressed): 282.5 MB, within the target (6.5: target at most 330 MB, warn over 360 MB, fail over 450 MB)
- app layer (published output): 52.6 MB, over the warn line (6.5: target at most 35 MB, warn over 45 MB)
- compressed size (estimate: gzip of docker save): 126.2 MB, above the target (6.5: target at most 120 MB, warn over 130 MB, fail over 200 MB)
- time to healthy: 0.57 s (item 1: target at most 3 s, warn over 3 s, fail over 10 s)

| item | check | result | detail |
|---|---|---|---|
| 1 | first /healthz 200 | PASS | 0.571 s |
| 2 | base href follows X-Ingress-Path | PASS | '/' without the header, '/api/hassio_ingress/TOKEN/' with it (a 43-character token), an invalid value ignored |
| 3 | content types | PASS | _framework/blazor.web.9hsif5t8mt.js (text/javascript) |
| 4 | Set-Cookie on / (informational) | WARN | GET / sets a cookie: .AspNetCore.Antiforgery.VyLW6ORzMgk (D61: informational; the Blazor antiforgery cookie is expected) |
| 9 | image runs as root | PASS | id -u prints 0 |
| 10 | no forbidden path in the image | PASS | 5687 entries checked |

Items: 5 PASS, 1 WARN, 0 FAIL, 0 SKIP. Sizes are recorded against the table of 03 section 6.5; no item enforces them before item 8 (S16a).
