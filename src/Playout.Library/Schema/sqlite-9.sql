-- Sintoniza library, schema version 9 (SQLite): fixed top-level categories. Every category is under one of eight
-- that can't be deleted or renamed, each with a role: music (Music), jingles (Jingles), promos (Promos), progautor
-- (ProgAutor: shows recorded outside), fillers (Fillers), redacao (Redação: news), spots (Spots: adverts), takes
-- (Takes: voice inserts — the time, the temperature…). The app makes them when it opens the library, taking in
-- top-level categories of the same name (Musicas → Music, Ads → Spots) and putting the others under them by kind.
-- And the station's settings (shared by every studio, edited in Sintoniza Pro). Same as mysql-9.sql.

ALTER TABLE categories ADD COLUMN role TEXT NULL;   -- one of the eight above for the top-level ones; null for the rest
CREATE UNIQUE INDEX IF NOT EXISTS categories_role ON categories (role);

CREATE TABLE IF NOT EXISTS station_settings (
    station_id INTEGER NOT NULL PRIMARY KEY REFERENCES stations(id) ON DELETE CASCADE,
    settings   TEXT    NOT NULL,                    -- JSON, see docs/library-database.md
    version    INTEGER NOT NULL DEFAULT 1,          -- +1 on every save: the playouts read them again
    updated_at TEXT    NOT NULL,
    updated_by TEXT    NOT NULL DEFAULT ''
);

UPDATE schema_info SET value = '9' WHERE name = 'version';
