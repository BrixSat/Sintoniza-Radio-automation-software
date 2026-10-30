-- Sintoniza library, schema version 19 (MySQL / MariaDB): live shows (see sqlite-19.sql).

ALTER TABLE schedule_entries ADD COLUMN presenter VARCHAR(200) NOT NULL DEFAULT '';
ALTER TABLE schedule_entries ADD COLUMN show_minutes DOUBLE NOT NULL DEFAULT 0;
ALTER TABLE schedule_entries ADD COLUMN notes TEXT NULL;

UPDATE schema_info SET value = '19' WHERE name = 'version';
