-- Sintoniza library, schema version 14 (MySQL / MariaDB): when a song may be picked automatically (see
-- sqlite-14.sql). Same columns as sqlite-14.sql.

ALTER TABLE tracks ADD COLUMN play_from CHAR(10) NULL;
ALTER TABLE tracks ADD COLUMN play_until CHAR(10) NULL;
ALTER TABLE tracks ADD COLUMN play_days CHAR(7) NULL;
ALTER TABLE tracks ADD COLUMN play_hours CHAR(24) NULL;
ALTER TABLE tracks ADD COLUMN max_day INT NULL;
ALTER TABLE tracks ADD COLUMN max_week INT NULL;

UPDATE schema_info SET value = '14' WHERE name = 'version';
