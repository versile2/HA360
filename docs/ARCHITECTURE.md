# The Realm: architecture in one page

## What it is

The Realm is a Home Assistant add-on (an "app" in current Home Assistant wording) that shows a map-first family
locator: people on a full-screen map, a bottom sheet with Drivers, Vehicles and Places, and weekly driving reports built
from its own stored history. It appears as a sidebar panel through Ingress, so Home Assistant's login is the only login,
and it works in the companion app. It publishes no port.

Live positions come from Home Assistant's own entities (the Life360 trackers, the companion apps and zones). The
add-on holds no Life360 login or token and never calls Life360's API. The one exception is member pictures: with the
`avatar` option of a member set to `life360` (and in the default `auto` mode when the person has no picture of their own
in Home Assistant), the add-on downloads the picture, without credentials, from the `life360.com` address Home Assistant
reports for the member's tracker, and caches it under `/data/cache/avatars`. All personal configuration (names, entity
ids, addresses) lives in the add-on's options on the user's own Home Assistant, never in this repository.

## Stack

| Layer | Choice |
|---|---|
| Runtime | .NET 10, Blazor Web App with Interactive Server rendering |
| UI | MudBlazor 9.5.0 and MudX (the bottom sheet), dark theme |
| Map | MapLibre GL JS 6.11.2, vendored, driven through JS interop; tiles are fetched by the browser |
| Home Assistant access | WebSocket and REST through the Supervisor proxy (`homeassistant_api`) |
| History | SQLite under `/data`, schema as plain SQL scripts keyed on `PRAGMA user_version`; EF Core is only a mapper |
| Packaging | One image per version, `ghcr.io/versile2/ha-cartographer:<version>`, add-on slug `realm`, Ingress on port 8099 |

Per-frame work (pins, bubbles, camera) is JavaScript; the server sends immutable view-model payloads and receives
discrete events. The app runs in one of two modes from the same UI: `Live` (Home Assistant plus SQLite) and `Demo`
(a fictional family with a frozen clock and no network, used for tests, screenshots and install checks).

## Project layout

```
repository.yaml            add-on repository descriptor (required at the root)
realm/                     the add-on folder: config.yaml, DOCS.md, CHANGELOG.md, translations/, icon.png, logo.png
src/Realm.Domain/          pure models, algorithms and ports; no I/O
src/Realm.Infrastructure/  Home Assistant clients, SQLite, options, hosted services
src/Realm.Demo/            the fictional fixture and the Demo implementations of the ports
src/Realm.Web/             the Blazor app and its JS modules (wwwroot)
tests/                     xUnit and bUnit projects, JS unit tests, Playwright end-to-end tests (each added with its code)
tools/ci/                  the CI feedback scripts and option-bindings.json
.github/workflows/         ci.yml, release.yml (on merge to main, or by hand)
docs/ARCHITECTURE.md       this file
```

`realm/config.yaml` is the only `config.yaml` in the repository, because the Supervisor registers one app per config
file it finds. `tools/ci/option-bindings.json` lists every add-on option key with the .NET configuration path it binds
to; the repository guards (see How CI works) compare that list with `config.yaml` and `realm/translations/en.yaml`.

## Configuration

The Configuration tab (the `options` and `schema` blocks of `realm/config.yaml`) holds thresholds, switches and three
lists: `members`, `vehicles` and `places`. They ship empty; with no options set the app discovers people from Home
Assistant and shows plain names. A small example with an invented cast (patterns in angle brackets stand for your own
entity ids):

```yaml
members:
  - id: king
    display_name: "Alden"
    lore_title: "The King"
    color: "#E8BC4E"
    person: "person.<slug>"
    life360_tracker: "device_tracker.<slug>"
    companion_tracker: "device_tracker.<slug>"
  - id: queen
    display_name: "Briar"
    lore_title: "The Queen"
vehicles:
  - id: wagon
    name: "Pickup"
    lore_title: "The King's Wagon"
    glyph: pickup
    integration: fordpass
    entity_prefix: "fordpass_<vin>"
places:
  - zone: "zone.<slug>"
    display_name: "Hearth Haven"
    subtitle: "Home"
    kind: home
```

A member with `kind: static` is a fixed pin for someone who does not share location (`static_label`, `static_latitude`,
`static_longitude`). `lore_title` is only a secondary label; plain names stay the primary text.

## How CI works

The development sandbox cannot restore NuGet packages, so GitHub Actions is the compiler and the test runner. The loop is
push, wait, read one short markdown file, fix, push.

1. `.github/workflows/ci.yml` runs on every push except to the `ci-artifacts` branch. The `dotnet` job restores, builds
   with warnings as errors, and runs the tests; its logs and `.trx` results are uploaded as a workflow artifact.
2. The `publish-ci` job runs after it, pass or fail. `tools/ci/make-summary.mjs` (Node built-ins only) turns the
   artifacts into a `SUMMARY.md` with the result, the first distinct compiler errors as `file(line,col): CODE message`
   and the failed tests. `tools/ci/publish-ci-artifacts.sh` pushes that to the orphan branch `ci-artifacts` as one new
   commit: `LATEST.md`, `LATEST.json` and `runs/<branch-slug>/<run>-<sha7>/`.
3. `tools/ci/wait-for-ci.sh <sha>` polls that branch over plain `git` and prints the summary for the pushed commit
   (exit 0 success, 1 failure, 2 timeout, 3 superseded by a newer push, 64 usage error). Details are in
   `tools/ci/README.md`.

The jobs are `guards` (repository policy, seconds; the others wait for it), `dotnet`, `js` (types, Node tests, map style
validation), `docker-smoke` (the image built and exercised on every push), `e2e` (Playwright against the published Demo
app) and `publish-ci`, which waits for all of them. `SUMMARY.md` has a section for each, plus the AC matrix.

## Releasing

The add-on version in `realm/config.yaml` equals the image tag, and every version has a section in
`realm/CHANGELOG.md`. A release is made automatically by merging a PR to main
whose `realm/config.yaml` has a `version` that has no tag yet (and whose CHANGELOG has the section): the release workflow builds and pushes
the image, then creates the tag `vX.Y.Z` and the GitHub release (CHANGELOG section plus notes generated from the merged PRs). A merge
that leaves the version unchanged releases nothing. Actions -> release -> Run workflow re-runs it by hand. The store
offers the new version as soon as main has it, before the image is pushed; an update attempted in that window fails to pull and
succeeds on retry. Install in Home Assistant by adding `https://github.com/Versile2/ha-cartographer` as an app
repository.
