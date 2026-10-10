# HA Cartographer

HA Cartographer is a map-focused Home Assistant add-on: a more capable and convenient alternative to Home Assistant's built-in map. It brings familiar Life360-style features into Home Assistant: an interactive family map, location history, places that match your Home Assistant zones, and weekly driving reports with speeding, phone use and top speed.

It shows where everyone is on a full-screen map with a bottom sheet (Drivers, Trackers, Places) and keeps its own history, so it can show driving reports (by week, month, 3, 6 or 12 months, or a custom range; printable) and each person's Location History (a day at a time on the map and as a list of visits and drives, or the last 7 days; printable), which go back further than Home Assistant's recorder does. It opens from the Home Assistant sidebar, so Home Assistant's own login is the only login.

## Setup

1. In Home Assistant open **Settings -> Apps -> Install app**, open the menu (three dots) and choose **Repositories**,
   and add `https://github.com/Versile2/ha-cartographer`.
2. Install **HA Cartographer** and press **Start**.
3. Switch on **Show in sidebar** on the app's page. Home Assistant keeps this as a per-install setting, so the app
   cannot turn it on by itself. Optionally switch on **Watchdog** too.
4. Open the sidebar entry called **The Realm** (crown icon; the app is listed as HA Cartographer in the store).
5. Choose who is on the map inside the app: open **Settings** (the third item of the bottom bar) and use **Who's on the
   map**. On the first start the app puts every `person` entity, and every GPS `device_tracker` that reported a position in
   the last 30 days, under **People**, and leaves one notification in Home Assistant ("HA Cartographer: ...") that says so.
   Drag a row to **People**, **Trackers** or **Not tracked**, or use the row's menu (the three dots, **Move to...**). Tap a row to
   see which Home Assistant entities it is (entity ids with their friendly names, and the Life360 member name when Life360
   feeds it) and to change its name, title (a secondary label), colour and picture (the photo of Home Assistant or Life360,
   the first letter of the name, or a car, truck, person, pet, phone or tag). What you change is kept apart from what Home
   Assistant and Life360 say, so a later change there never replaces it; **Reset to Home Assistant / Life360** brings the
   source's values back. The map, the lists and the reports use your values. A tracker you put under Trackers follows
   its position and has no odometer or fuel.

Later, anything new that Home Assistant reports joins **People** with a notification of its own, and a person or tracker that has
been unavailable, unknown or removed for 7 days moves to **Not tracked** by itself, again with a notification. Not tracked means
off the map, the lists and the reports, and no new history is stored for it; what was stored before stays until it ages out.
Moving something by hand is never undone by the app.

## Adding from the lists

The last row of each list adds something. **+ Add driver** and **+ Add tracker** list everything the app found: choose one
that is Not tracked and it moves onto the map (what is already there is greyed out). **+ Add place** lets you drop a pin,
set a radius between 25 m and 2 km, name it and choose an icon; **Save** creates a zone in Home Assistant, which then shows
up here like any other zone. Creating a zone needs a Home Assistant administrator account; if yours is not one, the app says
so and adds nothing. The place's icon is stored in Home Assistant, but Home Assistant's zone list does not hand icons to the
app, so a place you add shows with the plain pin icon in the Places list. Switch the three rows off with the `allow_add` option.

The names, titles and colours live in the app's own database on your Home Assistant. They are never part of the
repository or the image.

## Add-on options

The **Configuration** tab has seven options. Everything else is a fixed default, and an option from an older version that is
still in your configuration is ignored.

| Option | Meaning |
|---|---|
| `demo_mode` | Show the fictional family instead of your data. |
| `allow_demo_param` | Let `?demo=1` open a demo session while the app runs on real data. |
| `allow_add` | Show the **+ Add driver**, **+ Add tracker** and **+ Add place** rows at the end of the lists (default on). Demo mode always shows them. |
| `log_level` | How much the app writes to its log. |
| `retention_fix_days` | How long positions are kept (default 120). |
| `driving_week_start` | The first day of the driving report's week (`monday` or `sunday`). |
| `driving_speeding_mph` | The speed above which a drive counts a speeding event (default 80). |

## Where the data comes from

