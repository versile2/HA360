-- 0.2.0 (D113, D114): who is on the map is stored here, not in the add-on options; and the FordPass vehicle samples are gone.
-- vehicle_drives stays: trips.vehicle_drive_id still references it (always NULL).
DROP TABLE IF EXISTS vehicle_samples;

CREATE TABLE roster (
  entity_id    TEXT PRIMARY KEY,                   -- person.alden | device_tracker.pixel_8
  kind         TEXT NOT NULL CHECK (kind IN ('person','tracker')),
  grp          TEXT NOT NULL CHECK (grp IN ('people','vehicles','not_tracked')),
  display_name TEXT NOT NULL,
  lore_title   TEXT NULL,
  color        TEXT NOT NULL,                      -- #RRGGBB
  sort_order   INTEGER NOT NULL,                   -- inside the group, lowest first
  source       TEXT NOT NULL,                      -- 'Home Assistant' | 'Life360' | 'Home Assistant + Life360'
  first_seen   INTEGER NOT NULL,                   -- ms
  last_active  INTEGER NOT NULL,                   -- ms: the last discovery at which it reported a usable state
  auto_moved_at INTEGER NULL                       -- ms: set when the lifecycle rules moved it to not_tracked
);
