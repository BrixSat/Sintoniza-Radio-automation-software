-- Sintoniza library, schema version 21 (SQLite): series. An event can play a different file on each day of the week
-- (a horoscope Monday to Sunday) and on given dates (an episode on its day): the file for the date first, then the
-- one for the weekday, else the event's own path. Same table as mysql-21.sql. Dates are local 'yyyy-MM-dd'.

CREATE TABLE IF NOT EXISTS event_series (
    id       INTEGER PRIMARY KEY,
    entry_id TEXT    NOT NULL REFERENCES schedule_entries(id) ON DELETE CASCADE,
    weekday  INTEGER NULL,                -- 0 Sunday … 6 Saturday: every week on this day…
    date     TEXT    NULL,                -- …or only on this date
    path     TEXT    NOT NULL DEFAULT ''  -- a file, a folder (its newest file) or a stream, as the event's path
);
CREATE INDEX IF NOT EXISTS event_series_entry ON event_series (entry_id);

UPDATE schema_info SET value = '21' WHERE name = 'version';
