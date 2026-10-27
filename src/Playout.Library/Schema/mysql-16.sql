-- Sintoniza library, schema version 16 (MySQL / MariaDB): the studios of each station and which of them is on air
-- (see sqlite-16.sql). Same columns as sqlite-16.sql.

CREATE TABLE IF NOT EXISTS studios (
    station_id BIGINT       NOT NULL,
    computer   VARCHAR(190) NOT NULL,
    seen_at    CHAR(20)     NOT NULL,
    on_air     INT          NOT NULL DEFAULT 0,
    since      CHAR(20)     NULL,
    playing    VARCHAR(500) NOT NULL DEFAULT '',
    version    VARCHAR(40)  NOT NULL DEFAULT '',
    PRIMARY KEY (station_id, computer)
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4;

UPDATE schema_info SET value = '16' WHERE name = 'version';
