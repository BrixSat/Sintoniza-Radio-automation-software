-- Sintoniza library, schema version 8 (SQLite): music rules. A song's energy (1 calm … 5 lively), besides its tempo
-- (1 slow … 5 fast). An hour template's line can ask for a tempo and energy range; a template can keep an artist and
-- an album apart for some hours and limit slow songs in a row. Same columns as mysql-8.sql.

ALTER TABLE tracks ADD COLUMN energy INTEGER NULL;                                -- 1 calm … 5 lively

ALTER TABLE playlist_lines ADD COLUMN tempo_min  INTEGER NULL;                    -- null: any
ALTER TABLE playlist_lines ADD COLUMN tempo_max  INTEGER NULL;
ALTER TABLE playlist_lines ADD COLUMN energy_min INTEGER NULL;
ALTER TABLE playlist_lines ADD COLUMN energy_max INTEGER NULL;

ALTER TABLE schedule_entries ADD COLUMN artist_hours     REAL    NOT NULL DEFAULT 0; -- templates: 0 = the playout's setting
ALTER TABLE schedule_entries ADD COLUMN album_hours      REAL    NOT NULL DEFAULT 0; -- templates: 0 = no album rule
ALTER TABLE schedule_entries ADD COLUMN max_slow_in_row  INTEGER NOT NULL DEFAULT 0; -- templates: tempo 1–2 songs in a row; 0 = any

UPDATE schema_info SET value = '8' WHERE name = 'version';