Home Assistant, with one small exception. The app subscribes to the `device_tracker`, `person` and `zone` entities of
Home Assistant's own Life360 integration and of the companion apps; these are the only two sources (Home Assistant and Life360).
It holds no Life360 login or token and does not log in to or call Life360's API. The exception is member pictures: when
the picture Home Assistant reports for a member is an HTTPS address on `life360.com`, the app downloads it without
credentials and caches it under `/data/cache/avatars`. That happens when the person has no picture of their own in Home Assistant; without any picture the app shows
initials. The app has no access to Home Assistant's configuration files: it talks to Home Assistant only through
the Supervisor. It changes nothing in Home Assistant except to create the persistent notifications described under Setup and, when you use **+ Add place**, the zone you ask for (Home Assistant's `zone/create` command, which needs an administrator; switch the rows off with `allow_add`).

## Location History

Open a person (or a tracker) on the map, open their detail and press **History**. The day shows on the map as a trail with a marker for every place they stayed, and below it as a list: **At Hearth Haven, 8:05 am - 5:42 pm, 9 hrs 37 mins**, and **Drive, 12.4 mi, 24 mins, top 71 mph** with where it started and ended. A visit is named after the Home Assistant zone, otherwise the town or street. Use the arrows, the date button or **Today** to change the day, and **Last 7 days** to list the week. The days go back as far as `retention_fix_days`; a person who is Not tracked has no history, and a tracker keeps only its latest position. **Print** saves the day as a table without the map.

## What is stored and where

- Positions, trips and the list of who is on the map are stored in `/data/realm.db` (SQLite) and are included in backups. Positions
  are kept for 100 days by default (option `retention_fix_days`, 100 to 400; the Driving report's 6-month and 1-year periods need at least 185 and 366 days).
- Avatar pictures are cached under `/data/cache/avatars`, which is excluded from backups. Home Assistant's frontend may
  also cache them in your browser for the same user.
- Nothing leaves your Home Assistant box except two things. Map tiles and style files, which your browser (not the app)
  fetches from public map servers (OpenFreeMap and, for the satellite style, the USGS National Map): those servers see
  your IP address and the area you are viewing, not who is on the map. And the member pictures described above, which
  the app downloads from `life360.com` without credentials.
- No analytics, no telemetry, no geocoding and no update checks. Ingress is the only door to the app: it publishes no
  port. There is no cloud service behind it.
- Driving statistics are computed when a trip closes. If you change `driving_speeding_mph`, the change applies to trips
  that close from then on; earlier trips keep the numbers they were stored with.

## Phone-use stat

To count phone use while driving, enable the disabled `binary_sensor.<device>_interactive` entity of the companion app in
Home Assistant (Settings -> Devices -> the phone -> show disabled entities). The app never changes Home Assistant's
entity configuration, so this is your decision. Until it is enabled, the phone-use figure shows a dash.

## Demo mode

Switch on the `demo_mode` option to show a fictional family with a frozen clock and no network access. It is useful for
checking that the install works before real data is involved. With `allow_demo_param` on, adding `?demo=1` to the
address opens a demo session while the app runs on real data.

## Troubleshooting

- **Diagnostics.** In the app tap **Settings** (the third item of the bottom bar, right of Location and Driving), then **About -> Diagnostics**, to see `diagnostics.json`: connection states,
  counts, version numbers, database health and a list of warning codes. It holds no names, positions or tokens, only
  your option ids, so strip those before pasting it into a public issue. **Settings -> About -> Report an issue** opens the
  GitHub issue chooser in a new tab, and **Star this project** opens the repository page.

  | Warning code | Meaning |
  |---|---|
  | `ha_unavailable` | The app cannot reach Home Assistant. |
  | `ha_auth_failed` | Home Assistant refused the app's access. |
  | `ha_ws_down_over_60s` | The live connection to Home Assistant has been down for more than a minute. |
  | `ingest_drops` | Incoming updates were dropped because the app could not keep up. |
  | `writer_queue_over_80pct` | The database write queue is more than 80 % full. |
  | `unclean_shutdown` | The app did not shut down cleanly the last time. |
  | `zone_data_missing` | Time-zone data could not be resolved at start. |
  | `payload_schema_mismatch` | Data for the map had an unexpected shape. |

- **Banners.** A banner appears when the live connection to Home Assistant is lost, or when every Life360 tracker is
  unavailable. It clears by itself when data returns.
- **"Stale" or "offline".** A person is shown as stale after 30 minutes without a new position (never sooner than their
  source's normal update interval allows) and as offline after 24 hours. `diagnostics.json` lists the effective stale
  threshold per member.
- **Someone is missing from the map.** Open **Settings -> Who's on the map**. If they are under **Not tracked**, move them back to
  **People**. A tracker that is not a GPS tracker (a router or Bluetooth tracker) is never listed.
- **The Back gesture.** Back inside the Home Assistant app leaves the app at once when the browser history has nothing to
  go back to; Esc and the close button always work.
- **The sidebar entry is missing.** Check that **Show in sidebar** is on for the app (see Setup).

## Trademarks

HA Cartographer is an independent, unofficial project. It is not affiliated with, endorsed by or sponsored by Life360, Inc.,
the Open Home Foundation or Nabu Casa. Life360 is a trademark of Life360, Inc. Home Assistant names and logos belong
to their owners. These names are used only to say what the app works with.
