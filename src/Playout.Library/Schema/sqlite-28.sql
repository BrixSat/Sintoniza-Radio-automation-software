-- Sintoniza library, schema version 28 (SQLite): an hour template's targets for its songs ("at least 4 Portuguese
-- songs", "no two 80s in a row"), as JSON: [{"Kind":"AtLeast","Count":4,"CategoryId":12,"CategoryName":"…"}].
-- Same as mysql-28.sql.

ALTER TABLE schedule_entries ADD COLUMN targets TEXT NULL;

UPDATE schema_info SET value = '28' WHERE name = 'version';
