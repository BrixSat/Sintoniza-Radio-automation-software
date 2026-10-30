-- Sintoniza library, schema version 21 (MySQL / MariaDB): series, a different file for each weekday and for given
-- dates (see sqlite-21.sql).

CREATE TABLE IF NOT EXISTS event_series (
    id       BIGINT        NOT NULL AUTO_INCREMENT PRIMARY KEY,
    entry_id CHAR(36)      NOT NULL,
    weekday  INT           NULL,
    date     CHAR(10)      NULL,
    path     VARCHAR(1000) NOT NULL DEFAULT '',
    KEY event_series_entry (entry_id),
    CONSTRAINT event_series_entry_fk FOREIGN KEY (entry_id) REFERENCES schedule_entries (id) ON DELETE CASCADE
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4;

UPDATE schema_info SET value = '21' WHERE name = 'version';
