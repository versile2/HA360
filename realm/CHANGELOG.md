# Changelog

## 0.3.1

### Added

- **Trackers keep a history, like people.** A tracker (a GPS device tracker under **Trackers**) now stores its positions, and
  its **History** screen shows the same day view, timeline and trail as a person's: the places it stayed and its moves, written
  **Moved - 12.4 mi - 24 mins** (a tracker has no driver, so there is no speed or phone-use chip). Positions are stored in the same
  place as a person's, so they age out with `retention_fix_days` and a tracker that is **Not tracked** stores nothing.
- **A "Keep history" switch per tracker.** Settings, Who's on the map, tap a tracker, then **Keep history** (on by default, for
  existing and new trackers; remember to press Save). Off: new positions are no longer stored, the tracker shows only its latest
  position again, and what was stored stays in History until it ages out. With the switch off and nothing stored, its History page
  says so and how to turn it on. The helper text gives the size: about 6-20 MB per 100 days.
- **Backfill.** When the add-on starts, a tracker that keeps its history is filled from Home Assistant's recorder, back as far as
  `backfill_days` (so a tracker you switch on today has its past days at the next start).
- **Demo.** The Ford Pickup in the demo has five days of history.

### Notes

- A database migration (`0004`) adds one column to the roster. Moves are worked out from the stored positions when History is
  opened, so a tracker that reports rarely shows a rough (dashed) path.

## 0.3.0

### Added

- **Location History.** Open a person (or a tracker) on the map, open their detail and press **History**. The screen shows one day
  at a time: the trail on the map (drives as solid lines in the person's colour, a dashed line where only a rough path was
  recorded, a gap where there is no data), a numbered marker for every place they stayed, and the two ends of the day.
  Below the map (a side panel on a wide screen) the timeline lists the day as it happened: **At Hearth Haven - 8:05 am to
  5:42 pm - 9 hrs 37 mins**, then **Drive - 12.4 mi - 24 mins - top 71 mph** with where it started and ended and chips for
  speeding and phone use. Tap a row to see it on the map; tap a drive's path on the map to find its row.
- **A day selector.** Previous and next day, a date button that opens a calendar limited to the days the app keeps, and **Today**.
  The day is the Home Assistant time zone's day (a day when the clocks change has 23 or 25 hours and is measured right).
  `/history/{person}?date=YYYY-MM-DD` opens a day directly.
- **Last 7 days.** A toggle lists the last seven days, newest first, each with its visits and drives; tap a day to open it.
- **Places are named, never "unknown".** A visit is at a zone you drew in Home Assistant, otherwise at the town or street the
  position is in ("Pinebrook", "near Pinebrook"). A visit counts when the person stayed at least 5 minutes.
- **Print.** The History screen prints (or saves as PDF) as a table of the day or of the seven days, with the header
  "Location History - name - date", without the map.
- **Demo.** The demo cast has a believable week of visits and drives.

### Notes

- History is built from the positions and trips the app already keeps, for as many days as `retention_fix_days` allows (100 by
  default). Nothing new is stored, there is no migration and no new option. A person who is **Not tracked**, and a tracker (which
  keeps only its latest position), have no history. Days before the app first recorded a person are empty.
## 0.2.3

### Changed

- **Add place now behaves like Home Assistant's own zone editor.** The radius starts at 100 m (Home Assistant's default for a
  new zone) and the slider moves in single steps from 0 up to 2 km. Home Assistant's editor is a number box that starts at 0,
  steps by 1 and has no upper limit; the 2 km cap is this app's, because the control is a slider.
- **The radius is shown in your Home Assistant unit system.** A metric Home Assistant shows metres and kilometres, a US customary
  one shows feet and miles (the slider then runs 0 to 6,500 ft). The app reads the unit system from Home Assistant's
  configuration when you start adding a place; the Demo is imperial. What is sent to Home Assistant is always metres.
- **New wording.** The add-on description now reads "A Life360-style family map with driving reports, inside Home Assistant.",
  and the introduction in the README, the add-on's Documentation tab and the landing page says what the app is: a map-focused
  alternative to Home Assistant's built-in map with Life360-style features.

