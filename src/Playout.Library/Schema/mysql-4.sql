-- Sintoniza library, schema version 4 (MySQL / MariaDB): the station's schedule and traffic, as the schedule program
-- edits them (events, ad breaks and their running order, smart playlists, clients and campaigns), the folders of each
-- category, and the as-run log. Same tables as sqlite-4.sql. The empty tables of the earlier log-based design
-- (clocks, advertisers, campaigns, bookings, logs…) are removed by the app after this script, if they are still empty.
-- Times of day 'HH:mm:ss'; dates 'yyyy-MM-dd'; local date-times 'yyyy-MM-ddTHH:mm:ss'.

CREATE TABLE IF NOT EXISTS schedule_entries (
    id                CHAR(36)      NOT NULL PRIMARY KEY,
    station_id        BIGINT        NOT NULL,
    type              VARCHAR(10)   NOT NULL,
    position          INT           NOT NULL DEFAULT 0,
    name              VARCHAR(200)  NOT NULL DEFAULT '',
    enabled           TINYINT       NOT NULL DEFAULT 1,
    start_mode        VARCHAR(10)   NOT NULL DEFAULT 'Soft',
    max_delay_minutes DOUBLE        NOT NULL DEFAULT 0,
    lock_level        VARCHAR(10)   NOT NULL DEFAULT 'Free',
    path              VARCHAR(1000) NOT NULL DEFAULT '',
    stream_minutes    DOUBLE        NOT NULL DEFAULT 0,
    max_age_minutes   INT           NOT NULL DEFAULT 0,
    KEY schedule_entries_station (station_id, type, position),
    CONSTRAINT schedule_entries_station_fk FOREIGN KEY (station_id) REFERENCES stations (id) ON DELETE CASCADE
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4;

CREATE TABLE IF NOT EXISTS schedule_slots (
    entry_id CHAR(36) NOT NULL,
    weekday  TINYINT  NOT NULL,
    time     CHAR(8)  NOT NULL,
    PRIMARY KEY (entry_id, weekday, time),
    CONSTRAINT schedule_slots_entry_fk FOREIGN KEY (entry_id) REFERENCES schedule_entries (id) ON DELETE CASCADE
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4;

CREATE TABLE IF NOT EXISTS break_parts (
    entry_id          CHAR(36)      NOT NULL,
    position          INT           NOT NULL,
    kind              VARCHAR(10)   NOT NULL,
    path              VARCHAR(1000) NOT NULL DEFAULT '',
    section           VARCHAR(100)  NOT NULL DEFAULT '',
    only_with_adverts TINYINT       NOT NULL DEFAULT 0,
    PRIMARY KEY (entry_id, position),
    CONSTRAINT break_parts_entry_fk FOREIGN KEY (entry_id) REFERENCES schedule_entries (id) ON DELETE CASCADE
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4;

CREATE TABLE IF NOT EXISTS playlist_lines (
    entry_id      CHAR(36)      NOT NULL,
    position      INT           NOT NULL,
    source        VARCHAR(10)   NOT NULL,
    category_id   BIGINT        NULL,
    category_name VARCHAR(100)  NOT NULL DEFAULT '',
    kind          VARCHAR(20)   NOT NULL DEFAULT 'music',
    folder        VARCHAR(1000) NOT NULL DEFAULT '',
    PRIMARY KEY (entry_id, position),
    CONSTRAINT playlist_lines_entry_fk FOREIGN KEY (entry_id) REFERENCES schedule_entries (id) ON DELETE CASCADE
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4;

CREATE TABLE IF NOT EXISTS ad_clients (
    id         CHAR(36)     NOT NULL PRIMARY KEY,
    station_id BIGINT       NOT NULL,
    position   INT          NOT NULL DEFAULT 0,
    name       VARCHAR(200) NOT NULL DEFAULT '',
    enabled    TINYINT      NOT NULL DEFAULT 1,
    CONSTRAINT ad_clients_station_fk FOREIGN KEY (station_id) REFERENCES stations (id) ON DELETE CASCADE
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4;

CREATE TABLE IF NOT EXISTS ad_campaigns (
    id         CHAR(36)     NOT NULL PRIMARY KEY,
    client_id  CHAR(36)     NOT NULL,
    position   INT          NOT NULL DEFAULT 0,
    name       VARCHAR(200) NOT NULL DEFAULT '',
    enabled    TINYINT      NOT NULL DEFAULT 1,
    starts_on  CHAR(10)     NULL,
    ends_on    CHAR(10)     NULL,
    section    VARCHAR(100) NOT NULL DEFAULT '',
    place      INT          NOT NULL DEFAULT 0,
    CONSTRAINT ad_campaigns_client_fk FOREIGN KEY (client_id) REFERENCES ad_clients (id) ON DELETE CASCADE
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4;

CREATE TABLE IF NOT EXISTS ad_campaign_files (
    campaign_id CHAR(36)      NOT NULL,
    position    INT           NOT NULL,
    path        VARCHAR(1000) NOT NULL,
    PRIMARY KEY (campaign_id, position),
    CONSTRAINT ad_campaign_files_campaign_fk FOREIGN KEY (campaign_id) REFERENCES ad_campaigns (id) ON DELETE CASCADE
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4;

CREATE TABLE IF NOT EXISTS ad_placements (
    id          BIGINT   NOT NULL AUTO_INCREMENT PRIMARY KEY,
    campaign_id CHAR(36) NOT NULL,
    weekday     TINYINT  NULL,
    date        CHAR(10) NULL,
    time        CHAR(8)  NOT NULL,
    KEY ad_placements_campaign (campaign_id),
    CONSTRAINT ad_placements_campaign_fk FOREIGN KEY (campaign_id) REFERENCES ad_campaigns (id) ON DELETE CASCADE
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4;

CREATE TABLE IF NOT EXISTS schedule_state (
    entry_id     CHAR(36) NOT NULL PRIMARY KEY,
    last_started CHAR(19) NOT NULL
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4;

CREATE TABLE IF NOT EXISTS schedule_versions (
    station_id BIGINT       NOT NULL PRIMARY KEY,
    version    BIGINT       NOT NULL DEFAULT 0,
    updated_at CHAR(20)     NOT NULL,
    updated_by VARCHAR(100) NOT NULL DEFAULT '',
    CONSTRAINT schedule_versions_station_fk FOREIGN KEY (station_id) REFERENCES stations (id) ON DELETE CASCADE
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4;

CREATE TABLE IF NOT EXISTS category_folders (
    path        VARCHAR(760) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL PRIMARY KEY,
    category_id BIGINT       NOT NULL,
    CONSTRAINT category_folders_category_fk FOREIGN KEY (category_id) REFERENCES categories (id) ON DELETE CASCADE
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4;

CREATE TABLE IF NOT EXISTS as_run (
    id         BIGINT       NOT NULL AUTO_INCREMENT PRIMARY KEY,
    station_id BIGINT       NOT NULL,
    started_at CHAR(19)     NOT NULL,
    ended_at   CHAR(19)     NULL,
    type       VARCHAR(20)  NOT NULL DEFAULT '',
    source     VARCHAR(20)  NOT NULL DEFAULT '',
    event      VARCHAR(200) NOT NULL DEFAULT '',
    advertiser VARCHAR(200) NOT NULL DEFAULT '',
    artist     VARCHAR(500) NOT NULL DEFAULT '',
    title      VARCHAR(500) NOT NULL DEFAULT '',
    album      VARCHAR(500) NOT NULL DEFAULT '',
    composer   VARCHAR(500) NOT NULL DEFAULT '',
    label      VARCHAR(500) NOT NULL DEFAULT '',
    isrc       VARCHAR(20)  NOT NULL DEFAULT '',
    year       VARCHAR(10)  NOT NULL DEFAULT '',
    file       VARCHAR(760) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL DEFAULT '',
    UNIQUE KEY as_run_play (station_id, started_at, file),
    KEY as_run_advertiser (advertiser, started_at)
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4;

UPDATE schema_info SET value = '4' WHERE name = 'version';
