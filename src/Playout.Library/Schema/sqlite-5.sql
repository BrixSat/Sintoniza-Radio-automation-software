-- Sintoniza library, schema version 5 (SQLite): categories in a tree (Music › 80s) and songs in more than one
-- category. Same tables as mysql-5.sql. Runs in one transaction, with foreign keys switched off by the app for it:
-- the categories table is made again (a category's name is now unique among its siblings, not in the whole library).

CREATE TABLE categories_new (
    id         INTEGER PRIMARY KEY,
    parent_id  INTEGER NULL REFERENCES categories(id) ON DELETE SET NULL,  -- null: a top-level category
    name       TEXT    NOT NULL,
    kind       TEXT    NOT NULL DEFAULT 'music',  -- music | jingle | commercial | other
    color      TEXT    NULL,                      -- '#RRGGBB'
    sort_order INTEGER NOT NULL DEFAULT 0,
    UNIQUE (parent_id, name)
);
INSERT INTO categories_new (id, parent_id, name, kind, color, sort_order)
    SELECT id, NULL, name, kind, color, sort_order FROM categories;
DROP TABLE categories;
ALTER TABLE categories_new RENAME TO categories;
CREATE INDEX IF NOT EXISTS categories_parent ON categories (parent_id);

-- The other categories a song is in, besides its own (tracks.category_id). Choosing a category takes in its songs,
-- the songs that are also in it, and the same for every category under it.
CREATE TABLE IF NOT EXISTS track_categories (
    track_id    INTEGER NOT NULL REFERENCES tracks(id) ON DELETE CASCADE,
    category_id INTEGER NOT NULL REFERENCES categories(id) ON DELETE CASCADE,
    PRIMARY KEY (track_id, category_id)
);
CREATE INDEX IF NOT EXISTS track_categories_category ON track_categories (category_id);

UPDATE schema_info SET value = '5' WHERE name = 'version';
