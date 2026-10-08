# tools/ci

The CI feedback loop: push, wait, read one short markdown file over `git`, fix, push.
Nobody reads a console, so every run, pass or fail, ends in a readable summary on the orphan branch `ci-artifacts`.

| File | Role |
|---|---|
| `make-summary.mjs` | Reads the downloaded job artifacts and writes `SUMMARY.md`, `errors.log`, `build.tail.log`, `tests/*.trx` and, from the `e2e` job, `e2e/results.json`, `e2e/contract.tap`, `e2e/app.log`, `shots/` and `failures/`, and from the `js` job `js/js-tests.tap`, `js/tsc.log` and `js/styles.log`. Renders `## Jobs`, `## Guards`, `## E2E`, `## JS`, `## Map styles`, `## Docker smoke`, `## Screenshots` and the AC matrix `## Acceptance criteria`. Node built-ins only. |
| `guards.mjs` | The twelve guards of 03 section 7.3: one `PASS`/`FAIL`/`REPORT` line per finding, exit 1 on any `FAIL`. The `guards` job tees its whole output to `guards.log` (uploaded with the `guards` artifact, also when a guard fails). |
| `image-smoke.sh` | Runs the built image the way the Supervisor does (Demo data, no Home Assistant) and checks items 1 to 12 of 03 section 7.8 (Demo container: healthz, base href, content types, cookie, healthcheck mode, diagnostics.json, logs, image size, root, forbidden paths, licence notices; and a second, Live container with Home Assistant unreachable: item 11); prints one `PASS`/`WARN`/`FAIL`/`SKIP` line per item and writes `ci-out/smoke.json` (sizes, time to healthy, one entry per item, and `failing_requests`: the response headers of a failed request, plus what the image holds for static web assets when the Blazor script is not served), which `make-summary.mjs` renders as `## Docker smoke`. Item 4 (`Set-Cookie` on `/`) is informational (D61): `WARN` with the cookie names, never `FAIL`. Needs Docker, so only the `docker-smoke` job runs it. |
| `validate-styles.mjs` | Validates the map styles against the MapLibre style specification: the styles built in `mapStyles.js` (satellite, demo-offline) with the zone and halo overlay appended are a `FAIL` on any error (exit 1); the three OpenFreeMap styles are fetched and an unreachable URL or a problem in the published style is only a `WARN`. `--offline` skips the fetches. The `js` job runs it as a soft step (`continue-on-error`) and keeps the output in `styles.log`. |
| `publish-ci-artifacts.sh` | Publishes that folder to `ci-artifacts` as one new orphan commit (the `publish-ci` job runs it). |
| `wait-for-ci.sh` | Waits for the run of one pushed commit and prints its `SUMMARY.md`. |
| `ci-common.sh` | Sourced by both scripts: `branch_slug` and `summary_field`. |

## The orchestrator's loop

```
git push origin slice/S0-skeleton
tools/ci/wait-for-ci.sh "$(git rev-parse HEAD)"          # prints SUMMARY.md, then RESULT=... and RUN_URL=...
```

`wait-for-ci.sh <sha> [branch]`: `<sha>` is the pushed commit (full, or at least 7 hex digits). Without `branch` it is
`git rev-parse --abbrev-ref HEAD` of the current directory, so run it in the worktree that pushed. It runs
`git fetch origin ci-artifacts --depth=1` every 30 seconds and matches the run in either of two ways: `LATEST.json`
carries the SHA, or the folder `runs/<branch-slug>/<run>-<sha7>/` exists (the second one is what keeps parallel
branches apart, because `LATEST.json` only names the last run to finish).

| Exit | Meaning |
|---|---|
| 0 | the run succeeded (`RESULT=success`) |
| 1 | the run failed (`RESULT=failure`); read the SUMMARY it printed |
| 2 | timeout (25 minutes; `WAIT_TIMEOUT_S` overrides it) |
| 3 | superseded: a newer run of the same branch was published, so the run for this SHA will never appear (a push that moves the branch tip cancels the older run) |
| 64 | usage error: not a SHA, no branch (detached HEAD), not a git repository |

