-- Sintoniza library, schema version 30 (MySQL / MariaDB): cue points made by auto cue are marked (see sqlite-30.sql).

ALTER TABLE tracks ADD COLUMN cues_auto TINYINT NOT NULL DEFAULT 0;
ALTER TABLE tracks ADD COLUMN cues_auto_size BIGINT NULL;

UPDATE schema_info SET value = '30' WHERE name = 'version';
