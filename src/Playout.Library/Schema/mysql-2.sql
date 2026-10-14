-- Sintoniza music library, schema version 2 (MySQL / MariaDB): cue points in the library, so studios sharing it
-- share them. Documented in docs/library-database.md.
-- Safe to run again: MySQL commits each change as it goes, so a column that is already there is skipped.

ALTER TABLE tracks ADD COLUMN cue_in DOUBLE NULL;
ALTER TABLE tracks ADD COLUMN intro DOUBLE NULL;
ALTER TABLE tracks ADD COLUMN outro DOUBLE NULL;
ALTER TABLE tracks ADD COLUMN mix DOUBLE NULL;
ALTER TABLE tracks ADD COLUMN cue_out DOUBLE NULL;
ALTER TABLE tracks ADD COLUMN cues_updated_at CHAR(20) NULL;
CREATE INDEX tracks_cues_updated ON tracks (cues_updated_at);

UPDATE schema_info SET value = '2' WHERE name = 'version';
