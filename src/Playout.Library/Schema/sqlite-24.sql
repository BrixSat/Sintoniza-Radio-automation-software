-- Sintoniza library, schema version 24 (SQLite): rights per station. A user may work on every station of the
-- library (stations = '') or only on some (their ids, comma-separated): logging in to another station is refused.
-- Same as mysql-24.sql.

ALTER TABLE users ADD COLUMN stations TEXT NOT NULL DEFAULT '';

UPDATE schema_info SET value = '24' WHERE name = 'version';