Exit 3 works from a baseline: the highest run number under `runs/<slug>/` at the first poll. A match is always tried
first; only without one, a run above the baseline whose sha7 is not ours means the branch moved on.
`WAIT_POLL_S` (default 30) changes the poll interval.

If the script cannot tell what happened, the independent check is the Actions API for the SHA, with the read token:
`GET https://api.github.com/repos/Versile2/ha-cartographer/actions/runs?head_sha=<sha>` (`status` and `conclusion`). If `publish-ci`
itself could not push, the workflow artifacts (3 days) hold the raw logs.

By hand:

```
git fetch origin ci-artifacts --depth=1
git show origin/ci-artifacts:LATEST.md                     # newest run of any branch
git show origin/ci-artifacts:LATEST.json
git ls-tree --name-only origin/ci-artifacts runs/<branch-slug>/
git show origin/ci-artifacts:runs/<branch-slug>/<run>-<sha7>/SUMMARY.md
```

## New repository

The `ci-artifacts` branch is recreated by the first CI run of a new repository (`publish-ci-artifacts.sh` creates the orphan branch when none exists). It then contains only CI output of that repository; nothing from an earlier repository carries over, and `ci.yml` needs `contents: write` for the push.

## What is on `ci-artifacts`

```
README.md                                  static
LATEST.md, LATEST.json                     the newest run of any branch ({ run, sha, branch, slug, result, url })
runs/<branch-slug>/<run>-<sha7>/
    SUMMARY.md                             read this first
    errors.log                             compiler errors, distinct (the restore.log tail when the build never ran)
    build.tail.log                         last 300 lines of the build log
    tests/*.trx                            test results
    e2e/results.json                       Playwright's JSON report (the e2e job)
    e2e/contract.tap                       the payload contract run (tests/contract, node --test)
    e2e/app.log                            the last 300 lines of the Demo app's log during the e2e run
    js/js-tests.tap, js/tsc.log, js/styles.log   the js job's Node TAP, type check and style validation (each cut to its last 400 KB)
    shots/<project>/<scene>.png            the screenshot gallery (S6b), indexed in SUMMARY.md under `## Screenshots`
    failures/<test>.<project>.png          the first 20 screenshots of failing e2e tests
```

The branch slug is the branch name in lower case with every character outside `[a-z0-9._-]` replaced by `-`
(`slice/S5-map-1` becomes `slice-s5-map-1`). One folder per slug: the newest run replaces the previous one, the folders
of other branches are kept. Everything `make-summary.mjs` leaves in its `--out` folder is published, so a later job adds
its files there (screenshots, reports) and the publish script does not change.

## SUMMARY.md

The header is a contract between the three tools: `- result:` (`success` or `failure`), `- branch:`, `- sha:`, `- run:`
(the workflow run number) and `- url:`. After it: the job table (from the workflow's `needs`), the first 40 distinct
compiler errors as `file(line,col): CODE message` (all of them in `errors.log`), failed tests with the first 15 lines of
each message, and warnings. A run is a failure when any job did not succeed, or an error or failed test was found, or
the run left no input at all.

`## Guards` (after the job table, when a `guards.log` was downloaded) lists every `FAIL` line as written, one `REPORT` line per
guard with id ranges compressed (`REPORT ac-coverage: 50 AC ids missing (AC-01 … AC-50)`) and the `PASS` count; when guards
fail, `- why:` and the Notes say `guards failed: <guard names>` instead of the compiler or test-host wording.

`## Jobs` is the job table from the workflow's `needs`. `## Docker smoke` renders `smoke.json` (one line per item, sizes, time to healthy,
`failing_requests`); a missing `smoke.json` after a `docker-smoke` job that succeeded fails the run. `## JS` gives the Node test counts and the
type errors of `tsc.log` (failed tests and type errors fail the run); `## Map styles` the result of `validate-styles.mjs` (a `FAIL` line fails the
run, a `WARN` for a third-party style does not). `## Screenshots (n)` lists the gallery.

