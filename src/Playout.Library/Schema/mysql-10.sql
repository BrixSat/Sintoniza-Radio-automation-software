-- Sintoniza library, schema version 10 (MySQL / MariaDB): voice tracks in the daily playlists (see sqlite-10.sql).
-- Same columns as sqlite-10.sql.

ALTER TABLE plan_items ADD COLUMN overlap DOUBLE NOT NULL DEFAULT 0;
ALTER TABLE plan_items ADD COLUMN follows TINYINT NOT NULL DEFAULT 0;

UPDATE schema_info SET value = '10' WHERE name = 'version';
