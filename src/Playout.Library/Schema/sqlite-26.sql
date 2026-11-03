-- Sintoniza library, schema version 26 (SQLite): a permission to start and stop the stream by hand ("stream"), and
-- whether each studio is streaming.
-- Those who could change the station's or a computer's settings keep doing it; presenters don't, until it is given
-- to them (Sintoniza Pro → Users). Same as mysql-26.sql.

UPDATE users SET permissions = permissions || ',stream'
WHERE (',' || permissions || ',') NOT LIKE '%,stream,%'
  AND ((',' || permissions || ',') LIKE '%,station,%' OR (',' || permissions || ',') LIKE '%,computer,%');

-- Whether the studio is streaming (the computer on air's: a standby one going on air streams if it was).
ALTER TABLE studios ADD COLUMN streaming INTEGER NULL; -- null: not known (starting, or an older version)

UPDATE schema_info SET value = '26' WHERE name = 'version';
