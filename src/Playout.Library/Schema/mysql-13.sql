-- Sintoniza library, schema version 13 (MySQL / MariaDB): what the ERC needs about each song (see sqlite-13.sql).
-- Same columns as sqlite-13.sql.

ALTER TABLE tracks ADD COLUMN erc_portuguese TINYINT NULL;
ALTER TABLE tracks ADD COLUMN erc_nationality VARCHAR(10) NULL;
ALTER TABLE tracks ADD COLUMN erc_release VARCHAR(10) NULL;
ALTER TABLE categories ADD COLUMN erc_portuguese TINYINT NULL;
ALTER TABLE categories ADD COLUMN erc_language VARCHAR(10) NULL;
ALTER TABLE categories ADD COLUMN erc_nationality VARCHAR(10) NULL;

UPDATE schema_info SET value = '13' WHERE name = 'version';
