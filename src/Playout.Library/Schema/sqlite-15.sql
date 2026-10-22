-- Sintoniza library, schema version 15 (SQLite): notes for the presenter in the daily playlists. plan_hours.note: about
-- the whole hour; plan_items.note: about one item (what to say, the sponsor to mention). Shown on air before and while
-- it plays; null: none. Same columns as mysql-15.sql.

ALTER TABLE plan_hours ADD COLUMN note TEXT NULL;
ALTER TABLE plan_items ADD COLUMN note TEXT NULL;

UPDATE schema_info SET value = '15' WHERE name = 'version';
