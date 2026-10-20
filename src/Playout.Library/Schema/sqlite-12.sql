-- Sintoniza library, schema version 12 (SQLite): the on-air program asks for a user too, with the new permission
-- onair. Everyone who could log in before may use it (they could, without logging in). Same as mysql-12.sql.

UPDATE users SET permissions = CASE WHEN permissions = '' THEN 'onair' ELSE permissions || ',onair' END
WHERE ',' || permissions || ',' NOT LIKE '%,onair,%';

UPDATE schema_info SET value = '12' WHERE name = 'version';
