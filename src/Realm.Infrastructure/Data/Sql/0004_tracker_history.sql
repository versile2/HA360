-- 0.3.1 (D125): a tracker stores its positions in `fixes` like a person (member_id = the roster id, e.g. tracker_wagon; source life360|companion already fits the CHECK, and
-- `members` rows are created by the writer), when the owner keeps its history. Nothing changes in `fixes`. The switch lives with the roster row; ON for every existing entry.
ALTER TABLE roster ADD COLUMN keep_history INTEGER NOT NULL DEFAULT 1;  -- 1 = store this entry's positions for Location History (used by trackers); 0 = latest position only
