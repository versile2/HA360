# HA360 — The Realm

A map-first family locator that runs as a Home Assistant add-on. It works with Home Assistant's Life360 integration
and the Home Assistant companion app.

Status: skeleton in place (build configuration, projects, CI feedback loop); features arrive slice by slice.

## Build

- .NET 10 SDK (see `global.json`): `dotnet build Realm.slnx` and `dotnet test Realm.slnx`.
- CI: `.github/workflows/ci.yml`. Every run publishes a summary to the `ci-artifacts` branch; see `tools/ci/README.md`.

## Licence

The Realm is released under the MIT licence ([LICENSE](LICENSE)). The components it bundles or fetches, and the
licences and credits they ask for, are listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). The image carries
both files in `/app`.

## Trademarks

The Realm is an independent, unofficial project. It is not affiliated with, endorsed by or sponsored by Life360, Inc.,
the Open Home Foundation or Nabu Casa. Life360 is a trademark of Life360, Inc. Home Assistant names and logos belong
to their owners. These names are used only to say what the app works with.
