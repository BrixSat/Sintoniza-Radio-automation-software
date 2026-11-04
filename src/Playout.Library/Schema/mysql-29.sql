-- Sintoniza library, schema version 29 (MySQL / MariaDB): the studios' logs, and what each studio warns about (see
-- sqlite-29.sql).

CREATE TABLE IF NOT EXISTS studio_log (
    id          BIGINT       NOT NULL AUTO_INCREMENT PRIMARY KEY,
    station_id  BIGINT       NOT NULL,
    computer    VARCHAR(100) NOT NULL,
    at          CHAR(19)     NOT NULL,
    problem     TINYINT      NOT NULL DEFAULT 0,
    line        TEXT         NOT NULL,
    INDEX studio_log_at (station_id, computer, at),
    INDEX studio_log_problems (station_id, problem, at),
    INDEX studio_log_old (at)
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4;

ALTER TABLE studios ADD COLUMN warnings VARCHAR(1000) NOT NULL DEFAULT '';

UPDATE schema_info SET value = '29' WHERE name = 'version';
