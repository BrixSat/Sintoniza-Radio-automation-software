# Library database

The station's database: an SQLite file on the studio computer, or a MySQL / MariaDB server that several studios
(and the schedule program on another computer) share (Settings → Library). It holds the music library, the
schedule and advertising traffic, the folders of each category, and the as-run log. The app reads and edits it, and
`radio-import` fills the library from the music folders. Other programs may read it and write to it too. This page
is the contract they rely on.

What stays on each computer: its settings (audio devices, the database login…), and, so the station keeps playing
when the database can't be reached, the playout's playlist and a copy of the last schedule it read.

Schema version **4**. The scripts that create the tables are
[`sqlite-1.sql`](../src/Playout.Library/Schema/sqlite-1.sql) and
[`mysql-1.sql`](../src/Playout.Library/Schema/mysql-1.sql), then
[`sqlite-2.sql`](../src/Playout.Library/Schema/sqlite-2.sql) and
[`mysql-2.sql`](../src/Playout.Library/Schema/mysql-2.sql) (cue points), then
[`sqlite-3.sql`](../src/Playout.Library/Schema/sqlite-3.sql) and
[`mysql-3.sql`](../src/Playout.Library/Schema/mysql-3.sql) (stations, and the tables of an earlier log-based design),
then [`sqlite-4.sql`](../src/Playout.Library/Schema/sqlite-4.sql) and
[`mysql-4.sql`](../src/Playout.Library/Schema/mysql-4.sql) (the schedule, traffic, category folders and as-run log;
the earlier design's tables are removed if they are empty). The app creates the tables itself the
first time it opens an empty database, and upgrades an older one, so only the database and a user with rights to
create and alter tables are needed:

```sql
CREATE DATABASE sintoniza CHARACTER SET utf8mb4;
CREATE USER 'sintoniza'@'%' IDENTIFIED BY '…';
GRANT ALL PRIVILEGES ON sintoniza.* TO 'sintoniza'@'%';
```

## Conventions

- **Times** are UTC text in the form `2026-10-14T18:20:11Z`.
- **Booleans** are `0` / `1`.
- **Paths** are full paths to the file as the studio computers see it. With a shared MySQL library, every studio
  must see the music under the same path, e.g. the same mapped drive or mount point. Paths are compared exactly
  (case and accents count) and may be up to 760 characters long.
- `schema_info` holds `version`. A program that finds a version it doesn't know should not write. The app
  refuses to open a library made by a newer version, so studios sharing one should run the same version.

## Tables

### `categories`

What a song or jingle is for the station. The library starts with Current, Recurrent, Gold, Jingles, IDs, Promos
and Ads; stations can rename them and add their own.

| Column | Type | |
| --- | --- | --- |
| `id` | integer | |
| `name` | text, unique | |
| `kind` | text | `music`, `jingle`, `commercial` or `other`: how the playout treats the category |
| `color` | text, null | `#RRGGBB`, shown in the app |
| `sort_order` | integer | order in lists |

### `tracks`

One row per audio file.

| Column | Type | |
| --- | --- | --- |
| `id` | integer | |
| `path` | text, unique | full path of the file |
| `file_size`, `file_modified` | integer, time | size in bytes and last write time; a scan reads a file again only when these change |
| `title`, `artist`, `album`, `genre` | text | from the tags (`''` if none) |
| `year` | integer, null | |
| `isrc` | text, null | for royalty reports |
| `duration` | real | seconds |
| `category_id` | integer, null | → `categories.id`; null = not categorised yet |
| `tempo` | integer, null | 1 (slow) … 5 (fast) |
| `voice` | text, null | `male`, `female`, `duo`, `group` or `instrumental` |
| `language` | text, null | short code: `pt`, `en`, … |
| `enabled` | 0/1 | 0 = never picked automatically (rotation) |
| `missing` | 0/1 | 1 = the last scan didn't find the file. Rows are kept, so category and history survive if the file comes back |
| `added_at`, `updated_at` | time | |
| `search` | text | what search matches against. **Insert `''`**: the app fills it in (lower case, accents removed) when it opens the library |
| `cue_in`, `intro`, `outro`, `mix`, `cue_out` | real, null | cue points in seconds from the start of the file; null = not set (see the README) |
| `cues_updated_at` | time, null | when the cue points were last set or cleared, by the **database server's** clock; null = never |

The app reads the cue points of files whose `cues_updated_at` changed every 30 seconds. A program that changes cue
points must set `cues_updated_at` to the server's current UTC time in the same form
(`DATE_FORMAT(UTC_TIMESTAMP(), '%Y-%m-%dT%H:%i:%sZ')` in MySQL, `strftime('%Y-%m-%dT%H:%M:%SZ', 'now')` in
SQLite), or the studios won't see the change until they restart.

### `plays`

Every item that went on air: the as-run history. Rotation rules (artist and title separation) and reports use it.

| Column | Type | |
| --- | --- | --- |
| `id` | integer | |
| `track_id` | integer, null | → `tracks.id` when the file is in the library |
| `path`, `artist`, `title` | text | as played, so the history stays readable if the track is deleted |
| `started_at` | time | when it went on air |
| `source` | text | `playlist` or `cart` |
| `station_id`, `log_item_id`, `ended_at`, `kind` | | from schema 3, not used |

### `category_folders`

The folders given to a category (Settings → Library → Categories…, or right-click a folder): their songs are in it,
and new songs found there by a scan get it too.

| Column | Type | |
| --- | --- | --- |
| `path` | text, key | the folder, as the studio computers see it |
| `category_id` | integer | → `categories.id` |

### `as_run`

What went on air, with its start and end: what the reports (music for royalties, proof of broadcast for
advertisers) are made from. Each studio also keeps its own CSV files and puts in later what the database missed.

| Column | Type | |
| --- | --- | --- |
| `id` | integer | |
| `station_id` | integer | → `stations.id` |
| `started_at`, `ended_at` | text | local time, `yyyy-MM-ddTHH:mm:ss`; `ended_at` null if the app stopped while it played |
| `type` | text | `Music`, `Jingle`, `Advert`, `Stream`, `Programme` |
| `source` | text | `playlist` or `cart` |
| `event`, `advertiser` | text | the scheduled event or ad break (`Anuncios 10:59`), and whose advert it is |
| `artist`, `title`, `album`, `composer`, `label`, `isrc`, `year` | text | from the file's tags |
| `file` | text | the file played; `station_id`, `started_at` and `file` together are unique |

## The schedule

What the schedule program (Sintoniza Schedule) edits, for each station (`station_id`; one station, 1, for now).
It saves everything at once, as a new version in `schedule_versions`; the playout reads it again when the version
changes. Ids are text (`CHAR(36)`, a GUID). Days: `weekday` 0 = Sunday … 6 = Saturday; times `HH:mm:ss`; dates
`yyyy-MM-dd`.

| Table | |
| --- | --- |
| `schedule_entries` | `type` `event` (a file, the newest file in a folder, or a stream at set times), `break` (an ad break) or `playlist` (an hour template); `name`, `enabled`, `start_mode` (`Soft`, `Hard`, `EndAt`), `max_delay_minutes`, `lock_level` (`Free`, `Movable`, `Locked`); for events `path`, `stream_minutes`, `max_age_minutes`; `position` keeps the order of the lists |
| `schedule_slots` | `entry_id`, `weekday`, `time`: when each entry runs, every week (for an `EndAt` break, when it ends) |
| `break_parts` | a break's running order: `position`, `kind` (`Jingle`, `Section`), `path` of a jingle, `section` name, `only_with_adverts` (a section opener) |
| `playlist_lines` | an hour template's lines, in `position` order: `source` `Category` (`category_id`, `category_name`, `kind`) or `Folder` (`folder`) |
| `ad_clients` | advertisers: `name` (on the proof of broadcast), `enabled` |
| `ad_campaigns` | a client's campaigns: `name`, `enabled`, `starts_on` and `ends_on` (null = no limit), `section` of the break they go in (`''` = its first), `place` (0 automatic, 1–5 from the start of the section, 6–10 at its end) |
| `ad_campaign_files` | a campaign's adverts (they take turns): `position`, `path` |
| `ad_placements` | where a campaign plays: the break at `time`, every week on `weekday`, or only on `date` |
| `schedule_state` | `entry_id`, `last_started`: when the playout last started each event and break (local time), so a restart doesn't start it again; not touched by a save |
| `schedule_versions` | `station_id`, `version`, `updated_at` (UTC), `updated_by`: a save over a newer version is asked about first |

## Adding music from another program

Insert into `tracks` with at least `path`, `title`, `artist`, `duration`, `added_at`, `updated_at` and
`search = ''`. Set `file_size` and `file_modified` too if you can. Otherwise `radio-import` reads the file again the
next time it scans that folder, keeping its id, category and attributes.

## radio-import

```
radio-import                          scan the folders set in Settings → Library into the app's library
radio-import /music /jingles          scan these folders
radio-import --db /path/library.db …  an SQLite library file
radio-import --mysql user:password@server[:port]/database …
radio-import --stats                  what is in the library
```

Only new and changed files are opened, and only their tags and headers are read, so it can run every few minutes
from cron or Task Scheduler (the app also scans by itself; see Settings → Library). Several scans at once, for
example from studios sharing a MySQL library, are safe. A folder that can't be reached (for example a network share that is offline) is
skipped, and nothing in it is marked missing.
