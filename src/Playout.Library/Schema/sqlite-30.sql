-- Sintoniza library, schema version 30 (SQLite): cue points made by auto cue (from the sound, on an on-air computer)
-- are marked, so a station with auto cue off leaves them out, a hand edit replaces them, and auto cue never writes
-- over cue points set by hand. Same as mysql-30.sql.

ALTER TABLE tracks ADD COLUMN cues_auto INTEGER NOT NULL DEFAULT 0;
ALTER TABLE tracks ADD COLUMN cues_auto_size INTEGER NULL; -- the file's size (bytes) auto cue's were made for (another: made again)

UPDATE schema_info SET value = '30' WHERE name = 'version';
