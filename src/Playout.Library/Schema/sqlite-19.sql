-- Sintoniza library, schema version 19 (SQLite): live shows. A schedule entry of type 'show' is a presenter on air
-- for show_minutes from each of its times (schedule_slots, schedule_dates): the playout goes ASSISTED for it and back
-- to AUTO after; presenter and notes are for RDS, the website and the presenter view. Same columns as mysql-19.sql.

ALTER TABLE schedule_entries ADD COLUMN presenter    TEXT NOT NULL DEFAULT '';      -- shows
ALTER TABLE schedule_entries ADD COLUMN show_minutes REAL NOT NULL DEFAULT 0;       -- shows: how long, from each time
ALTER TABLE schedule_entries ADD COLUMN notes        TEXT NOT NULL DEFAULT '';      -- shows: for the presenter

UPDATE schema_info SET value = '19' WHERE name = 'version';
