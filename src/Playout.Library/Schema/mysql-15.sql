-- Sintoniza library, schema version 15 (MySQL / MariaDB): notes for the presenter in the daily playlists (see
-- sqlite-15.sql). Same columns as sqlite-15.sql.

ALTER TABLE plan_hours ADD COLUMN note TEXT NULL;
ALTER TABLE plan_items ADD COLUMN note TEXT NULL;

UPDATE schema_info SET value = '15' WHERE name = 'version';
