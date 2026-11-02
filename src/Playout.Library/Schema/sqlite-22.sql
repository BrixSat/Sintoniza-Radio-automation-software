-- Sintoniza library, schema version 22 (SQLite): seasons. A season (summer, Christmas) runs between two days of the
-- year, every year, and may run over the new year (15 Dec to 6 Jan). An hour template of a season plays at its hours
-- only on those days, and then instead of the all-year template placed at the same hour. Same as mysql-22.sql.

CREATE TABLE IF NOT EXISTS seasons (
    id         TEXT    PRIMARY KEY,
    station_id INTEGER NOT NULL REFERENCES stations(id) ON DELETE CASCADE,
    position   INTEGER NOT NULL DEFAULT 0,
    name       TEXT    NOT NULL DEFAULT '',
    starts_on  TEXT    NOT NULL,   -- 'MM-dd': the first day, every year
    ends_on    TEXT    NOT NULL    -- 'MM-dd': the last day (before starts_on: it runs over the new year)
);

ALTER TABLE schedule_entries ADD COLUMN season_id TEXT NULL;   -- hour templates: their season (NULL: all year)

UPDATE schema_info SET value = '22' WHERE name = 'version';
