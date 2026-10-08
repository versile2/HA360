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
4. Open the sidebar entry called **The Realm** (crown icon; the app is listed as HA Cartographer in the store). With no options set, everyone found through Home Assistant appears with a plain
   name.
5. To name people, vehicles and places, open the **Configuration** tab. Members, vehicles and places are lists:
   each member has an `id` (a short lower-case slug) and a `display_name`, and can have a `lore_title` (a secondary
   label), a `color`, and the entity ids of its trackers. An example is in
   [docs/ARCHITECTURE.md](https://github.com/Versile2/ha-cartographer/blob/main/docs/ARCHITECTURE.md). Options are read when the
   app starts, so restart the app after saving them.

Your names, entity ids and addresses live only in these options on your own Home Assistant. They are never part of the
repository or the image.

## Where the data comes from

Home Assistant, with one small exception. The app subscribes to the `device_tracker`, `person` and `zone` entities of
Home Assistant's own Life360 integration and of the companion apps, and reads vehicle entities when you configure them.
It holds no Life360 login or token and does not log in to or call Life360's API. The exception is member pictures: when
the picture Home Assistant reports for a member is an HTTPS address on `life360.com`, the app downloads it without
credentials and caches it under `/data/cache/avatars`. That happens when a member's `avatar` option is `life360`, and in
the default `auto` mode when the person has no picture of their own in Home Assistant. Set `avatar` to `none` to show
initials instead. The app has no access to Home Assistant's configuration files: it talks to Home Assistant only through
the Supervisor.

## What is stored and where

- Positions, trips and vehicle samples are stored in `/data/realm.db` (SQLite) and are included in backups. Positions
  are kept for 120 days by default (option `retention_fix_days`).
- Avatar pictures are cached under `/data/cache/avatars`, which is excluded from backups. Home Assistant's frontend may
  also cache them in your browser for the same user.
- Nothing leaves your Home Assistant box except two things. Map tiles and style files, which your browser (not the app)
  fetches from public map servers (OpenFreeMap and, for the satellite style, the USGS National Map): those servers see
  your IP address and the area you are viewing, not who is on the map. And the member pictures described above, which
  the app downloads from `life360.com` without credentials.
- No analytics, no telemetry, no geocoding and no update checks. Ingress is the only door to the app: it publishes no
  port. There is no cloud service behind it.
- Driving statistics are computed when a trip closes. If you change a `trips_*`, `driving_speeding_*` or
  `driving_phone_*` option, the change applies to trips that close from then on; earlier trips keep the numbers they
  were stored with.

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
  | `vehicle_sensor_stale` | A vehicle's sensors have not updated recently. |

- **Banners.** A banner appears when the live connection to Home Assistant is lost, or when every Life360 tracker is
  unavailable. It clears by itself when data returns.
- **"Stale" or "offline".** A person is shown as stale after `ui_stale_after_minutes` without a new position (never
  sooner than their source's normal update interval allows) and as offline after `ui_offline_after_hours`.
  `diagnostics.json` lists the effective stale threshold per member.
- **The Back gesture.** If the Android Back gesture misbehaves inside the Home Assistant app, switch the option
  `ui_history_tokens` off in the **Configuration** tab and restart the app. Back then leaves the app at once, while Esc
  and the close button still work.
- **The sidebar entry is missing.** Check that **Show in sidebar** is on for the app (see Setup).

## Trademarks

HA Cartographer is an independent, unofficial project. It is not affiliated with, endorsed by or sponsored by Life360, Inc.,
the Open Home Foundation or Nabu Casa. Life360 is a trademark of Life360, Inc. Home Assistant names and logos belong
to their owners. These names are used only to say what the app works with.
