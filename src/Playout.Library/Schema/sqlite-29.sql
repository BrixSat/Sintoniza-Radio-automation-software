-- Sintoniza library, schema version 29 (SQLite): the studios' logs (each on-air computer sends the lines of its log,
-- the station's time; problems marked) for Sintoniza Pro → Station → Studios, kept 14 days; and what each studio warns
-- about in its setup (on Wi-Fi, on battery…). Same as mysql-29.sql.

CREATE TABLE IF NOT EXISTS studio_log (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    station_id  INTEGER NOT NULL,
    computer    TEXT    NOT NULL,
    at          TEXT    NOT NULL,
    problem     INTEGER NOT NULL DEFAULT 0,
    line        TEXT    NOT NULL
);
CREATE INDEX IF NOT EXISTS studio_log_at ON studio_log (station_id, computer, at);
CREATE INDEX IF NOT EXISTS studio_log_problems ON studio_log (station_id, problem, at);
CREATE INDEX IF NOT EXISTS studio_log_old ON studio_log (at);

ALTER TABLE studios ADD COLUMN warnings TEXT NOT NULL DEFAULT '';

UPDATE schema_info SET value = '29' WHERE name = 'version';
