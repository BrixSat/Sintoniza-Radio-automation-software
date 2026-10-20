-- Sintoniza library, schema version 3 (SQLite): scheduling. Stations, clocks (hour plans), rotation rules,
-- commercial traffic (advertisers, campaigns, bookings) and the daily logs the playout plays.
-- Replaced by the schedule tables of version 4 (their empty tables are removed then). Runs in one transaction.
-- Dates are local to the station, 'yyyy-MM-dd'; times of day 'HH:mm:ss'; lengths and offsets in seconds.

CREATE TABLE IF NOT EXISTS stations (
    id        INTEGER PRIMARY KEY,
    name      TEXT    NOT NULL UNIQUE,
    time_zone TEXT    NOT NULL DEFAULT '',   -- IANA name, e.g. 'Europe/Lisbon'; '' = the computer's own
    active    INTEGER NOT NULL DEFAULT 1
);

-- Per station, name and value: days_ahead, make_at and the default rest times (see the design document).
CREATE TABLE IF NOT EXISTS schedule_settings (
    station_id INTEGER NOT NULL REFERENCES stations(id) ON DELETE CASCADE,
    name       TEXT    NOT NULL,
    value      TEXT    NOT NULL,
    PRIMARY KEY (station_id, name)
);

-- The plan of an hour.
CREATE TABLE IF NOT EXISTS clocks (
    id         INTEGER PRIMARY KEY,
    station_id INTEGER NOT NULL REFERENCES stations(id) ON DELETE CASCADE,
    name       TEXT    NOT NULL,
    color      TEXT    NULL,
    active     INTEGER NOT NULL DEFAULT 1
);

-- The parts of a clock, in order.
CREATE TABLE IF NOT EXISTS clock_slots (
    id             INTEGER PRIMARY KEY,
    clock_id       INTEGER NOT NULL REFERENCES clocks(id) ON DELETE CASCADE,
    position       INTEGER NOT NULL,
    kind           TEXT    NOT NULL,                  -- pick | item | break | event | voice
    category_id    INTEGER NULL REFERENCES categories(id) ON DELETE SET NULL,  -- pick: from this category
    track_id       INTEGER NULL REFERENCES tracks(id) ON DELETE SET NULL,      -- item: this track
    source         TEXT    NULL,                      -- event: file, folder (its newest file) or stream URL
    name           TEXT    NULL,                      -- label in the playlist and reports
    at_seconds     REAL    NULL,                      -- when in the hour it is due (0-3599); null = after the one before
    timing         TEXT    NOT NULL DEFAULT 'none',   -- none | soft | hard | end_at (at_seconds is when it ends)
    max_delay      REAL    NULL,                      -- soft: at most this many seconds late
    lock_level     TEXT    NOT NULL DEFAULT 'free',   -- free | movable | locked
    max_seconds    REAL    NULL,                      -- break: longest it may run; event stream: how long it plays
    intro_track_id INTEGER NULL REFERENCES tracks(id) ON DELETE SET NULL,      -- break: opening jingle
    outro_track_id INTEGER NULL REFERENCES tracks(id) ON DELETE SET NULL       -- break: closing jingle
);
CREATE INDEX IF NOT EXISTS clock_slots_clock ON clock_slots (clock_id, position);

-- Which clock plays each hour of the week (weekday 0 = Sunday).
CREATE TABLE IF NOT EXISTS clock_grid (
    station_id INTEGER NOT NULL REFERENCES stations(id) ON DELETE CASCADE,
    weekday    INTEGER NOT NULL,
    hour       INTEGER NOT NULL,
    clock_id   INTEGER NOT NULL REFERENCES clocks(id) ON DELETE CASCADE,
    PRIMARY KEY (station_id, weekday, hour)
);

-- Particular dates (holidays, special programmes): wins over the grid.
CREATE TABLE IF NOT EXISTS clock_overrides (
    station_id INTEGER NOT NULL REFERENCES stations(id) ON DELETE CASCADE,
    date       TEXT    NOT NULL,
    hour       INTEGER NOT NULL,
    clock_id   INTEGER NOT NULL REFERENCES clocks(id) ON DELETE CASCADE,
    PRIMARY KEY (station_id, date, hour)
);

-- Rotation rules for one category; null = the station's default (schedule_settings).
CREATE TABLE IF NOT EXISTS rotation_rules (
    id                  INTEGER PRIMARY KEY,
    station_id          INTEGER NOT NULL REFERENCES stations(id) ON DELETE CASCADE,
    category_id         INTEGER NOT NULL REFERENCES categories(id) ON DELETE CASCADE,
    song_rest_minutes   INTEGER NULL,
    artist_rest_minutes INTEGER NULL,
    title_rest_minutes  INTEGER NULL,
    max_per_hour        INTEGER NULL,
    tempo_flow          INTEGER NOT NULL DEFAULT 0,   -- 1: not two slow songs (tempo 1-2) in a row
    voice_flow          INTEGER NOT NULL DEFAULT 0,   -- 1: not two songs of the same voice in a row
    UNIQUE (station_id, category_id)
);

-- Shared by the stations (one company may sell on all of them).
CREATE TABLE IF NOT EXISTS advertisers (
    id          INTEGER PRIMARY KEY,
    name        TEXT    NOT NULL,
    competition TEXT    NULL,       -- advertisers with the same code are kept out of the same break
    contact     TEXT    NULL,
    notes       TEXT    NULL,
    active      INTEGER NOT NULL DEFAULT 1
);

