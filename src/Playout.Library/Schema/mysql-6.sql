-- Sintoniza library, schema version 6 (MySQL / MariaDB): the daily playlists. Each hour is generated in advance from
-- the hour template placed there (Sintoniza Schedule → Playlists): the exact songs and jingles, in order, with how
-- long each plays. The playlist manager shows and edits them; the playout plays them. Ad breaks and events aren't
-- stored here: they come from the schedule (bookings can change up to air time). Same tables as sqlite-6.sql.
-- Hours are local times 'yyyy-MM-ddTHH:00:00'; generated_at is UTC.

CREATE TABLE IF NOT EXISTS plan_hours (
    station_id   BIGINT       NOT NULL,
    hour_start   CHAR(19)     NOT NULL,
    template_id  CHAR(36)     NULL,
    signature    TEXT         NOT NULL,
    generated_at CHAR(20)     NOT NULL,
    generated_by VARCHAR(100) NOT NULL DEFAULT '',
    edited       TINYINT      NOT NULL DEFAULT 0,
    PRIMARY KEY (station_id, hour_start),
    CONSTRAINT plan_hours_station_fk FOREIGN KEY (station_id) REFERENCES stations (id) ON DELETE CASCADE
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4;

CREATE TABLE IF NOT EXISTS plan_items (
    id          CHAR(36)      NOT NULL PRIMARY KEY,
    station_id  BIGINT        NOT NULL,
    hour_start  CHAR(19)      NOT NULL,
    position    INT           NOT NULL,
    kind        VARCHAR(12)   NOT NULL DEFAULT 'music',
    path        VARCHAR(1000) NOT NULL,
    title       VARCHAR(500)  NOT NULL DEFAULT '',
    artist      VARCHAR(500)  NOT NULL DEFAULT '',
    seconds     DOUBLE        NOT NULL DEFAULT 0,
    track_id    BIGINT        NULL,
    line        INT           NULL,
    category_id BIGINT        NULL,
    optional    TINYINT       NOT NULL DEFAULT 0,
    KEY plan_items_hour (station_id, hour_start, position),
    CONSTRAINT plan_items_track_fk FOREIGN KEY (track_id) REFERENCES tracks (id) ON DELETE SET NULL,
    CONSTRAINT plan_items_hour_fk FOREIGN KEY (station_id, hour_start) REFERENCES plan_hours (station_id, hour_start) ON DELETE CASCADE
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4;

UPDATE schema_info SET value = '6' WHERE name = 'version';