`## E2E` (after the job table, when the `e2e` job left a `results.json` or `contract.tap`) gives the Playwright counts (passed, failed,
flaky, skipped), then one block per failed test (project, file, title, the acceptance criteria in its title, the first 15 lines of
the error), the payload contract result, and the tail of the app log when something failed. A failed test, a failed contract test,
an unreadable `results.json` or one with no test fails the run; so does an `e2e` job that succeeded and left no `results.json`.
A test that failed and then passed on its retry is `flaky`: listed under `## Flaky tests`, never a failure of the run.

`## Acceptance criteria` is the AC matrix: one row per criterion, AC-01 to AC-50, with the status `passed`, `failed`, `skipped`,
`flaky`, `partial` or `missing` and where the tests were found. A criterion is read from the test titles that carry `[AC-nn]` (a suffix
such as `[AC-49a]` counts for AC-49): the display names in the `.trx` files (dotnet), the titles in `e2e/results.json` (retries
turn a pass into `flaky`) and the TAP of the `js` job (`js-tests.tap`). With several tests the worst status wins: failed, flaky,
partial, passed, skipped; `partial` means a test of the criterion passed while another was skipped, fixme'd or expected to fail; no test at all
is `missing`. The matrix itself derives no verdict (a failed test fails the run through the rules above; a flaky criterion is listed, not failed,
04 section 1.6). Since S15 (D50) `tools/ci/ac-scope.json` is `"enforce"` with all 50 ids: `ac-coverage` FAILs a missing or unknown AC id and
`testid-contract` FAILs a test id of `testids.json` that is absent from `src/`.

## Authentication of the publish step

`publish-ci-artifacts.sh` pushes with `--force-with-lease` against the tip it just fetched, retries up to 6 times with a
jittered, growing backoff, and every push is a fresh root commit. The `publish-ci` job has no `concurrency:` on purpose:
GitHub keeps one running and only one pending job per group and cancels the rest, so a third simultaneous publisher would
lose its SUMMARY, while this script already makes parallel publishers queue up safely (the workflow-level per-ref group stays). When `GH_TOKEN` is set and the remote is an `https://github.com/<owner>/<repo>` URL, it pushes through
`https://x-access-token:<token>@github.com/<owner>/<repo>.git` itself and masks the token in anything it prints; it does not
depend on the credentials `actions/checkout` persisted. It never runs with `set -x`. It works against any remote, which is
how the tests exercise it (a `file://` bare repository). Environment: `CI_OUT` (default `ci-out`), `CI_REMOTE` (default
`origin`), `PUBLISH_BACKOFF_S` (base of the backoff in seconds, default 2).

## Tests

```
node --test "tests/js/**/*.test.mjs"
```

The tests build temporary git repositories and need `git`, `bash` and Node 22. They run locally only until the `js` job
of slice S5 exists; its quoted glob then picks them up.

## Releases

`release.yml` is not part of the CI summary. It runs automatically when a PR is merged to main (a push to main), and by hand too (Actions -> release -> Run workflow, on main). It reads `version` from `realm/config.yaml`; the tag is `v<version>`. If that tag already exists, a push run skips cleanly (notice and job summary "bump realm/config.yaml version to release") and a by-hand run fails with that message (never re-released). Otherwise it needs the `## <version>` section in `realm/CHANGELOG.md`, builds and pushes `ghcr.io/versile2/ha-cartographer:<version>`, and only after that creates the tag and a GitHub release with notes generated from the merged PRs (`.github/release.yml`). It fails on any ref other than main; the tag exists only once the image does, so a run that failed earlier can be run again by hand. `.github/pull_request_template.md` reminds authors that merging releases.
