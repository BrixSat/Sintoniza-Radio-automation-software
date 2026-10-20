-- Sintoniza library, schema version 11 (MySQL / MariaDB): users (see sqlite-11.sql). Same table as sqlite-11.sql.

CREATE TABLE IF NOT EXISTS users (
    id            BIGINT       NOT NULL AUTO_INCREMENT PRIMARY KEY,
    name          VARCHAR(100) NOT NULL,
    password_hash VARCHAR(200) NOT NULL,
    permissions   VARCHAR(500) NOT NULL DEFAULT '',
    enabled       TINYINT      NOT NULL DEFAULT 1,
    last_login    CHAR(20)     NULL,
    created_at    CHAR(20)     NOT NULL,
    UNIQUE KEY users_name (name)
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4;

UPDATE schema_info SET value = '11' WHERE name = 'version';
