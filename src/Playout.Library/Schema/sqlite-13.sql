-- Sintoniza library, schema version 13 (SQLite): what the ERC needs about each song (Portuguese music quotas, Lei da
-- Rádio art. 41.º–47.º). Songs: erc_portuguese (1 Portuguese music, 0 not, null: as its categories say),
-- erc_nationality (the performer's country, as the ERC's code, e.g. 045 = Portugal), erc_release (first edition:
-- yyyy-mm-dd, yyyy-mm or yyyy; null: the year tag). The language is tracks.language (pt-PT, pt-BR, en…). Categories:
-- the same as defaults for their songs and the categories under them. Same columns as mysql-13.sql.

ALTER TABLE tracks ADD COLUMN erc_portuguese INTEGER NULL;
ALTER TABLE tracks ADD COLUMN erc_nationality TEXT NULL;
ALTER TABLE tracks ADD COLUMN erc_release TEXT NULL;
ALTER TABLE categories ADD COLUMN erc_portuguese INTEGER NULL;
ALTER TABLE categories ADD COLUMN erc_language TEXT NULL;
ALTER TABLE categories ADD COLUMN erc_nationality TEXT NULL;

UPDATE schema_info SET value = '13' WHERE name = 'version';
