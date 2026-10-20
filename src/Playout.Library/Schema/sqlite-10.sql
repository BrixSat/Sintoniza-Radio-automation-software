-- Sintoniza library, schema version 10 (SQLite): voice tracks in the daily playlists. A plan item of kind 'voice' is
-- the host's talk recorded ahead of time (Sintoniza Pro → Daily playlists): it can start some seconds before
-- the item before it ends, over its ending (overlap), and belongs to it (follows = 1: removed, moved and skipped with
-- it). Same columns as mysql-10.sql.

ALTER TABLE plan_items ADD COLUMN overlap REAL    NOT NULL DEFAULT 0; -- seconds it starts before the item before ends
ALTER TABLE plan_items ADD COLUMN follows INTEGER NOT NULL DEFAULT 0; -- 1: belongs to the item before it

UPDATE schema_info SET value = '10' WHERE name = 'version';
