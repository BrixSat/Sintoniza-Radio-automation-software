-- Sintoniza library, schema version 7 (MySQL / MariaDB): events on a date. Besides its weekly times (schedule_slots),
-- an event can play once at a date and time: e.g. another station's stream put in a day's playlist at 14:00. Same
-- table as sqlite-7.sql. Times are local 'yyyy-MM-ddTHH:mm:ss'.

CREATE TABLE IF NOT EXISTS schedule_dates (
    entry_id CHAR(36) NOT NULL,
    at       CHAR(19) NOT NULL,
    PRIMARY KEY (entry_id, at),
    CONSTRAINT schedule_dates_entry_fk FOREIGN KEY (entry_id) REFERENCES schedule_entries (id) ON DELETE CASCADE
) ENGINE = InnoDB DEFAULT CHARSET = utf8mb4;

UPDATE schema_info SET value = '7' WHERE name = 'version';