Home Assistant's values, for the record: default radius 100 (`DEFAULT_RADIUS` in
[`homeassistant/const.py`](https://github.com/home-assistant/core/blob/dev/homeassistant/const.py), also the new-zone value in
[`dialog-zone-detail.ts`](https://github.com/home-assistant/frontend/blob/dev/src/panels/config/zone/dialog-zone-detail.ts));
radius field min 0, step 1, no max, unit "meters" in
[`ha-selector-location.ts`](https://github.com/home-assistant/frontend/blob/dev/src/components/ha-selector/ha-selector-location.ts).

## 0.2.2

### Added

- **Add rows at the end of every list.** The Drivers tab ends with **+ Add driver**, the Trackers tab with **+ Add tracker**
  and the Places tab with **+ Add place**.
- **Add driver / Add tracker** open a list of everything Home Assistant and Life360 report (people, GPS trackers and Life360
  members) with the same picture the map and Settings draw, the name, the Home Assistant entity id and friendly name, and where
  it comes from. What is already on the map is greyed out ("Already on the map"); what is Not tracked can be chosen. A search
  box filters. Choosing one moves it to People or Trackers, exactly like Settings, and selects it on the map.
- **Add place** puts a pin in the middle of the map with a radius circle. Drag the pin (or tap the map, or use the arrow keys),
  set the radius, give the place a name and an icon, and press Save: the app creates a real **zone in Home
  Assistant** (the same as Settings -> Areas, labels and zones -> Zones) and it appears on the map and in Places within a few
  seconds. Esc, Back or Cancel leave without adding anything. Creating a zone needs a Home Assistant administrator; if Home
  Assistant refuses, you get a clear message and nothing is added.
- **A new option, `allow_add`** (on by default). Switch it off to hide the three Add rows. In Demo mode the rows always show and
  the actions are only simulated: nothing is written to Home Assistant.

### Changed

- This is the first time the app writes a zone to Home Assistant; the persistent notifications are still the only other write.
  A zone you delete in Home Assistant disappears from the app, as before.

## 0.2.1

### Added

- **Edit what a row shows.** Tap a row in Settings -> Who's on the map to see which Home Assistant entities it is (entity
  ids with their friendly names, and the Life360 member name when Life360 feeds it) and to change its name, title, colour
  and picture: the photo from Home Assistant or Life360, the first letter of the name, or a car, truck, person, pet,
  phone or tag. The map, the lists and the reports use your values.
- Your values are kept apart from the source's: a later change of a name in Home Assistant or Life360 never replaces
  them. **Reset to Home Assistant / Life360** (shown once you changed something) brings the source's values back.

### Changed

- **Zones match Home Assistant exactly.** Every zone is drawn and listed, whatever its radius (a very large zone such
  as a regional "approach" zone used to be left out). A circle wider than the map view is drawn as an outline with a
  very faint fill, and a tap inside it is a tap on the map, so pins and controls stay easy to hit.
- **Tapping Drivers, Trackers or Places while the sheet is low now opens it to 80 % and switches to that tab.** Back
  from 80 % returns to the low sheet. The side panel is unchanged.
- **"Vehicles" is now "Trackers"** everywhere you see it: the sheet tab, Settings, the summary line, the notifications and
  the documentation.
- **Settings rows are all the same shape** (handle, avatar, two lines of text, the three-dot button), and the avatar is
  what the map pin shows for that entry: the photo when the pin shows one, else the same initial or glyph on the same colour.
- A tracker's pin is now drawn on its roster colour (a rounded square), like a person's pin.
- The database gets a small schema update (0003) that adds columns for the values above; nothing is lost.

## 0.2.0

### Added

- **Who's on the map.** A new section at the top of Settings lists every person and GPS tracker that Home Assistant
  reports, in three groups: People, Vehicles and Not tracked. Drag a row to another group, or use its menu
  (Move to...). Tap a row to change its name, title and colour.
- On the first start every person, and every GPS tracker that reported a position in the last 30 days, is put under
  People. Later, anything new is added to People. A tracker that has been unavailable, unknown or removed for 7 days
  moves to Not tracked by itself. Each of these steps leaves one notification in Home Assistant
  (HA Cartographer: ...) that points to Settings -> Who's on the map. Nothing is sent in Demo.
- A vehicle is any tracker you put under Vehicles. It follows the tracker's position; there is no odometer or fuel.
- **Longer periods in the Driving report.** A fifth control next to the week chips is a split button: the main part
  re-applies your last long period (Last month by default), the arrow opens a menu with Last month, Last 3 months, Last
  6 months, Last year and Custom range. Last 6 months and Last year appear only when `retention_fix_days` covers them
  (185 and 366 days). The period is kept in the page address, so Back, printing and links work.
- **Custom range** with a start and an end date picker, checked against today and the history that is kept.
- **Print or save as PDF** from every Driving page: a clean Letter/A4 layout with the totals, drivers and every drive.
- The drive list of a driver has a pager on top and at the bottom (25, 50 or 100 rows).

### Changed

- **The sources are Home Assistant and Life360 only.** FordPass, the second-vehicle placeholder and their Connections
  rows are gone, and so is the stored vehicle history of the FordPass sensors (the table is dropped).
- **The add-on has six options now:** demo_mode, allow_demo_param, log_level, driving_week_start,
  driving_speeding_mph and retention_fix_days. The people, vehicles, places, ignore list and tuning options are gone;
  their values are fixed defaults, and any old value in your configuration is ignored.
- Ids of people and vehicles come from Home Assistant now (a person's id is the part of person.xxx after the dot, a
  tracker's id is tracker_ plus the part after the dot). Positions stored under the old ids are no longer shown; they age out with
  the history retention.
- The add-on makes one call that writes to Home Assistant: creating the notifications above.

- **Drives and top speeds now say where they happened**: the zone name, otherwise the city ("near Pinebrook", "I-65
  near Pinebrook"), otherwise the nearest zone. "Unknown place" is gone.
- `retention_fix_days` now ranges from 100 to 400 days and defaults to 100. Each 100 days uses roughly 6-20 MB per
  tracked person (estimate).

## 0.1.2

### Fixed

- After an update, the first page load no longer logs an antiforgery key error (the app's keys are now kept in /data).

### Changed

- Demo mode is now the first option in the add-on configuration.
- The status line in a person's details shows how fresh the location is ("updated 3 min ago").

## 0.1.1

### Changed

- **Settings moved into the bottom bar.** The floating gear is gone from the map and from the Driving pages, where it
  overlapped content. The bottom bar is now Location, Driving and Settings; Settings opens the same dialog as before
  (Back and Esc close it) and leaves the current tab selected. The map credits may use the left of the screen now.

### Added

- **Report an issue and Star this project** in Settings -> About. They open the GitHub issue chooser and the project
  page in a new tab.

### Fixed

- **Map credits no longer cover the map controls** on phones. The credits pill folds after five seconds from the start
  of the page (it no longer waits for every map tile), and folds on the first tap without swallowing it.
- **Detail views no longer say things twice.** A person's freshness line ("The raven's late, last seen 42 min ago")
  now shows once, in the status card under the place, not also under the name; the street address line is left out when
  it is the same place the card already shows (a stale Dara, Briar on I-65). A vehicle's "Updated 20 min ago" under its
  name is gone too (its Last update row and stale warning say it). Screen readers still hear the status.
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
