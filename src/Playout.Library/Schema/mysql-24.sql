-- Sintoniza library, schema version 24 (MySQL / MariaDB): rights per station (see sqlite-24.sql).

ALTER TABLE users ADD COLUMN stations VARCHAR(500) NOT NULL DEFAULT '';

UPDATE schema_info SET value = '24' WHERE name = 'version';
