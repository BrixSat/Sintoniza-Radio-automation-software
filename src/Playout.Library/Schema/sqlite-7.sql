-- Sintoniza library, schema version 7 (SQLite): events on a date. Besides its weekly times (schedule_slots), an event
-- can play once at a date and time: e.g. another station's stream put in a day's playlist at 14:00. Same table as
-- mysql-7.sql. Times are local 'yyyy-MM-ddTHH:mm:ss'.

CREATE TABLE IF NOT EXISTS schedule_dates (
    entry_id TEXT NOT NULL REFERENCES schedule_entries(id) ON DELETE CASCADE,
    at       TEXT NOT NULL,
    PRIMARY KEY (entry_id, at)
);

UPDATE schema_info SET value = '7' WHERE name = 'version';
