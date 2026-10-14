-- Sintoniza music library, schema version 1 (SQLite).
-- Documented in docs/library-database.md; other programs may write to these tables.
-- Safe to run again: a version step that stopped halfway (MySQL commits each table as it goes) is finished.
-- Times are UTC text, 'yyyy-MM-ddTHH:mm:ssZ'. Booleans are 0/1.

CREATE TABLE IF NOT EXISTS schema_info (
    name  TEXT PRIMARY KEY,
    value TEXT NOT NULL
);

-- What a song or jingle is for the station: Current, Gold, Jingles, Ads…
CREATE TABLE IF NOT EXISTS categories (
    id         INTEGER PRIMARY KEY,
    name       TEXT    NOT NULL UNIQUE,
    kind       TEXT    NOT NULL DEFAULT 'music',  -- music | jingle | commercial | other
    color      TEXT    NULL,                      -- '#RRGGBB'
    sort_order INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE IF NOT EXISTS tracks (
    id            INTEGER PRIMARY KEY,
    path          TEXT    NOT NULL UNIQUE,        -- full path of the file, as this computer sees it
    file_size     INTEGER NOT NULL DEFAULT 0,     -- bytes; with file_modified, tells a rescan whether to read the file again
    file_modified TEXT    NULL,
    title         TEXT    NOT NULL DEFAULT '',
    artist        TEXT    NOT NULL DEFAULT '',
    album         TEXT    NOT NULL DEFAULT '',
    genre         TEXT    NOT NULL DEFAULT '',
    year          INTEGER NULL,
    isrc          TEXT    NULL,
    duration      REAL    NOT NULL DEFAULT 0,     -- seconds
    category_id   INTEGER NULL REFERENCES categories(id) ON DELETE SET NULL,
    tempo         INTEGER NULL,                   -- 1 slow … 5 fast
    voice         TEXT    NULL,                   -- male | female | duo | group | instrumental
    language      TEXT    NULL,                   -- e.g. 'pt', 'en'
    enabled       INTEGER NOT NULL DEFAULT 1,     -- 0: never picked automatically
    missing       INTEGER NOT NULL DEFAULT 0,     -- 1: the last scan didn't find the file
    added_at      TEXT    NOT NULL,
    updated_at    TEXT    NOT NULL,
    search        TEXT    NOT NULL DEFAULT ''     -- filled by the app: lower case, no accents; leave '' when inserting
);
CREATE INDEX IF NOT EXISTS tracks_artist_title ON tracks (artist COLLATE NOCASE, title COLLATE NOCASE);
CREATE INDEX IF NOT EXISTS tracks_category ON tracks (category_id);

-- Every item that went on air (the as-run history; rotation rules use it).
CREATE TABLE IF NOT EXISTS plays (
    id         INTEGER PRIMARY KEY,
    track_id   INTEGER NULL REFERENCES tracks(id) ON DELETE SET NULL,
    path       TEXT    NOT NULL,
    artist     TEXT    NOT NULL DEFAULT '',
    title      TEXT    NOT NULL DEFAULT '',
    started_at TEXT    NOT NULL,
    source     TEXT    NOT NULL DEFAULT 'playlist'  -- playlist | cart
);
CREATE INDEX IF NOT EXISTS plays_started ON plays (started_at);
CREATE INDEX IF NOT EXISTS plays_track ON plays (track_id, started_at);

INSERT OR IGNORE INTO categories (name, kind, color, sort_order) VALUES
    ('Current',   'music',      '#3E8EDE', 1),
    ('Recurrent', 'music',      '#2FA37A', 2),
    ('Gold',      'music',      '#D6A21E', 3),
    ('Jingles',   'jingle',     '#B4232A', 4),
    ('IDs',       'jingle',     '#C2185B', 5),
    ('Promos',    'other',      '#7B5CC4', 6),
    ('Ads',       'commercial', '#1F6F4A', 7);

INSERT OR IGNORE INTO schema_info (name, value) VALUES ('version', '1');
