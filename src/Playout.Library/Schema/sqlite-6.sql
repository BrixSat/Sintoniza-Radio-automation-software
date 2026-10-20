-- Sintoniza library, schema version 6 (SQLite): the daily playlists. Each hour is generated in advance from the hour
-- template placed there (Sintoniza Schedule → Playlists): the exact songs and jingles, in order, with how long each
-- plays. The playlist manager shows and edits them; the playout plays them. Ad breaks and events aren't stored here:
-- they come from the schedule (bookings can change up to air time). Same tables as mysql-6.sql.
-- Hours are local times 'yyyy-MM-ddTHH:00:00'; generated_at is UTC.

CREATE TABLE IF NOT EXISTS plan_hours (
    station_id   INTEGER NOT NULL REFERENCES stations(id) ON DELETE CASCADE,
    hour_start   TEXT    NOT NULL,
    template_id  TEXT    NULL,                    -- the hour template it was made from; null: none placed there
    signature    TEXT    NOT NULL DEFAULT '',     -- that template as it was (its lines): changed → made again
    generated_at TEXT    NOT NULL,
    generated_by TEXT    NOT NULL DEFAULT '',     -- the computer that made it
    edited       INTEGER NOT NULL DEFAULT 0,      -- 1: changed by hand, never made again by itself
    PRIMARY KEY (station_id, hour_start)
);

CREATE TABLE IF NOT EXISTS plan_items (
    id          TEXT    NOT NULL PRIMARY KEY,
    station_id  INTEGER NOT NULL,
    hour_start  TEXT    NOT NULL,
    position    INTEGER NOT NULL,
    kind        TEXT    NOT NULL DEFAULT 'music', -- music | jingle | commercial | programme
    path        TEXT    NOT NULL,
    title       TEXT    NOT NULL DEFAULT '',
    artist      TEXT    NOT NULL DEFAULT '',
    seconds     REAL    NOT NULL DEFAULT 0,       -- how long it plays before the next item starts
    track_id    INTEGER NULL REFERENCES tracks(id) ON DELETE SET NULL,
    line        INTEGER NULL,                     -- the template line it came from (0 = first); null: added by hand
    category_id INTEGER NULL,
    optional    INTEGER NOT NULL DEFAULT 0,       -- 1: at the end of the hour, played only if there is time
    FOREIGN KEY (station_id, hour_start) REFERENCES plan_hours(station_id, hour_start) ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS plan_items_hour ON plan_items (station_id, hour_start, position);

UPDATE schema_info SET value = '6' WHERE name = 'version';
