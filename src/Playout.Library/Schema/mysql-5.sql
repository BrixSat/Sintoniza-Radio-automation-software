-- Sintoniza library, schema version 5 (MySQL / MariaDB): categories in a tree (Music › 80s) and songs in more than
-- one category. Same tables as sqlite-5.sql. A category's name is now unique among its siblings, not in the whole
-- library. Each statement can run again (the app skips what is already done).

ALTER TABLE categories
    ADD COLUMN parent_id BIGINT NULL AFTER id,
    ADD CONSTRAINT categories_parent_fk FOREIGN KEY (parent_id) REFERENCES categories (id) ON DELETE SET NULL;
ALTER TABLE categories ADD UNIQUE KEY categories_parent_name (parent_id, name);
ALTER TABLE categories DROP INDEX categories_name;

-- The other categories a song is in, besides its own (tracks.category_id). Choosing a category takes in its songs,
-- the songs that are also in it, and the same for every category under it.
CREATE TABLE IF NOT EXISTS track_categories (
    track_id    BIGINT NOT NULL,
    category_id BIGINT NOT NULL,
    PRIMARY KEY (track_id, category_id),
    KEY track_categories_category (category_id),
    CONSTRAINT track_categories_track_fk FOREIGN KEY (track_id) REFERENCES tracks (id) ON DELETE CASCADE,
    CONSTRAINT track_categories_category_fk FOREIGN KEY (category_id) REFERENCES categories (id) ON DELETE CASCADE
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4;

UPDATE schema_info SET value = '5' WHERE name = 'version';
