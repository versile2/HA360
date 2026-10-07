# Changelog

## 0.1.1

### Fixed

- **Map credits no longer cover the settings gear** on phones. The credits pill is kept clear of the gear, folds after
  five seconds from the start of the page (it no longer waits for every map tile), and folds on the first tap without
  swallowing it.
- **Satellite map style no longer fails to load** when the page is in the background or throttled. Styles built in the
  app are now loaded through a blob URL, which does not wait for an animation frame.

## 0.1.0

First experimental release.

### What it contains

- **Location.** A full-screen map with pins for people, vehicles and saved places, and a bottom sheet with Drivers,
  Vehicles and Places. The sheet rests at a peek height, and a tap on its handle opens it to 80 % (and back).
- **Edge bubbles and fan-out.** People outside the visible map appear as bubbles on its edge, pointing the way and
  grouping when they are close together. Pins that sit on top of each other at the same place fan out so that each
  one can be tapped.
- **Selection and details.** Choosing someone (a pin, an edge bubble or a row) centres the map on them and shows a
  summary at peek height; a tap on the summary or on the handle opens the full details.
- **Back.** The Back gesture or button steps up one level at a time (report, details, summary, no selection) and
  leaves the app only from the top. The option `ui_history_tokens` turns this off if Back misbehaves inside the Home
  Assistant app.
- **Driving.** A weekly report (Monday to Sunday by default, option `driving_week_start`): speeding, phone use, top
  speed, drives and miles, a card per driver, a popup that explains each statistic, and a page for one driver's week
  with their drives by day. The app derives the trips from the positions it stores.
- **Map styles and Settings.** Night, Day, Streets and Satellite map styles, and a Settings dialog (map style, saved
  places on or off, default view radius, layout: Auto, bottom sheet or side panel, connection status, map credits).
- **Diagnostics.** `diagnostics.json`, opened from Settings, About: connection states, counts, database health and
  warning codes, without names, positions or tokens.
- **Live data from Home Assistant.** Positions come from the `device_tracker`, `person` and `zone` entities that Home
  Assistant already has (its Life360 integration and the companion apps) and from the vehicle entities you configure.
  The app keeps its own SQLite history under `/data`, fills in recent history from Home Assistant when it starts, and
  trims old positions after `retention_fix_days`.
- **Demo mode.** The option `demo_mode` shows a fictional family on a frozen clock, with no network access, so an
  install can be checked before any real data is involved.
- **Packaging.** An Ingress sidebar panel called "The Realm", an amd64 image and cold backups.

### Known limits

- Everything comes from Home Assistant. The app does not call Life360's own API; a Life360 REST client (member list
  cross-check, saved places, history backfill) is planned for 1.1.
- Rapid acceleration and hard braking show a dash: position updates are too far apart to measure them.
- Speeding is counted from the positions the app received, so it is labelled sampled. Distances are estimated from
  positions and run a few percent low on winding roads; odometer-based distances are planned for 1.1.
- Phone use is shown only where the companion app's `interactive` sensor is enabled in Home Assistant; until then it
  is a dash.
- Dark theme only (no light theme), miles and mph only, and no trails or location timeline yet.
