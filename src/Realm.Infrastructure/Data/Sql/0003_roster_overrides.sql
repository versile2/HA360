-- 0.2.1 (D117): the owner's own name, title, colour and picture are kept apart from what Home Assistant and Life360 say, so a later change at the source never
-- replaces them and "Reset" can bring the source's values back. display_name, lore_title and color stay: they hold the values in effect.
ALTER TABLE roster ADD COLUMN source_name   TEXT NULL;  -- the name the source gives, as of the last discovery
ALTER TABLE roster ADD COLUMN source_title  TEXT NULL;  -- the title the source gives (Home Assistant has none)
ALTER TABLE roster ADD COLUMN source_color  TEXT NULL;  -- the colour it took when it was found
ALTER TABLE roster ADD COLUMN name_override  TEXT NULL; -- the owner's name; NULL: none
ALTER TABLE roster ADD COLUMN title_override TEXT NULL; -- the owner's title; '' means "no title" over a source title
ALTER TABLE roster ADD COLUMN color_override TEXT NULL; -- the owner's colour; NULL: none
ALTER TABLE roster ADD COLUMN icon TEXT NULL;           -- 'photo' | 'initial' | 'glyph:car|truck|person|pet|phone|tag'; NULL: automatic

-- A row of 0.2.0 cannot tell its edits from what the source said: its name and title count as the owner's until the first discovery of 0.2.1 learns the source's name
-- (a name equal to it is then dropped as an override), and its colour is taken as the colour it was found with.
UPDATE roster SET source_name = display_name, name_override = display_name, title_override = lore_title, source_color = color;
