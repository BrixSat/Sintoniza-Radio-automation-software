-- Sintoniza library, schema version 27 (SQLite): a show is live (a presenter in the studio: ASSISTED once they say
-- they are there) or automated (it only names that part of the day; AUTO goes on). The shows there are stay live.
-- Same as mysql-27.sql.

ALTER TABLE schedule_entries ADD COLUMN show_live INTEGER NOT NULL DEFAULT 1;

UPDATE schema_info SET value = '27' WHERE name = 'version';
