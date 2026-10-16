-- Sintoniza library, schema version 4 (SQLite): the station's schedule and traffic, as the schedule program edits them
-- (events, ad breaks and their running order, smart playlists, clients and campaigns), the folders of each category,
-- and the as-run log. Same tables as mysql-4.sql. Runs in one transaction: all of it or nothing. The empty tables of
-- the earlier log-based design are removed by the app after this script, if they are still empty.
-- Times of day 'HH:mm:ss'; dates 'yyyy-MM-dd'; local date-times 'yyyy-MM-ddTHH:mm:ss'.

CREATE TABLE IF NOT EXISTS schedule_entries (
    id                TEXT    NOT NULL PRIMARY KEY,
    station_id        INTEGER NOT NULL REFERENCES stations(id) ON DELETE CASCADE,
    type              TEXT    NOT NULL,                 -- event | break | playlist
    position          INTEGER NOT NULL DEFAULT 0,
    name              TEXT    NOT NULL DEFAULT '',
    enabled           INTEGER NOT NULL DEFAULT 1,
    start_mode        TEXT    NOT NULL DEFAULT 'Soft',   -- Soft | Hard | EndAt
    max_delay_minutes REAL    NOT NULL DEFAULT 0,
    lock_level        TEXT    NOT NULL DEFAULT 'Free',   -- Free | Movable | Locked
    path              TEXT    NOT NULL DEFAULT '',
    stream_minutes    REAL    NOT NULL DEFAULT 0,
    max_age_minutes   INTEGER NOT NULL DEFAULT 0
);
CREATE INDEX IF NOT EXISTS schedule_entries_station ON schedule_entries (station_id, type, position);

CREATE TABLE IF NOT EXISTS schedule_slots (
    entry_id TEXT    NOT NULL REFERENCES schedule_entries(id) ON DELETE CASCADE,
    weekday  INTEGER NOT NULL,                           -- 0 = Sunday … 6 = Saturday
    time     TEXT    NOT NULL,
    PRIMARY KEY (entry_id, weekday, time)
);

CREATE TABLE IF NOT EXISTS break_parts (
    entry_id          TEXT    NOT NULL REFERENCES schedule_entries(id) ON DELETE CASCADE,
    position          INTEGER NOT NULL,
    kind              TEXT    NOT NULL,                  -- Jingle | Section
    path              TEXT    NOT NULL DEFAULT '',
    section           TEXT    NOT NULL DEFAULT '',
    only_with_adverts INTEGER NOT NULL DEFAULT 0,
    PRIMARY KEY (entry_id, position)
);

CREATE TABLE IF NOT EXISTS playlist_lines (
    entry_id      TEXT    NOT NULL REFERENCES schedule_entries(id) ON DELETE CASCADE,
    position      INTEGER NOT NULL,
    source        TEXT    NOT NULL,                      -- Category | Folder
    category_id   INTEGER NULL,
    category_name TEXT    NOT NULL DEFAULT '',
    kind          TEXT    NOT NULL DEFAULT 'music',
    folder        TEXT    NOT NULL DEFAULT '',
    PRIMARY KEY (entry_id, position)
);

CREATE TABLE IF NOT EXISTS ad_clients (
    id         TEXT    NOT NULL PRIMARY KEY,
    station_id INTEGER NOT NULL REFERENCES stations(id) ON DELETE CASCADE,
    position   INTEGER NOT NULL DEFAULT 0,
    name       TEXT    NOT NULL DEFAULT '',
    enabled    INTEGER NOT NULL DEFAULT 1
);

CREATE TABLE IF NOT EXISTS ad_campaigns (
    id        TEXT    NOT NULL PRIMARY KEY,
    client_id TEXT    NOT NULL REFERENCES ad_clients(id) ON DELETE CASCADE,
    position  INTEGER NOT NULL DEFAULT 0,
    name      TEXT    NOT NULL DEFAULT '',
    enabled   INTEGER NOT NULL DEFAULT 1,
    starts_on TEXT    NULL,
    ends_on   TEXT    NULL,
    section   TEXT    NOT NULL DEFAULT '',
    place     INTEGER NOT NULL DEFAULT 0                 -- 0 automatic, 1-5 from the start, 6-10 at the end
);

CREATE TABLE IF NOT EXISTS ad_campaign_files (
    campaign_id TEXT    NOT NULL REFERENCES ad_campaigns(id) ON DELETE CASCADE,
    position    INTEGER NOT NULL,
    path        TEXT    NOT NULL,
    PRIMARY KEY (campaign_id, position)
);

CREATE TABLE IF NOT EXISTS ad_placements (
    id          INTEGER PRIMARY KEY,
    campaign_id TEXT    NOT NULL REFERENCES ad_campaigns(id) ON DELETE CASCADE,
    weekday     INTEGER NULL,                            -- every week on this day…
    date        TEXT    NULL,                            -- …or only on this date
    time        TEXT    NOT NULL
);
CREATE INDEX IF NOT EXISTS ad_placements_campaign ON ad_placements (campaign_id);

CREATE TABLE IF NOT EXISTS schedule_state (
    entry_id     TEXT NOT NULL PRIMARY KEY,               -- not a foreign key: kept when the schedule is saved again
    last_started TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS schedule_versions (
    station_id INTEGER NOT NULL PRIMARY KEY REFERENCES stations(id) ON DELETE CASCADE,
    version    INTEGER NOT NULL DEFAULT 0,
    updated_at TEXT    NOT NULL,
    updated_by TEXT    NOT NULL DEFAULT ''
);

CREATE TABLE IF NOT EXISTS category_folders (
    path        TEXT    NOT NULL PRIMARY KEY,
    category_id INTEGER NOT NULL REFERENCES categories(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS as_run (
    id         INTEGER PRIMARY KEY,
    station_id INTEGER NOT NULL,
    started_at TEXT    NOT NULL,
    ended_at   TEXT    NULL,
    type       TEXT    NOT NULL DEFAULT '',
    source     TEXT    NOT NULL DEFAULT '',
    event      TEXT    NOT NULL DEFAULT '',
    advertiser TEXT    NOT NULL DEFAULT '',
    artist     TEXT    NOT NULL DEFAULT '',
    title      TEXT    NOT NULL DEFAULT '',
    album      TEXT    NOT NULL DEFAULT '',
    composer   TEXT    NOT NULL DEFAULT '',
    label      TEXT    NOT NULL DEFAULT '',
    isrc       TEXT    NOT NULL DEFAULT '',
    year       TEXT    NOT NULL DEFAULT '',
    file       TEXT    NOT NULL DEFAULT '',
    UNIQUE (station_id, started_at, file)
);
CREATE INDEX IF NOT EXISTS as_run_advertiser ON as_run (advertiser, started_at);

UPDATE schema_info SET value = '4' WHERE name = 'version';
