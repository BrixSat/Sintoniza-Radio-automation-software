-- Sintoniza library, schema version 25 (SQLite): listener counts. The computer on air reads how many listen to the
-- stream (from the Icecast or Shoutcast server) every minute; one row per station and minute (local time,
-- yyyy-MM-ddTHH:mm, as the as-run log). Same as mysql-25.sql.

CREATE TABLE IF NOT EXISTS listeners (
    station_id INTEGER NOT NULL,
    minute     TEXT    NOT NULL,
    listeners  INTEGER NOT NULL,
    PRIMARY KEY (station_id, minute)
);

UPDATE schema_info SET value = '25' WHERE name = 'version';
