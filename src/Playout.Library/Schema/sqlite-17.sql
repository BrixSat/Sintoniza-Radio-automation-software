-- Sintoniza library, schema version 17 (SQLite): the play history by station. plays.station_id (there since 3, unused
-- until now) says which station played it; what was played before is the first station's. Rotation, rest and the
-- music rules count each station's own plays. Same as mysql-17.sql.

UPDATE plays SET station_id = 1 WHERE station_id IS NULL;
CREATE INDEX IF NOT EXISTS plays_station_started ON plays (station_id, started_at);

UPDATE schema_info SET value = '17' WHERE name = 'version';
