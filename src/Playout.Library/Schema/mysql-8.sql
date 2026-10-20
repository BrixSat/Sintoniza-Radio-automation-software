-- Sintoniza library, schema version 8 (MySQL / MariaDB): music rules. A song's energy (1 calm … 5 lively), besides
-- its tempo (1 slow … 5 fast). An hour template's line can ask for a tempo and energy range; a template can keep an
-- artist and an album apart for some hours and limit slow songs in a row. Same columns as sqlite-8.sql.

ALTER TABLE tracks ADD COLUMN energy TINYINT NULL;

ALTER TABLE playlist_lines ADD COLUMN tempo_min TINYINT NULL;
ALTER TABLE playlist_lines ADD COLUMN tempo_max TINYINT NULL;
ALTER TABLE playlist_lines ADD COLUMN energy_min TINYINT NULL;
ALTER TABLE playlist_lines ADD COLUMN energy_max TINYINT NULL;

ALTER TABLE schedule_entries ADD COLUMN artist_hours DOUBLE NOT NULL DEFAULT 0;
ALTER TABLE schedule_entries ADD COLUMN album_hours DOUBLE NOT NULL DEFAULT 0;
ALTER TABLE schedule_entries ADD COLUMN max_slow_in_row INT NOT NULL DEFAULT 0;

UPDATE schema_info SET value = '8' WHERE name = 'version';
