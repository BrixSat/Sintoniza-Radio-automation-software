-- Sintoniza library, schema version 26 (MySQL / MariaDB): the "stream" permission, and whether each studio is streaming (see sqlite-26.sql).

UPDATE users SET permissions = CONCAT(permissions, ',stream')
WHERE CONCAT(',', permissions, ',') NOT LIKE '%,stream,%'
  AND (CONCAT(',', permissions, ',') LIKE '%,station,%' OR CONCAT(',', permissions, ',') LIKE '%,computer,%');

-- Whether the studio is streaming (the computer on air's: a standby one going on air streams if it was).
ALTER TABLE studios ADD COLUMN streaming TINYINT NULL; -- null: not known (starting, or an older version)

UPDATE schema_info SET value = '26' WHERE name = 'version';
