-- Sintoniza library, schema version 28 (MySQL / MariaDB): an hour template's targets (see sqlite-28.sql).

ALTER TABLE schedule_entries ADD COLUMN targets TEXT NULL;

UPDATE schema_info SET value = '28' WHERE name = 'version';
