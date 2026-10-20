-- Sintoniza music library, schema version 2 (SQLite): cue points in the library, so studios sharing it share them.
-- Documented in docs/library-database.md. Runs in one transaction: all of it or nothing.

ALTER TABLE tracks ADD COLUMN cue_in REAL NULL;          -- seconds from the start of the file; null = not set
ALTER TABLE tracks ADD COLUMN intro REAL NULL;
ALTER TABLE tracks ADD COLUMN outro REAL NULL;
ALTER TABLE tracks ADD COLUMN mix REAL NULL;
ALTER TABLE tracks ADD COLUMN cue_out REAL NULL;
ALTER TABLE tracks ADD COLUMN cues_updated_at TEXT NULL; -- when the cue points were last set or cleared; null = never
CREATE INDEX IF NOT EXISTS tracks_cues_updated ON tracks (cues_updated_at);

UPDATE schema_info SET value = '2' WHERE name = 'version';
