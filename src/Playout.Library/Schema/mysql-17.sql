-- Sintoniza library, schema version 17 (MySQL / MariaDB): the play history by station (see sqlite-17.sql).

UPDATE plays SET station_id = 1 WHERE station_id IS NULL;
CREATE INDEX plays_station_started ON plays (station_id, started_at);

UPDATE schema_info SET value = '17' WHERE name = 'version';
