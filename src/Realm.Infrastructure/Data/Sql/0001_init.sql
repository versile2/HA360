CREATE TABLE meta (key TEXT PRIMARY KEY, value TEXT NOT NULL);
-- keys: schema_version (= PRAGMA user_version), created_utc, derive_hash, clean_shutdown ('1'/'0'), ha_time_zone

CREATE TABLE members (
  id                TEXT PRIMARY KEY,              -- MemberId (2.2)
  life360_id        TEXT NULL,
  recording_start   INTEGER NULL,                  -- earliest fix ts (ms); drives coverage (6.5)
  first_seen_utc    INTEGER NOT NULL,
  last_configured_utc INTEGER NOT NULL
);

CREATE TABLE fixes (                               -- people only (vehicles: vehicle_samples)
  id           INTEGER PRIMARY KEY,
  member_id    TEXT    NOT NULL REFERENCES members(id),
  ts           INTEGER NOT NULL,                   -- fix time (ms)
  source       TEXT    NOT NULL CHECK (source IN ('life360','companion')),
  lat          REAL    NOT NULL,
  lon          REAL    NOT NULL,
  acc_m        REAL    NULL,                       -- NULL = unknown (Life360 placeholder)
  speed_mps    REAL    NULL,
  heading_deg  REAL    NULL,
  alt_m        REAL    NULL,
  battery_pct  INTEGER NULL,
  charging     INTEGER NULL,                       -- 0/1
  driving      INTEGER NULL,                       -- Life360 flag, hint only (5.4)
  address      TEXT    NULL,                       -- stored on every fix that carries one (no delta encoding: order-independent, backfill-safe, R-071)
  track        INTEGER NOT NULL DEFAULT 1,         -- 1 = in the trip track (5.2)
  reason       TEXT    NULL,                       -- why track = 0: acc | spike | priority
  UNIQUE (member_id, ts, source)                   -- idempotent backfill; also the (member, ts) range index
);

CREATE TABLE vehicle_samples (                     -- one row per (vehicle, ts); partial columns merged by UPSERT; v1 records, v1.1 derives from it (5.7, 12.3)
  id           INTEGER PRIMARY KEY,
  vehicle_id   TEXT    NOT NULL,
  ts           INTEGER NOT NULL,
  odo_m        REAL    NULL,                       -- sensor state (mi) × 1609.344; no quantum assumed (1.3, R-063)
  fuel_pct     INTEGER NULL,
  ignition     TEXT    NULL,                       -- raw text lower-cased
  gear         TEXT    NULL,
  speed_mps    REAL    NULL,
  remote_start_s INTEGER NULL,
  lat          REAL    NULL,
  lon          REAL    NULL,
  UNIQUE (vehicle_id, ts)
);

CREATE TABLE signals (                             -- phone screen / lock / activity transitions
  id        INTEGER PRIMARY KEY,
  member_id TEXT    NOT NULL REFERENCES members(id),
  ts        INTEGER NOT NULL,
  kind      TEXT    NOT NULL CHECK (kind IN ('screen','locked','activity','android_auto')),
  value     TEXT    NOT NULL,                      -- on | off | unavailable | <activity>   (R-066: 'android_auto' is on | off | unavailable)
  UNIQUE (member_id, ts, kind)
);

CREATE TABLE vehicle_drives (                      -- created now, EMPTY in v1; written from v1.1 (12.3). Kept so v1.1 needs no schema bump.
  id                INTEGER PRIMARY KEY,
  vehicle_id        TEXT    NOT NULL,
  start_earliest_ts INTEGER NOT NULL,              -- previous reading (uncertain bound)
  start_ts          INTEGER NOT NULL,              -- first reading with higher odometer
  end_ts            INTEGER NOT NULL,              -- last reading with higher odometer
  end_latest_ts     INTEGER NOT NULL,              -- next reading (uncertain bound)
  odo_start_m       REAL    NOT NULL,
  odo_end_m         REAL    NOT NULL,
  distance_m        REAL    NOT NULL,
  fuel_start_pct    INTEGER NULL,  fuel_end_pct INTEGER NULL,
  pos_before_lat    REAL NULL, pos_before_lon REAL NULL, pos_after_lat REAL NULL, pos_after_lon REAL NULL,
  ign_unseen        INTEGER NOT NULL DEFAULT 0,
  UNIQUE (vehicle_id, start_ts)
);

CREATE TABLE trips (
  id               INTEGER PRIMARY KEY,
  member_id        TEXT    NOT NULL REFERENCES members(id),
  start_ts         INTEGER NOT NULL,   end_ts INTEGER NOT NULL,   duration_s INTEGER NOT NULL,
  start_lat        REAL NOT NULL, start_lon REAL NOT NULL, end_lat REAL NOT NULL, end_lon REAL NOT NULL,
  start_place_id   TEXT NULL,     end_place_id TEXT NULL,
  start_street     TEXT NULL,     end_street   TEXT NULL,
  distance_m       REAL NOT NULL,                  -- reported: = distance_gps_m in v1; odometer-scaled from v1.1
  distance_gps_m   REAL NOT NULL,
  distance_source  TEXT NOT NULL CHECK (distance_source IN ('gps','odometer')),   -- always 'gps' in v1
  vehicle_id       TEXT NULL,                      -- NULL in v1
  vehicle_drive_id INTEGER NULL REFERENCES vehicle_drives(id),                    -- NULL in v1
  top_speed_mps    REAL NULL,  top_speed_ts INTEGER NULL,  top_speed_street TEXT NULL,
  speeding_count   INTEGER NULL,                   -- NULL = unknown, 0 = measured zero
  phone_count      INTEGER NULL,
  accel_count      INTEGER NULL,                   -- always NULL in production (D20)
  braking_count    INTEGER NULL,                   -- always NULL in production (D20)
  quality          TEXT NOT NULL CHECK (quality IN ('dense','coarse')),
  has_gap          INTEGER NOT NULL DEFAULT 0,
  ended_by         TEXT NOT NULL CHECK (ended_by IN ('stop','nofix','coarse')),
  source_mask      TEXT NOT NULL,                  -- e.g. 'companion,life360'
  algo_version     INTEGER NOT NULL,
  derive_hash      TEXT NOT NULL,                  -- hash of the options that affect derived counts (7.7)
  created_ts       INTEGER NOT NULL,
  UNIQUE (member_id, start_ts)
);
CREATE INDEX ix_trips_start ON trips(start_ts);

CREATE TABLE trip_events (
  id       INTEGER PRIMARY KEY,
  trip_id  INTEGER NOT NULL REFERENCES trips(id) ON DELETE CASCADE,
  kind     TEXT    NOT NULL CHECK (kind IN ('speeding','phone')),
  start_ts INTEGER NOT NULL,  end_ts INTEGER NOT NULL,
  peak     REAL NULL,         -- speeding: peak m/s; phone: seconds
  lat      REAL NULL,         lon REAL NULL
);
CREATE INDEX ix_trip_events_trip ON trip_events(trip_id);

CREATE TABLE job_state (        -- backfill cursors, prune
  name        TEXT PRIMARY KEY,   -- 'backfill.ha:<entity>', 'prune', 'zones'  (v1.1 adds 'backfill.life360:<member>', 'recompute')
  cursor      TEXT NULL,          -- ISO-8601 UTC of the next chunk start
  last_run_ts INTEGER NULL, ok INTEGER NULL, detail TEXT NULL
);
