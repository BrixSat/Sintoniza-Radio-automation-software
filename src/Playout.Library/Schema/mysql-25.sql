-- Sintoniza library, schema version 25 (MySQL / MariaDB): listener counts (see sqlite-25.sql).

CREATE TABLE IF NOT EXISTS listeners (
    station_id BIGINT   NOT NULL,
    minute     CHAR(16) NOT NULL,
    listeners  INT      NOT NULL,
    PRIMARY KEY (station_id, minute)
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4;

UPDATE schema_info SET value = '25' WHERE name = 'version';
