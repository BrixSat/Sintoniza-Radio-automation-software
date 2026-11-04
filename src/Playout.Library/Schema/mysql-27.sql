-- Sintoniza library, schema version 27 (MySQL / MariaDB): a show is live (a presenter in the studio) or automated
-- (see sqlite-27.sql).

ALTER TABLE schedule_entries ADD COLUMN show_live TINYINT NOT NULL DEFAULT 1;

UPDATE schema_info SET value = '27' WHERE name = 'version';
