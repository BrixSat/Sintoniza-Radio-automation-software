-- Sintoniza library, schema version 18 (MySQL / MariaDB): advertising, a client's sector and a break's longest
-- length (see sqlite-18.sql).

ALTER TABLE ad_clients ADD COLUMN sector VARCHAR(100) NOT NULL DEFAULT '';

ALTER TABLE schedule_entries ADD COLUMN max_seconds DOUBLE NOT NULL DEFAULT 0;

UPDATE schema_info SET value = '18' WHERE name = 'version';
