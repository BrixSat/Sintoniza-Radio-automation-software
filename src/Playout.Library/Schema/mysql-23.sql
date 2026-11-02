-- Sintoniza library, schema version 23 (MySQL / MariaDB): a soft event or ad break may start early (see sqlite-23.sql).

ALTER TABLE schedule_entries ADD COLUMN max_early_minutes DOUBLE NOT NULL DEFAULT 0;

UPDATE schema_info SET value = '23' WHERE name = 'version';
