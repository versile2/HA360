# HA360 — The Realm

A Life360-style, map-first family tracker that runs as a Home Assistant add-on.

Status: skeleton in place (build configuration, projects, CI feedback loop); features arrive slice by slice.

## Build

- .NET 10 SDK (see `global.json`): `dotnet build Realm.slnx` and `dotnet test Realm.slnx`.
- CI: `.github/workflows/ci.yml`. Every run publishes a summary to the `ci-artifacts` branch; see `tools/ci/README.md`.
