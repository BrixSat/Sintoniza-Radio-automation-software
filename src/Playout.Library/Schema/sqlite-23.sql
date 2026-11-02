-- Sintoniza library, schema version 23 (SQLite): a soft event or ad break may start early. With max_early_minutes it
-- goes in that long before its time, after the item on air, so it plays at the first song end in the window from
-- then to max_delay_minutes after its time (at the end of it, what is on air fades). Same as mysql-23.sql.

ALTER TABLE schedule_entries ADD COLUMN max_early_minutes REAL NOT NULL DEFAULT 0;

UPDATE schema_info SET value = '23' WHERE name = 'version';