CREATE TABLE IF NOT EXISTS campaigns (
    id            INTEGER PRIMARY KEY,
    station_id    INTEGER NOT NULL REFERENCES stations(id) ON DELETE CASCADE,
    advertiser_id INTEGER NOT NULL REFERENCES advertisers(id),
    name          TEXT    NOT NULL,
    starts_on     TEXT    NOT NULL,
    ends_on       TEXT    NOT NULL,
    status        TEXT    NOT NULL DEFAULT 'booked',   -- booked | paused | cancelled
    notes         TEXT    NULL,
    created_at    TEXT    NOT NULL,
    updated_at    TEXT    NOT NULL
);
CREATE INDEX IF NOT EXISTS campaigns_dates ON campaigns (station_id, starts_on, ends_on);

-- The audio of a campaign: versions rotate by weight.
CREATE TABLE IF NOT EXISTS campaign_spots (
    id          INTEGER PRIMARY KEY,
    campaign_id INTEGER NOT NULL REFERENCES campaigns(id) ON DELETE CASCADE,
    track_id    INTEGER NOT NULL REFERENCES tracks(id),
    weight      INTEGER NOT NULL DEFAULT 1,
    starts_on   TEXT    NULL,
    ends_on     TEXT    NULL
);

-- How a campaign is placed: so many a day, in a time band and/or in breaks with a name.
CREATE TABLE IF NOT EXISTS bookings (
    id          INTEGER PRIMARY KEY,
    campaign_id INTEGER NOT NULL REFERENCES campaigns(id) ON DELETE CASCADE,
    days        TEXT    NOT NULL DEFAULT '0123456',   -- weekdays, 0 = Sunday
    per_day     INTEGER NOT NULL DEFAULT 1,
    from_time   TEXT    NOT NULL DEFAULT '00:00:00',
    to_time     TEXT    NOT NULL DEFAULT '24:00:00',
    break_name  TEXT    NULL,                         -- only breaks with this name
    position    TEXT    NOT NULL DEFAULT 'any',       -- any | first | last
    fixed       INTEGER NOT NULL DEFAULT 0            -- 1: only in the band/break; 0: may go to a nearby break when full
);

-- One per station and broadcast day.
CREATE TABLE IF NOT EXISTS logs (
    id         INTEGER PRIMARY KEY,
    station_id INTEGER NOT NULL REFERENCES stations(id) ON DELETE CASCADE,
    date       TEXT    NOT NULL,
    status     TEXT    NOT NULL DEFAULT 'draft',      -- draft | ready | on_air
    revision   INTEGER NOT NULL DEFAULT 1,
    made_by    TEXT    NOT NULL DEFAULT 'auto',
    checked_by TEXT    NULL,
    checked_at TEXT    NULL,
    created_at TEXT    NOT NULL,
    updated_at TEXT    NOT NULL,
    UNIQUE (station_id, date)
);

CREATE TABLE IF NOT EXISTS log_items (
    id               INTEGER PRIMARY KEY,
    log_id           INTEGER NOT NULL REFERENCES logs(id) ON DELETE CASCADE,
    hour             INTEGER NOT NULL,
    position         INTEGER NOT NULL,
    kind             TEXT    NOT NULL,                -- song | jingle | spot | other | event | voice
    track_id         INTEGER NULL REFERENCES tracks(id) ON DELETE SET NULL,
    source           TEXT    NULL,
    title            TEXT    NOT NULL DEFAULT '',
    artist           TEXT    NOT NULL DEFAULT '',
    duration         REAL    NOT NULL DEFAULT 0,
    starts_at        TEXT    NOT NULL DEFAULT '00:00:00',   -- when it is expected to start
    due_at           TEXT    NULL,                    -- timed: when it is due (soft, hard) or has to end (end_at)
    timing           TEXT    NOT NULL DEFAULT 'none',
    max_delay        REAL    NULL,
    lock_level       TEXT    NOT NULL DEFAULT 'free',
    group_key        TEXT    NULL,                    -- items that belong together (one break, one event)
    group_name       TEXT    NULL,
    clock_slot_id    INTEGER NULL REFERENCES clock_slots(id) ON DELETE SET NULL,
    campaign_spot_id INTEGER NULL REFERENCES campaign_spots(id) ON DELETE SET NULL,
    booking_id       INTEGER NULL REFERENCES bookings(id) ON DELETE SET NULL,
    state            TEXT    NOT NULL DEFAULT 'scheduled'   -- scheduled | loaded | aired | missed | removed
);
CREATE INDEX IF NOT EXISTS log_items_log ON log_items (log_id, hour, position);
CREATE INDEX IF NOT EXISTS log_items_booking ON log_items (booking_id);

-- What went on air, linked to the log.
ALTER TABLE plays ADD COLUMN station_id INTEGER NULL;
ALTER TABLE plays ADD COLUMN log_item_id INTEGER NULL;
ALTER TABLE plays ADD COLUMN ended_at TEXT NULL;
ALTER TABLE plays ADD COLUMN kind TEXT NULL;
CREATE INDEX IF NOT EXISTS plays_log_item ON plays (log_item_id);

INSERT OR IGNORE INTO stations (id, name) VALUES (1, 'Station 1');

UPDATE schema_info SET value = '3' WHERE name = 'version';
