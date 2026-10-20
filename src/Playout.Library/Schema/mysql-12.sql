-- Sintoniza library, schema version 12 (MySQL / MariaDB): the on-air program asks for a user too, with the new
-- permission onair. Everyone who could log in before may use it (they could, without logging in). Same as sqlite-12.sql.

UPDATE users SET permissions = CASE WHEN permissions = '' THEN 'onair' ELSE CONCAT(permissions, ',onair') END
WHERE CONCAT(',', permissions, ',') NOT LIKE '%,onair,%';

UPDATE schema_info SET value = '12' WHERE name = 'version';
