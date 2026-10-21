-- Sintoniza library, schema version 14 (SQLite): when a song may be picked automatically (daily playlists, fill
-- music). play_from / play_until: yyyy-mm-dd, not before / not after (null: no limit); play_days: 7 characters, Monday
-- to Sunday, 1 = may play (null: every day); play_hours: 24 characters, 00h to 23h (null: every hour); max_day,
-- max_week: at most this many times a day / a week, Monday to Sunday (null: no limit). Same columns as mysql-14.sql.

ALTER TABLE tracks ADD COLUMN play_from TEXT NULL;
ALTER TABLE tracks ADD COLUMN play_until TEXT NULL;
ALTER TABLE tracks ADD COLUMN play_days TEXT NULL;
ALTER TABLE tracks ADD COLUMN play_hours TEXT NULL;
ALTER TABLE tracks ADD COLUMN max_day INTEGER NULL;
ALTER TABLE tracks ADD COLUMN max_week INTEGER NULL;

UPDATE schema_info SET value = '14' WHERE name = 'version';
