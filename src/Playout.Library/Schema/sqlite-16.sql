-- Sintoniza library, schema version 16 (SQLite): the studios (computers running Sintoniza Emi) of each station, and
-- which of them is on air. Every studio writes seen_at (UTC, the database's clock) every few seconds while it runs;
-- on_air = 1 on one of them: that one plays, the others are standby (ready, silent) and take over when it stops
-- answering. playing: what is on air there (for Sintoniza Pro); version: its Sintoniza Emi. Same columns as
-- mysql-16.sql.

CREATE TABLE IF NOT EXISTS studios (
    station_id INTEGER NOT NULL,
    computer   TEXT    NOT NULL,
    seen_at    TEXT    NOT NULL,
    on_air     INTEGER NOT NULL DEFAULT 0,
    since      TEXT    NULL,
    playing    TEXT    NOT NULL DEFAULT '',
    version    TEXT    NOT NULL DEFAULT '',
    PRIMARY KEY (station_id, computer)
);

UPDATE schema_info SET value = '16' WHERE name = 'version';
