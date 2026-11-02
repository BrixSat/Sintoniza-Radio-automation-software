-- Sintoniza library, schema version 22 (MySQL / MariaDB): seasons for hour templates (see sqlite-22.sql).

CREATE TABLE IF NOT EXISTS seasons (
    id         CHAR(36)     NOT NULL PRIMARY KEY,
    station_id BIGINT       NOT NULL,
    position   INT          NOT NULL DEFAULT 0,
    name       VARCHAR(100) NOT NULL DEFAULT '',
    starts_on  CHAR(5)      NOT NULL,
    ends_on    CHAR(5)      NOT NULL,
    CONSTRAINT seasons_station_fk FOREIGN KEY (station_id) REFERENCES stations (id) ON DELETE CASCADE
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4;

ALTER TABLE schedule_entries ADD COLUMN season_id CHAR(36) NULL;

UPDATE schema_info SET value = '22' WHERE name = 'version';
