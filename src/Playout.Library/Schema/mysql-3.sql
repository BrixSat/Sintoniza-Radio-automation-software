-- Sintoniza library, schema version 3 (MySQL / MariaDB): scheduling. Stations, clocks (hour plans), rotation
-- rules, commercial traffic (advertisers, campaigns, bookings) and the daily logs the playout plays.
-- Same tables as sqlite-3.sql. Replaced by the schedule tables of version 4 (their empty tables are removed then).
-- Safe to run again: MySQL commits each change as it goes, so what a stopped run already made is skipped.
-- Dates are local to the station, 'yyyy-MM-dd'; times of day 'HH:mm:ss'; lengths and offsets in seconds.

CREATE TABLE IF NOT EXISTS stations (
    id        BIGINT       NOT NULL AUTO_INCREMENT PRIMARY KEY,
    name      VARCHAR(100) NOT NULL,
    time_zone VARCHAR(64)  NOT NULL DEFAULT '',
    active    TINYINT      NOT NULL DEFAULT 1,
    UNIQUE KEY stations_name (name)
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4;

CREATE TABLE IF NOT EXISTS schedule_settings (
    station_id BIGINT       NOT NULL,
    name       VARCHAR(64)  NOT NULL,
    value      VARCHAR(255) NOT NULL,
    PRIMARY KEY (station_id, name),
    CONSTRAINT schedule_settings_station_fk FOREIGN KEY (station_id) REFERENCES stations (id) ON DELETE CASCADE
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4;

CREATE TABLE IF NOT EXISTS clocks (
    id         BIGINT       NOT NULL AUTO_INCREMENT PRIMARY KEY,
    station_id BIGINT       NOT NULL,
    name       VARCHAR(100) NOT NULL,
    color      VARCHAR(9)   NULL,
    active     TINYINT      NOT NULL DEFAULT 1,
    CONSTRAINT clocks_station_fk FOREIGN KEY (station_id) REFERENCES stations (id) ON DELETE CASCADE
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4;

CREATE TABLE IF NOT EXISTS clock_slots (
    id             BIGINT       NOT NULL AUTO_INCREMENT PRIMARY KEY,
    clock_id       BIGINT       NOT NULL,
    position       INT          NOT NULL,
    kind           VARCHAR(20)  NOT NULL,
    category_id    BIGINT       NULL,
    track_id       BIGINT       NULL,
    source         VARCHAR(1000) NULL,
    name           VARCHAR(100) NULL,
    at_seconds     DOUBLE       NULL,
    timing         VARCHAR(10)  NOT NULL DEFAULT 'none',
    max_delay      DOUBLE       NULL,
    lock_level     VARCHAR(10)  NOT NULL DEFAULT 'free',
    max_seconds    DOUBLE       NULL,
    intro_track_id BIGINT       NULL,
    outro_track_id BIGINT       NULL,
    KEY clock_slots_clock (clock_id, position),
    CONSTRAINT clock_slots_clock_fk FOREIGN KEY (clock_id) REFERENCES clocks (id) ON DELETE CASCADE,
    CONSTRAINT clock_slots_category_fk FOREIGN KEY (category_id) REFERENCES categories (id) ON DELETE SET NULL,
    CONSTRAINT clock_slots_track_fk FOREIGN KEY (track_id) REFERENCES tracks (id) ON DELETE SET NULL,
    CONSTRAINT clock_slots_intro_fk FOREIGN KEY (intro_track_id) REFERENCES tracks (id) ON DELETE SET NULL,
    CONSTRAINT clock_slots_outro_fk FOREIGN KEY (outro_track_id) REFERENCES tracks (id) ON DELETE SET NULL
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4;

CREATE TABLE IF NOT EXISTS clock_grid (
    station_id BIGINT  NOT NULL,
    weekday    TINYINT NOT NULL,
    hour       TINYINT NOT NULL,
    clock_id   BIGINT  NOT NULL,
    PRIMARY KEY (station_id, weekday, hour),
    CONSTRAINT clock_grid_station_fk FOREIGN KEY (station_id) REFERENCES stations (id) ON DELETE CASCADE,
    CONSTRAINT clock_grid_clock_fk FOREIGN KEY (clock_id) REFERENCES clocks (id) ON DELETE CASCADE
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4;

CREATE TABLE IF NOT EXISTS clock_overrides (
    station_id BIGINT   NOT NULL,
    date       CHAR(10) NOT NULL,
    hour       TINYINT  NOT NULL,
    clock_id   BIGINT   NOT NULL,
    PRIMARY KEY (station_id, date, hour),
    CONSTRAINT clock_overrides_station_fk FOREIGN KEY (station_id) REFERENCES stations (id) ON DELETE CASCADE,
    CONSTRAINT clock_overrides_clock_fk FOREIGN KEY (clock_id) REFERENCES clocks (id) ON DELETE CASCADE
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4;

CREATE TABLE IF NOT EXISTS rotation_rules (
    id                  BIGINT  NOT NULL AUTO_INCREMENT PRIMARY KEY,
    station_id          BIGINT  NOT NULL,
    category_id         BIGINT  NOT NULL,
    song_rest_minutes   INT     NULL,
    artist_rest_minutes INT     NULL,
    title_rest_minutes  INT     NULL,
    max_per_hour        INT     NULL,
    tempo_flow          TINYINT NOT NULL DEFAULT 0,
    voice_flow          TINYINT NOT NULL DEFAULT 0,
    UNIQUE KEY rotation_rules_category (station_id, category_id),
    CONSTRAINT rotation_rules_station_fk FOREIGN KEY (station_id) REFERENCES stations (id) ON DELETE CASCADE,
    CONSTRAINT rotation_rules_category_fk FOREIGN KEY (category_id) REFERENCES categories (id) ON DELETE CASCADE
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4;

CREATE TABLE IF NOT EXISTS advertisers (
    id          BIGINT        NOT NULL AUTO_INCREMENT PRIMARY KEY,
    name        VARCHAR(200)  NOT NULL,
    competition VARCHAR(50)   NULL,
    contact     VARCHAR(500)  NULL,
    notes       VARCHAR(2000) NULL,
    active      TINYINT       NOT NULL DEFAULT 1
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4;

CREATE TABLE IF NOT EXISTS campaigns (
    id            BIGINT        NOT NULL AUTO_INCREMENT PRIMARY KEY,
    station_id    BIGINT        NOT NULL,
    advertiser_id BIGINT        NOT NULL,
    name          VARCHAR(200)  NOT NULL,
    starts_on     CHAR(10)      NOT NULL,
    ends_on       CHAR(10)      NOT NULL,
    status        VARCHAR(20)   NOT NULL DEFAULT 'booked',
    notes         VARCHAR(2000) NULL,
    created_at    CHAR(20)      NOT NULL,
    updated_at    CHAR(20)      NOT NULL,
    KEY campaigns_dates (station_id, starts_on, ends_on),
    CONSTRAINT campaigns_station_fk FOREIGN KEY (station_id) REFERENCES stations (id) ON DELETE CASCADE,
    CONSTRAINT campaigns_advertiser_fk FOREIGN KEY (advertiser_id) REFERENCES advertisers (id)
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4;

CREATE TABLE IF NOT EXISTS campaign_spots (
    id          BIGINT   NOT NULL AUTO_INCREMENT PRIMARY KEY,
    campaign_id BIGINT   NOT NULL,
    track_id    BIGINT   NOT NULL,
    weight      INT      NOT NULL DEFAULT 1,
    starts_on   CHAR(10) NULL,
    ends_on     CHAR(10) NULL,
    CONSTRAINT campaign_spots_campaign_fk FOREIGN KEY (campaign_id) REFERENCES campaigns (id) ON DELETE CASCADE,
    CONSTRAINT campaign_spots_track_fk FOREIGN KEY (track_id) REFERENCES tracks (id)
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4;

CREATE TABLE IF NOT EXISTS bookings (
    id          BIGINT       NOT NULL AUTO_INCREMENT PRIMARY KEY,
    campaign_id BIGINT       NOT NULL,
    days        VARCHAR(7)   NOT NULL DEFAULT '0123456',
    per_day     INT          NOT NULL DEFAULT 1,
    from_time   CHAR(8)      NOT NULL DEFAULT '00:00:00',
    to_time     CHAR(8)      NOT NULL DEFAULT '24:00:00',
    break_name  VARCHAR(100) NULL,
    position    VARCHAR(10)  NOT NULL DEFAULT 'any',
    fixed       TINYINT      NOT NULL DEFAULT 0,
    CONSTRAINT bookings_campaign_fk FOREIGN KEY (campaign_id) REFERENCES campaigns (id) ON DELETE CASCADE
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4;

CREATE TABLE IF NOT EXISTS logs (
    id         BIGINT       NOT NULL AUTO_INCREMENT PRIMARY KEY,
    station_id BIGINT       NOT NULL,
    date       CHAR(10)     NOT NULL,
    status     VARCHAR(10)  NOT NULL DEFAULT 'draft',
    revision   INT          NOT NULL DEFAULT 1,
    made_by    VARCHAR(100) NOT NULL DEFAULT 'auto',
    checked_by VARCHAR(100) NULL,
    checked_at CHAR(20)     NULL,
    created_at CHAR(20)     NOT NULL,
    updated_at CHAR(20)     NOT NULL,
    UNIQUE KEY logs_day (station_id, date),
    CONSTRAINT logs_station_fk FOREIGN KEY (station_id) REFERENCES stations (id) ON DELETE CASCADE
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4;

CREATE TABLE IF NOT EXISTS log_items (
    id               BIGINT        NOT NULL AUTO_INCREMENT PRIMARY KEY,
    log_id           BIGINT        NOT NULL,
    hour             TINYINT       NOT NULL,
    position         INT           NOT NULL,
    kind             VARCHAR(10)   NOT NULL,
    track_id         BIGINT        NULL,
    source           VARCHAR(1000) NULL,
    title            VARCHAR(500)  NOT NULL DEFAULT '',
    artist           VARCHAR(500)  NOT NULL DEFAULT '',
    duration         DOUBLE        NOT NULL DEFAULT 0,
    starts_at        CHAR(8)       NOT NULL DEFAULT '00:00:00',
    due_at           CHAR(8)       NULL,
    timing           VARCHAR(10)   NOT NULL DEFAULT 'none',
    max_delay        DOUBLE        NULL,
    lock_level       VARCHAR(10)   NOT NULL DEFAULT 'free',
    group_key        VARCHAR(50)   NULL,
    group_name       VARCHAR(100)  NULL,
    clock_slot_id    BIGINT        NULL,
    campaign_spot_id BIGINT        NULL,
    booking_id       BIGINT        NULL,
    state            VARCHAR(10)   NOT NULL DEFAULT 'scheduled',
    KEY log_items_log (log_id, hour, position),
    KEY log_items_booking (booking_id),
    CONSTRAINT log_items_log_fk FOREIGN KEY (log_id) REFERENCES logs (id) ON DELETE CASCADE,
    CONSTRAINT log_items_track_fk FOREIGN KEY (track_id) REFERENCES tracks (id) ON DELETE SET NULL,
    CONSTRAINT log_items_slot_fk FOREIGN KEY (clock_slot_id) REFERENCES clock_slots (id) ON DELETE SET NULL,
    CONSTRAINT log_items_spot_fk FOREIGN KEY (campaign_spot_id) REFERENCES campaign_spots (id) ON DELETE SET NULL,
    CONSTRAINT log_items_booking_fk FOREIGN KEY (booking_id) REFERENCES bookings (id) ON DELETE SET NULL
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4;

ALTER TABLE plays ADD COLUMN station_id BIGINT NULL;
ALTER TABLE plays ADD COLUMN log_item_id BIGINT NULL;
ALTER TABLE plays ADD COLUMN ended_at CHAR(20) NULL;
ALTER TABLE plays ADD COLUMN kind VARCHAR(10) NULL;
CREATE INDEX plays_log_item ON plays (log_item_id);

INSERT IGNORE INTO stations (id, name) VALUES (1, 'Station 1');

UPDATE schema_info SET value = '3' WHERE name = 'version';
