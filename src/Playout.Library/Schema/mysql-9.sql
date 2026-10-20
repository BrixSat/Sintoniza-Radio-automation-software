-- Sintoniza library, schema version 9 (MySQL / MariaDB): fixed top-level categories (role: music, jingles, promos,
-- progautor, fillers, redacao, spots, takes; made by the app, see sqlite-9.sql) and the station's settings. Same as
-- sqlite-9.sql.

ALTER TABLE categories ADD COLUMN role VARCHAR(12) NULL;
ALTER TABLE categories ADD UNIQUE KEY categories_role (role);

CREATE TABLE IF NOT EXISTS station_settings (
    station_id BIGINT       NOT NULL PRIMARY KEY,
    settings   MEDIUMTEXT   NOT NULL,
    version    BIGINT       NOT NULL DEFAULT 1,
    updated_at CHAR(20)     NOT NULL,
    updated_by VARCHAR(200) NOT NULL DEFAULT '',
    CONSTRAINT station_settings_station_fk FOREIGN KEY (station_id) REFERENCES stations (id) ON DELETE CASCADE
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4;

UPDATE schema_info SET value = '9' WHERE name = 'version';
