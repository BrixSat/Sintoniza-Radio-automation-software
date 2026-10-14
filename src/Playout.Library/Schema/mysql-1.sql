-- Sintoniza music library, schema version 1 (MySQL 8 / MariaDB 10.5 or newer).
-- Same tables as sqlite-1.sql; documented in docs/library-database.md.
-- Safe to run again: a version step that stopped halfway (MySQL commits each table as it goes) is finished.
-- Times are UTC text, 'yyyy-MM-ddTHH:mm:ssZ'. Booleans are 0/1.
-- path compares exactly (utf8mb4_bin): the default collations ignore case and accents, which would make
-- "Canção.mp3" and "Cancao.mp3" the same file.

CREATE TABLE IF NOT EXISTS schema_info (
    name  VARCHAR(64)  NOT NULL PRIMARY KEY,
    value VARCHAR(255) NOT NULL
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4;

CREATE TABLE IF NOT EXISTS categories (
    id         BIGINT       NOT NULL AUTO_INCREMENT PRIMARY KEY,
    name       VARCHAR(100) NOT NULL,
    kind       VARCHAR(20)  NOT NULL DEFAULT 'music',
    color      VARCHAR(9)   NULL,
    sort_order INT          NOT NULL DEFAULT 0,
    UNIQUE KEY categories_name (name)
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4;

CREATE TABLE IF NOT EXISTS tracks (
    id            BIGINT        NOT NULL AUTO_INCREMENT PRIMARY KEY,
    path          VARCHAR(760)  CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
    file_size     BIGINT        NOT NULL DEFAULT 0,
    file_modified CHAR(20)      NULL,
    title         VARCHAR(500)  NOT NULL DEFAULT '',
    artist        VARCHAR(500)  NOT NULL DEFAULT '',
    album         VARCHAR(500)  NOT NULL DEFAULT '',
    genre         VARCHAR(200)  NOT NULL DEFAULT '',
    year          INT           NULL,
    isrc          VARCHAR(20)   NULL,
    duration      DOUBLE        NOT NULL DEFAULT 0,
    category_id   BIGINT        NULL,
    tempo         TINYINT       NULL,
    voice         VARCHAR(20)   NULL,
    language      VARCHAR(10)   NULL,
    enabled       TINYINT       NOT NULL DEFAULT 1,
    missing       TINYINT       NOT NULL DEFAULT 0,
    added_at      CHAR(20)      NOT NULL,
    updated_at    CHAR(20)      NOT NULL,
    search        VARCHAR(2000) NOT NULL DEFAULT '',
    UNIQUE KEY tracks_path (path),
    KEY tracks_artist_title (artist(100), title(100)),
    KEY tracks_category (category_id),
    CONSTRAINT tracks_category_fk FOREIGN KEY (category_id) REFERENCES categories (id) ON DELETE SET NULL
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4;

CREATE TABLE IF NOT EXISTS plays (
    id         BIGINT       NOT NULL AUTO_INCREMENT PRIMARY KEY,
    track_id   BIGINT       NULL,
    path       VARCHAR(760) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
    artist     VARCHAR(500) NOT NULL DEFAULT '',
    title      VARCHAR(500) NOT NULL DEFAULT '',
    started_at CHAR(20)     NOT NULL,
    source     VARCHAR(20)  NOT NULL DEFAULT 'playlist',
    KEY plays_started (started_at),
    KEY plays_track (track_id, started_at),
    CONSTRAINT plays_track_fk FOREIGN KEY (track_id) REFERENCES tracks (id) ON DELETE SET NULL
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4;

INSERT IGNORE INTO categories (name, kind, color, sort_order) VALUES
    ('Current',   'music',      '#3E8EDE', 1),
    ('Recurrent', 'music',      '#2FA37A', 2),
    ('Gold',      'music',      '#D6A21E', 3),
    ('Jingles',   'jingle',     '#B4232A', 4),
    ('IDs',       'jingle',     '#C2185B', 5),
    ('Promos',    'other',      '#7B5CC4', 6),
    ('Ads',       'commercial', '#1F6F4A', 7);

INSERT IGNORE INTO schema_info (name, value) VALUES ('version', '1');
