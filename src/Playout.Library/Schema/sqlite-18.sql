-- Sintoniza library, schema version 18 (SQLite): advertising. A client's business sector (two clients of the same
-- sector are kept apart in a break, never back to back), and the longest an ad break may be (Sintoniza Pro warns
-- when the adverts booked make it longer). Same columns as mysql-18.sql.

ALTER TABLE ad_clients ADD COLUMN sector TEXT NOT NULL DEFAULT '';                  -- e.g. Cars; '' = none

ALTER TABLE schedule_entries ADD COLUMN max_seconds REAL NOT NULL DEFAULT 0;        -- breaks: 0 = no limit

UPDATE schema_info SET value = '18' WHERE name = 'version';
