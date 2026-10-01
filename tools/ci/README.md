# tools/ci

The CI feedback loop: push, wait, read one short markdown file over `git`, fix, push.
Nobody reads a console, so every run, pass or fail, ends in a readable summary on the orphan branch `ci-artifacts`.

| File | Role |
|---|---|
| `make-summary.mjs` | Reads the downloaded job artifacts and writes `SUMMARY.md`, `errors.log`, `build.tail.log` and `tests/*.trx`. Node built-ins only. |
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
`GET https://api.github.com/repos/Versile2/ha360/actions/runs?head_sha=<sha>` (`status` and `conclusion`). If `publish-ci`
itself could not push, the workflow artifacts (3 days) hold the raw logs.

By hand:

```
git fetch origin ci-artifacts --depth=1
git show origin/ci-artifacts:LATEST.md                     # newest run of any branch
git show origin/ci-artifacts:LATEST.json
git ls-tree --name-only origin/ci-artifacts runs/<branch-slug>/
git show origin/ci-artifacts:runs/<branch-slug>/<run>-<sha7>/SUMMARY.md
```

## What is on `ci-artifacts`

```
README.md                                  static
LATEST.md, LATEST.json                     the newest run of any branch ({ run, sha, branch, slug, result, url })
runs/<branch-slug>/<run>-<sha7>/
    SUMMARY.md                             read this first
    errors.log                             compiler errors, distinct (the restore.log tail when the build never ran)
    build.tail.log                         last 300 lines of the build log
    tests/*.trx                            test results
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

## Authentication of the publish step

`publish-ci-artifacts.sh` pushes with `--force-with-lease` against the tip it just fetched, retries 3 times, and every push
is a fresh root commit. When `GH_TOKEN` is set and the remote is an `https://github.com/<owner>/<repo>` URL, it pushes through
`https://x-access-token:<token>@github.com/<owner>/<repo>.git` itself and masks the token in anything it prints; it does not
depend on the credentials `actions/checkout` persisted. It never runs with `set -x`. It works against any remote, which is
how the tests exercise it (a `file://` bare repository). Environment: `CI_OUT` (default `ci-out`), `CI_REMOTE` (default
`origin`), `PUBLISH_BACKOFF_S` (default 2).

## Tests

```
node --test "tests/js/**/*.test.mjs"
```

The tests build temporary git repositories and need `git`, `bash` and Node 22. They run locally only until the `js` job
of slice S5 exists; its quoted glob then picks them up.
