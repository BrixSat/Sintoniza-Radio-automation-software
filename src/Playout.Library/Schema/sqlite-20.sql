-- Sintoniza library, schema version 20 (SQLite): songs kept off the website. A song with not_for_web = 1 plays as
-- usual, but the website, the now-playing files and FTP uploads keep showing what they showed before it (RDS is
-- not affected). Same columns as mysql-20.sql.

ALTER TABLE tracks ADD COLUMN not_for_web INTEGER NOT NULL DEFAULT 0;

UPDATE schema_info SET value = '20' WHERE name = 'version';
