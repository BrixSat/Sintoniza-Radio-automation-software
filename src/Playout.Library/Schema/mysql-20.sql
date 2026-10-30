-- Sintoniza library, schema version 20 (MySQL / MariaDB): songs kept off the website (see sqlite-20.sql).

ALTER TABLE tracks ADD COLUMN not_for_web TINYINT NOT NULL DEFAULT 0;

UPDATE schema_info SET value = '20' WHERE name = 'version';
