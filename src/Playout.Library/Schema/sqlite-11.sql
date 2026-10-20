-- Sintoniza library, schema version 11 (SQLite): users. Sintoniza Pro, Sintoniza Import, the playout's Settings and
-- the web remote ask for one. The password is kept only as a salted hash (PBKDF2-SHA256: iterations$salt$hash, in
-- base64). permissions: comma-separated keys (library, programming, voice, traffic, station, users, computer,
-- remote). The app makes admin (everything) and dj when there are none. Same table as mysql-11.sql.

CREATE TABLE IF NOT EXISTS users (
    id            INTEGER PRIMARY KEY,
    name          TEXT    NOT NULL UNIQUE,
    password_hash TEXT    NOT NULL,
    permissions   TEXT    NOT NULL DEFAULT '',
    enabled       INTEGER NOT NULL DEFAULT 1,
    last_login    TEXT    NULL,               -- UTC
    created_at    TEXT    NOT NULL
);

UPDATE schema_info SET value = '11' WHERE name = 'version';
