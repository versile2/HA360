# HA Cartographer

HA Cartographer is a map-first family tracker that runs inside Home Assistant. It shows where everyone is on a full-screen
map with a bottom sheet (Drivers, Vehicles, Places) and keeps its own history, so it can show weekly driving reports
that go back further than Home Assistant's recorder does. It opens from the Home Assistant sidebar, so Home Assistant's
own login is the only login.

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
   Drag a row to **People**, **Vehicles** or **Not tracked**, or use the row's menu (the three dots, **Move to...**). Tap a row to
   change its name, title (a secondary label) and colour. A vehicle is a tracker you put under Vehicles; it follows the
   tracker's position and has no odometer or fuel.

Later, anything new that Home Assistant reports joins **People** with a notification of its own, and a person or tracker that has
been unavailable, unknown or removed for 7 days moves to **Not tracked** by itself, again with a notification. Not tracked means
off the map, the lists and the reports, and no new history is stored for it; what was stored before stays until it ages out.
Moving something by hand is never undone by the app.

The names, titles and colours live in the app's own database on your Home Assistant. They are never part of the
repository or the image.

## Add-on options

The **Configuration** tab has six options. Everything else is a fixed default, and an option from an older version that is
still in your configuration is ignored.

| Option | Meaning |
|---|---|
| `demo_mode` | Show the fictional family instead of your data. |
| `allow_demo_param` | Let `?demo=1` open a demo session while the app runs on real data. |
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
the Supervisor. It changes nothing in Home Assistant except to create the persistent notifications described under Setup.

## What is stored and where

- Positions, trips and the list of who is on the map are stored in `/data/realm.db` (SQLite) and are included in backups. Positions
  are kept for 120 days by default (option `retention_fix_days`).
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
